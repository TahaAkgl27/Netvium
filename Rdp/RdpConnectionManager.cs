using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Timers;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Rdp
{
    public sealed class RdpConnectionManager : IDisposable
    {
        private readonly Control _uiInvokeTarget;
        private readonly Action<Action> _marshalToUi;
        private readonly Func<string, EmbeddedRdpClient> _clientFactory;
        private readonly Action<EmbeddedRdpClient> _attachClient;
        private readonly Action? _detachClient;
        private readonly Func<EmbeddedRdpClient, RdpHostLayoutSnapshot>? _applyLayoutAndCapture;

        private readonly object _gate = new();

        private EmbeddedRdpClient? _client;
        private Action? _onConnecting;
        private Action? _onLoginComplete;
        private Action? _onConnected;
        private Action<int>? _onDisconnected;
        private Action<int>? _onFatalError;
        private Action<int>? _onLogonError;
        private Action<int>? _onWarning;
        private Action<int>? _onAuthWarningDisplayed;
        private Action<int>? _onAuthWarningDismissed;
        private Action<int>? _onDisconnectedWithReconnect;
        private Action<string>? _tryCompleteConnection;
        private CancellationTokenSource? _connectCts;
        private System.Timers.Timer? _pollTimer;

        private DateTime _attemptStartedUtc;
        private DateTime _connectDeadlineUtc;
        private int _lastClientState = -1;

        private ServerInfo? _lastServer;
        private int _lastWidth;
        private int _lastHeight;
        private bool _isManualDisconnect;
        private int _reconnectAttempts;
        private bool _connectedReceived;
        private RdpConnectionAttemptDiagnostics _diagnostics = new();
        private RdpConnectionTimeline? _timeline;

        public RdpConnectionTimeline? CurrentTimeline => _timeline;

        public int MaxReconnectAttempts { get; set; } = 3;
        public bool AutoReconnectEnabled { get; set; } = false;

        public event Action<string>? Status;
        public event Action<string>? ActiveProfileChanged;
        public event Action? Connecting;
        public event Action? Connected;
        public event Action<RdpConnectionFailureInfo>? ConnectionFailed;
        public event Action<RdpConnectionState, RdpConnectionAttemptDiagnostics>? StateChanged;
        public event Action<int, int>? Disconnected;
        public event Action<string>? LiveTimeline;

        public IReadOnlyList<RdpSecurityProfile> Rotation { get; set; } = (IReadOnlyList<RdpSecurityProfile>)RdpSecurityProfiles.DefaultRotation;

        public string DefaultDomain { get; set; } = "";

        /// <summary>Total time allowed for a connect attempt (all profile retries included).</summary>
        public int ConnectTimeoutSeconds { get; set; } = RdpConnectDeadline.ConnectPhaseTimeoutSeconds;

        public RdpConnectionState ConnectionState { get; private set; } = RdpConnectionState.Idle;
        public RdpConnectionAttemptDiagnostics LastDiagnostics => _diagnostics.Clone();

        public bool IsConnected => ConnectionState.IsSessionConnected();
        public bool IsConnecting => ConnectionState.IsInProgress();
        public RdpState SessionState => _client?.State ?? RdpState.Disconnected;
        public bool IsReadyForCapture => ConnectionState.IsSessionConnected();

        public int? TryGetClientState() => _client?.TryGetClientState();
        public string? ActiveProfileLabel { get; private set; }
        public DateTime? ConnectedAtUtc { get; private set; }

        public EmbeddedRdpClient? Client => _client;

        public RdpConnectionManager(
            Control uiInvokeTarget,
            Action<EmbeddedRdpClient> attachClient,
            Action? detachClient = null,
            Func<string, EmbeddedRdpClient>? clientFactory = null,
            Action<Action>? marshalToUi = null,
            Func<EmbeddedRdpClient, RdpHostLayoutSnapshot>? applyLayoutAndCapture = null)
        {
            _uiInvokeTarget = uiInvokeTarget;
            _marshalToUi = marshalToUi ?? (action =>
            {
                if (_uiInvokeTarget.IsHandleCreated && !_uiInvokeTarget.IsDisposed)
                {
                    if (_uiInvokeTarget.InvokeRequired)
                        _uiInvokeTarget.BeginInvoke(action);
                    else
                        action();
                }
                else
                {
                    action();
                }
            });
            _attachClient = attachClient;
            _detachClient = detachClient;
            _applyLayoutAndCapture = applyLayoutAndCapture;
            _clientFactory = clientFactory ?? (clsid => new EmbeddedRdpClient(clsid));
        }

        public void Dispose()
        {
            Disconnect();
            DestroyClient(fullGc: false);
        }

        public void Disconnect()
        {
            lock (_gate)
            {
                _isManualDisconnect = true;
                _reconnectAttempts = 0;
                _connectCts?.Cancel();
                _connectCts?.Dispose();
                _connectCts = null;
            }

            StopPollTimer();

            try
            {
                InvokeOnUi(() =>
                {
                    try { _client?.SafeDisconnect(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                });
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            if (ConnectionState == RdpConnectionState.LoginComplete
                || ConnectionState == RdpConnectionState.Connected)
            {
                TransitionState(RdpConnectionState.Disconnected, "User disconnected");
            }
            else if (ConnectionState.IsInProgress())
            {
                TransitionState(RdpConnectionState.Cancelled, "Connection cancelled by user");
                FinishTimeline("Cancelled");
            }
        }

        public Task ConnectAsync(ServerInfo server, int width, int height, CancellationToken externalCt)
        {
            lock (_gate)
            {
                if (ConnectionState.IsInProgress())
                {
                    _connectCts?.Cancel();
                }

                _isManualDisconnect = false;
                _reconnectAttempts = 0;
                _connectCts?.Dispose();
                _connectCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
                _lastServer = server;
                _lastWidth = width;
                _lastHeight = height;
                return ConnectInternalAsync(server, width, height, _connectCts.Token);
            }
        }

        public Task ReconnectAsync(CancellationToken ct)
        {
            var s = _lastServer;
            if (s == null) return Task.CompletedTask;
            return ConnectAsync(s, _lastWidth, _lastHeight, ct);
        }

        public void UpdateResolution(int width, int height)
        {
            _lastWidth = width;
            _lastHeight = height;
        }

        public void ApplyConnectionQuality(RdpConnectionQuality quality) =>
            InvokeOnUiSafe(() => _client?.ApplyConnectionQuality(quality));

        public void SendCtrlAltDel() =>
            InvokeOnUiSafe(() => _client?.SendCtrlAltDel());

        public Task<bool> TryExecuteRemoteShellCommandAsync(string command)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                InvokeOnUi(async () =>
                {
                    try
                    {
                        if (_client == null || !_client.IsConnected)
                        {
                            tcs.TrySetResult(false);
                            return;
                        }

                        var ok = await _client.ExecuteRunDialogCommandAsync(command);
                        tcs.TrySetResult(ok);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Remote shell command failed.", ex);
                        tcs.TrySetResult(false);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Remote shell command dispatch failed.", ex);
                tcs.TrySetResult(false);
            }

            return tcs.Task;
        }

        public void ForceRefresh() => InvokeOnUiSafe(() => _client?.ForceRefresh());
        public void SyncDisplaySettings() => InvokeOnUiSafe(() => _client?.SyncSessionDisplaySettings());
        public void EnsureHostFillsParent() => InvokeOnUiSafe(() => _client?.EnsureHostFillsParent());
        public void ApplyPerformanceLevel(string level) => InvokeOnUiSafe(() => _client?.ApplyPerformanceLevel(level));
        public void SetClipboardRedirect(bool enabled) => InvokeOnUiSafe(() => _client?.SetClipboardRedirect(enabled));
        public void SetKeyboardCapture(bool enabled) => InvokeOnUiSafe(() => _client?.SetKeyboardCapture(enabled));
        public void SetMouseCapture(bool enabled) => InvokeOnUiSafe(() => _client?.SetMouseCapture(enabled));

        private void InvokeOnUiSafe(Action action)
        {
            try
            {
                InvokeOnUi(action);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Operation failed.", ex);
            }
        }

        private async Task ConnectInternalAsync(ServerInfo server, int width, int height, CancellationToken ct)
        {
            var port = server.Port <= 0 ? 3389 : server.Port;
            var endpoint = $"{server.Ip}:{port}";

            _attemptStartedUtc = DateTime.UtcNow;
            _connectDeadlineUtc = RdpConnectDeadline.CreateUtc(ConnectTimeoutSeconds);
            _lastClientState = -1;
            _connectedReceived = false;

            _timeline = new RdpConnectionTimeline(server.Ip, port, _connectDeadlineUtc);
            _timeline.RecordStartConnect(server.Name, profileLabel: null);

            _diagnostics = new RdpConnectionAttemptDiagnostics
            {
                ServerName = server.Name,
                Host = server.Ip,
                Port = port,
                HasUsername = !string.IsNullOrWhiteSpace(server.Username),
                ConnectStartedUtc = _attemptStartedUtc,
                LastEvent = "ConnectStarted",
                FinalState = RdpConnectionState.Connecting,
                ConnectPhaseTimeoutSeconds = ConnectTimeoutSeconds,
                ApartmentState = Thread.CurrentThread.GetApartmentState().ToString()
            };

            RdpConnectionLogger.AttemptStarted(_diagnostics);
            RdpConnectionLogger.ConnectStarted(server.Ip, port);

            Emit($"Checking TCP {endpoint}...");
            _diagnostics.TcpReachable = await RdpPortProbe.CheckAsync(server.Ip, port, RdpPortProbe.DefaultTimeoutMs, ct).ConfigureAwait(false);
            _timeline?.Record("TCP port probe", $"reachable={_diagnostics.TcpReachable}");

            if (_diagnostics.TcpReachable != true)
            {
                var msg = port == 3389
                    ? "RDP port 3389 is unreachable."
                    : $"RDP port {port} is unreachable.";
                FailTerminal(RdpConnectionState.Failed, msg, userMessage: msg);
                FinishTimeline("TcpUnreachable");
                return;
            }

            PushLiveTimeline("TCP_OK");
            _timeline?.Record("TCP_OK", $"host={server.Ip} port={port}");

            TransitionState(RdpConnectionState.Connecting);

            try
            {
                ct.ThrowIfCancellationRequested();

                if (width <= 0 || height <= 0)
                {
                    FailTerminal(
                        RdpConnectionState.Failed,
                        "RDP host size is not ready.",
                        userMessage: "RDP host boyutu hazır değil.");
                    FinishTimeline("Failed");
                    return;
                }

                Emit($"{endpoint} connecting...");
                foreach (var version in RdpActiveXVersions.FallbackOrder)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!RdpActiveXVersions.IsRegistered(version.Clsid))
                    {
                        _timeline?.Record("ActiveX skipped", $"version={version.Label} clsid={version.Clsid} reason=not registered");
                        PushLiveTimeline($"SKIP_{version.Label}=NOT_REGISTERED");
                        continue;
                    }

                    if (!_connectedReceived && RdpConnectDeadline.IsExpired(_connectDeadlineUtc))
                    {
                        FailTerminal(
                            RdpConnectionState.TimedOut,
                            BuildEmbeddedTimeoutMessage(),
                            lastEvent: "Timeout");
                        FinishTimeline("TimedOut");
                        return;
                    }

                    _diagnostics.ProfileLabel = version.Label;
                    _diagnostics.ActiveXVersion = version.Label;
                    ActiveProfileChanged?.Invoke($"Connecting ({version.Label})...");
                    _timeline?.Record("ActiveX attempt", $"version={version.Label} clsid={version.Clsid}");

                    var ok = await TryConnectOnceAsync(server, port, version.Clsid, version.Label, width, height, ct).ConfigureAwait(false);
                    if (ok)
                    {
                        FinishTimeline(ConnectionState.ToString());
                        return;
                    }
                }

                if (!_connectedReceived && RdpConnectDeadline.IsExpired(_connectDeadlineUtc))
                {
                    FailTerminal(
                        RdpConnectionState.TimedOut,
                        BuildEmbeddedTimeoutMessage(),
                        lastEvent: "Timeout");
                    FinishTimeline("TimedOut");
                }
                else
                {
                    var failMsg = _diagnostics.TcpReachable == true
                        ? "Embedded ActiveX failure: connection failed. Try Open with Windows Remote Desktop."
                        : $"{endpoint} connection failed.";
                    FailTerminal(
                        RdpConnectionState.Failed,
                        failMsg,
                        userMessage: failMsg);
                    FinishTimeline("Failed");
                }
            }
            catch (OperationCanceledException)
            {
                if (!_isManualDisconnect)
                {
                    TransitionState(RdpConnectionState.Cancelled, "Connection cancelled");
                    FinishTimeline("Cancelled");
                }
            }
        }

        private void FinishTimeline(string outcome)
        {
            _timeline?.Finish(outcome);
            _timeline = null;
            if (_client != null)
                _client.ActiveTimeline = null;
        }

        private Task<bool> TryConnectOnceAsync(
            ServerInfo server,
            int port,
            string clsid,
            string versionLabel,
            int width,
            int height,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            MarshalToUi(() =>
            {
                try
                {
                    StopPollTimer();
                    DestroyClient(fullGc: false);

                    if (width <= 0 || height <= 0)
                    {
                        Emit("RDP host size is 0 — connect aborted.");
                        SafeTrySet(tcs, false);
                        return;
                    }

                    _client = _clientFactory(clsid);
                    _client.ActiveTimeline = _timeline;
                    ActiveProfileLabel = versionLabel;
                    _timeline?.RecordComRegistration(clsid);
                    _client.PrepareFreshControl();
                    _attachClient(_client);
                    PushLiveTimeline("ACTIVEX_CREATED");
                    _timeline?.Record("ACTIVEX_CREATED", $"clsid={clsid} version={versionLabel}");
                    EnsureHandle(_client);

                    RdpHostLayoutSnapshot? layout = null;
                    var layoutDeadline = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < layoutDeadline)
                    {
                        layout = _applyLayoutAndCapture?.Invoke(_client);
                        if (layout?.IsConnectReady == true)
                            break;
                        Application.DoEvents();
                        Thread.Sleep(50);
                    }

                    layout ??= _applyLayoutAndCapture?.Invoke(_client);
                    var layoutLog = layout?.ToLogBlock() ?? "layout snapshot unavailable";
                    _timeline?.Record("PreConnect layout", layoutLog);
                    PushLiveTimeline("LAYOUT_SNAPSHOT");
                    foreach (var line in layoutLog.Split('\n'))
                        PushLiveTimeline(line.Trim());

                    if (layout?.IsConnectReady != true)
                    {
                        const string layoutError = "RDP viewer layout is not ready. Host size is 0x0.";
                        _diagnostics.FailureReason = layoutError;
                        _diagnostics.LiveTimeline += Environment.NewLine + layoutLog;
                        PushLiveTimeline("LAYOUT_NOT_READY");
                        Emit(layoutError);
                        FailConnectionAttempt(
                            tcs,
                            width,
                            height,
                            RdpConnectionState.Failed,
                            layoutError,
                            layoutError,
                            finalizeAttempt: true);
                        return;
                    }

                    if (!_client.EnsureReadyForConnect())
                    {
                        PushLiveTimeline("HWND_READY=NO");
                        Emit("RDP control HWND/parent missing.");
                        SafeTrySet(tcs, false);
                        return;
                    }

                    PushLiveTimeline($"HWND_READY=0x{_client.Handle:X}");
                    _client.EnsureOcxEventsHooked();
                    _timeline?.RecordControlVerification(_client);

                    var connectW = layout.PanelClientWidth > 0
                        ? layout.PanelClientWidth
                        : layout.HostPixelWidth > 0 ? layout.HostPixelWidth : layout.ActiveXClientWidth;
                    var connectH = layout.PanelClientHeight > 0
                        ? layout.PanelClientHeight
                        : layout.HostPixelHeight > 0 ? layout.HostPixelHeight : layout.ActiveXClientHeight;
                    if (connectW <= 0 || connectH <= 0)
                    {
                        Emit("RDP control size is 0 — connect aborted.");
                        SafeTrySet(tcs, false);
                        return;
                    }

                    _client.SetExplicitSize(connectW, connectH);
                    var (verifyW, verifyH) = _client.GetActualClientSize();
                    PushLiveTimeline($"CONTROL_SIZE={connectW}x{connectH} verified={verifyW}x{verifyH}");

                    if (verifyW <= 0 || verifyH <= 0)
                    {
                        const string layoutError = "RDP viewer layout is not ready. Host size is 0x0.";
                        FailConnectionAttempt(
                            tcs,
                            connectW,
                            connectH,
                            RdpConnectionState.Failed,
                            layoutError,
                            layoutError,
                            finalizeAttempt: true);
                        return;
                    }

                    HookClientEvents(_client, tcs);

                    var (normalized, dom, userOnly) = RdpCredentialHelper.NormalizeUsername(server.Username, DefaultDomain);
                    _lastClientState = -1;

                    try
                    {
                        PushLiveTimeline("CONNECT_CALLED");
                        _client.ConnectMstscCompatible(
                            server.Ip,
                            port,
                            normalized,
                            dom,
                            userOnly,
                            server.Password,
                            connectW,
                            connectH);
                        PushLiveTimeline("CONNECT_RETURNED");
                    }
                    catch (COMException com)
                    {
                        _diagnostics.ComHResult = com.ErrorCode;
                        _client.RecordComError(com, "Connect");
                        RecordEvent("ConnectCOMException", com.ErrorCode);
                        _timeline?.Record("Connect COM exception", $"HRESULT=0x{com.ErrorCode:X8}", com);
                        PushLiveTimeline($"LAST_ERROR=HRESULT 0x{com.ErrorCode:X8}");
                        Emit($"Connect COM error: 0x{com.ErrorCode:X8}");
                        FailConnectionAttempt(
                            tcs,
                            connectW,
                            connectH,
                            RdpConnectionState.Failed,
                            $"COM error 0x{com.ErrorCode:X8}: {com.Message}",
                            $"COM error 0x{com.ErrorCode:X8}: {com.Message}",
                            com.ErrorCode,
                            finalizeAttempt: false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        RecordEvent("ConnectException", errorCode: -1);
                        _timeline?.Record("Connect exception", null, ex);
                        PushLiveTimeline($"LAST_ERROR={ex.GetType().Name}");
                        Emit($"Connect error: {ex.Message}");
                        FailConnectionAttempt(
                            tcs,
                            connectW,
                            connectH,
                            RdpConnectionState.Failed,
                            ex.Message,
                            ex.Message,
                            finalizeAttempt: false);
                        return;
                    }

                    StartPollTimer(tcs, ct, connectW, connectH);
                }
                catch (Exception ex)
                {
                    RecordEvent("ConnectInitException", errorCode: -1);
                    _timeline?.Record("Connect init exception", null, ex);
                    Emit($"Connect init error: {ex.Message}");
                    SafeTrySet(tcs, false);
                }
            });

            return tcs.Task;
        }

        private void HookClientEvents(EmbeddedRdpClient client, TaskCompletionSource<bool> tcs)
        {
            UnhookClientEvents();

            void AcceptConnected(string source)
            {
                _connectedReceived = true;

                if (source == "OnLoginComplete")
                    RecordEvent("OnLoginComplete");
                else
                    RecordEvent("OnConnected");

                if (tcs.Task.IsCompleted)
                {
                    if (source == "OnLoginComplete")
                        TransitionState(RdpConnectionState.LoginComplete);
                    return;
                }

                ConnectedAtUtc = DateTime.UtcNow;
                StopPollTimer();
                TransitionState(
                    source == "OnLoginComplete"
                        ? RdpConnectionState.LoginComplete
                        : RdpConnectionState.Connected);
                Emit($"Connected ({source}).");
                Connected?.Invoke();
                SafeTrySet(tcs, true);
            }

            _tryCompleteConnection = source => AcceptConnected(source);

            _onConnecting = () =>
            {
                RecordEvent("OnConnecting");
                Emit("Connecting...");
                Connecting?.Invoke();
            };

            _onConnected = () => AcceptConnected("OnConnected");

            _onLoginComplete = () =>
            {
                AcceptConnected("OnLoginComplete");
                try { client.ApplyDesktopSizeAfterLoginPublic(); } catch { /* optional */ }
            };

            _onDisconnected = reason =>
            {
                var wasInProgress = ConnectionState.IsInProgress();
                StopPollTimer();
                RecordEvent("OnDisconnected", reason);
                var ext = client.TryGetExtendedDisconnectReason();
                Emit($"Disconnected ({reason}, ext={ext}).");
                Disconnected?.Invoke(reason, ext);

                if (!tcs.Task.IsCompleted)
                {
                    SafeTrySet(tcs, false);
                    if (wasInProgress)
                        RecordEvent("OnDisconnected", reason);
                }
                else if (ConnectionState.IsSessionConnected())
                {
                    TransitionState(RdpConnectionState.Disconnected, $"Disconnected (reason={reason})", reason);
                }

                TryAutoReconnect();
            };

            _onFatalError = code =>
            {
                StopPollTimer();
                RecordEvent("OnFatalError", code);
                if (!tcs.Task.IsCompleted)
                {
                    FailConnectionAttempt(
                        tcs,
                        0,
                        0,
                        RdpConnectionState.Failed,
                        $"Fatal RDP error (code={code}).",
                        $"Fatal error: {code}",
                        code,
                        finalizeAttempt: true);
                }
            };

            _onLogonError = code =>
            {
                StopPollTimer();
                RecordEvent("OnLogonError", code);
                if (!tcs.Task.IsCompleted)
                {
                    FailConnectionAttempt(
                        tcs,
                        0,
                        0,
                        RdpConnectionState.Failed,
                        $"Logon error (code={code}). Check username, domain, or password.",
                        $"Logon error (code={code}). Check username, domain, or password.",
                        code,
                        finalizeAttempt: true);
                }
            };

            _onWarning = code =>
            {
                RecordEvent("OnWarning", code);
                Emit($"RDP warning: {code}");
            };

            _onAuthWarningDisplayed = code =>
            {
                RecordEvent("OnAuthenticationWarningDisplayed", code);
                Emit($"Authentication warning displayed: {code}");
            };

            _onAuthWarningDismissed = code =>
            {
                RecordEvent("OnAuthenticationWarningDismissed", code);
                Emit($"Authentication warning dismissed: {code}");
            };

            client.OnConnecting += _onConnecting;
            client.OnLoginComplete += _onLoginComplete;
            client.OnConnected += _onConnected;
            client.OnLogonError += _onLogonError;
            client.OnDisconnected += _onDisconnectedWithReconnect = reason => _onDisconnected?.Invoke(reason);
            client.OnFatalError += _onFatalError;
            client.OnWarning += _onWarning;
            client.OnAuthenticationWarningDisplayed += _onAuthWarningDisplayed;
            client.OnAuthenticationWarningDismissed += _onAuthWarningDismissed;
        }

        private void UnhookClientEvents()
        {
            if (_client == null) return;

            if (_onConnecting != null) { _client.OnConnecting -= _onConnecting; _onConnecting = null; }
            if (_onLoginComplete != null) { _client.OnLoginComplete -= _onLoginComplete; _onLoginComplete = null; }
            if (_onConnected != null) { _client.OnConnected -= _onConnected; _onConnected = null; }
            if (_onLogonError != null) { _client.OnLogonError -= _onLogonError; _onLogonError = null; }
            if (_onDisconnectedWithReconnect != null) { _client.OnDisconnected -= _onDisconnectedWithReconnect; _onDisconnectedWithReconnect = null; }
            if (_onFatalError != null) { _client.OnFatalError -= _onFatalError; _onFatalError = null; }
            if (_onWarning != null) { _client.OnWarning -= _onWarning; _onWarning = null; }
            if (_onAuthWarningDisplayed != null) { _client.OnAuthenticationWarningDisplayed -= _onAuthWarningDisplayed; _onAuthWarningDisplayed = null; }
            if (_onAuthWarningDismissed != null) { _client.OnAuthenticationWarningDismissed -= _onAuthWarningDismissed; _onAuthWarningDismissed = null; }

            _onDisconnected = null;
            _tryCompleteConnection = null;
        }

        private void StartPollTimer(TaskCompletionSource<bool> tcs, CancellationToken ct, int hostWidth, int hostHeight)
        {
            StopPollTimer();
            _pollTimer = new System.Timers.Timer(250);
            _pollTimer.AutoReset = true;
            _pollTimer.Elapsed += (_, _) =>
            {
                if (ct.IsCancellationRequested)
                {
                    StopPollTimer();
                    return;
                }

                if (!_connectedReceived && RdpConnectDeadline.IsExpired(_connectDeadlineUtc) && !tcs.Task.IsCompleted)
                {
                    MarshalToUi(() =>
                    {
                        _client?.LogSessionSnapshot("HandshakeTimeout", hostWidth, hostHeight);
                        FailConnectionAttempt(
                            tcs,
                            hostWidth,
                            hostHeight,
                            RdpConnectionState.TimedOut,
                            BuildEmbeddedTimeoutMessage(),
                            BuildEmbeddedTimeoutMessage(),
                            lastEvent: "Timeout",
                            finalizeAttempt: true);
                    });
                    return;
                }

                if (tcs.Task.IsCompleted)
                    return;

                var elapsed = (DateTime.UtcNow - _attemptStartedUtc).TotalSeconds;
                MarshalToUi(() => PollConnectionProgress(tcs, hostWidth, hostHeight, elapsed));
            };
            _pollTimer.Start();
        }

        private void PollConnectionProgress(TaskCompletionSource<bool> tcs, int hostWidth, int hostHeight, double elapsed)
        {
            if (_client == null || tcs.Task.IsCompleted)
                return;

            var connectedProp = _client.TryGetOcxConnected();
            var statusText = _client.TryGetStatusText();
            _diagnostics.LastStatusText = statusText;
            if (_client.LastComHResult.HasValue)
                _diagnostics.ComHResult = _client.LastComHResult;

            PushLiveTimeline(
                $"CONNECTED_PROPERTY={connectedProp?.ToString() ?? "?"} "
                + $"STATUS={statusText ?? "—"} "
                + $"LAST_EVENT={_diagnostics.LastEvent} "
                + $"LAST_ERROR={(_diagnostics.ErrorCode?.ToString() ?? "—")}");

            if (connectedProp == 1 && !_connectedReceived)
            {
                _tryCompleteConnection?.Invoke("Poll:ConnectedProperty");
                return;
            }

            var state = _client.TryGetClientState();
            if (state.HasValue && state.Value != _lastClientState)
            {
                _lastClientState = state.Value;
                _diagnostics.ClientState = state.Value;
                _timeline?.Record("Poll ClientState", $"value={state.Value} elapsed={elapsed:F1}s connected={connectedProp}");
                _client.LogSessionLine($"ClientState={state.Value} Connected={connectedProp} elapsed={elapsed:F1}s");
                AppLogger.Debug($"ClientState={state.Value} Connected={connectedProp}", category: "RDP", action: "ClientState");
            }

            _client.SyncState();

            if (!_connectedReceived)
            {
                try
                {
                    if (_client.State is RdpState.Connected or RdpState.LoginComplete)
                        _tryCompleteConnection?.Invoke("Poll:SyncState");
                    else if (_client.TryGetClientState() == 0)
                        _tryCompleteConnection?.Invoke("Poll:ClientStateActive");
                }
                catch { /* optional */ }
            }

            if (_connectedReceived || _client.State == RdpState.LoginComplete)
                return;

            if (_client.LastLogonErrorCode != 0)
            {
                PushLiveTimeline($"LAST_ERROR=LogonError {_client.LastLogonErrorCode}");
                FailConnectionAttempt(
                    tcs,
                    hostWidth,
                    hostHeight,
                    RdpConnectionState.Failed,
                    $"Logon error (code={_client.LastLogonErrorCode}). Check username, domain, or password.",
                    $"Logon error (code={_client.LastLogonErrorCode}). Check username, domain, or password.",
                    _client.LastLogonErrorCode,
                    finalizeAttempt: false);
            }
        }

        private void FailConnectionAttempt(
            TaskCompletionSource<bool> tcs,
            int hostWidth,
            int hostHeight,
            RdpConnectionState terminalState,
            string reason,
            string userMessage,
            int? errorCode = null,
            string? lastEvent = null,
            bool finalizeAttempt = false)
        {
            if (tcs.Task.IsCompleted)
                return;

            StopPollTimer();
            _client?.LogSessionSnapshot("FailConnectionAttempt", hostWidth, hostHeight);
            Emit(userMessage);
            SafeTrySet(tcs, false);

            try
            {
                UnhookClientEvents();
                _client?.SafeDisconnect();
            }
            catch (Exception ex)
            {
                AppLogger.Error("RDP disconnect after failure failed.", ex);
            }

            DestroyClient(fullGc: false);

            if (finalizeAttempt && !ConnectionState.IsTerminal())
                FailTerminal(terminalState, reason, errorCode, userMessage, lastEvent);
        }

        private void FailTerminal(
            RdpConnectionState terminalState,
            string reason,
            int? errorCode = null,
            string? userMessage = null,
            string? lastEvent = null)
        {
            if (ConnectionState.IsTerminal())
                return;

            if (terminalState == RdpConnectionState.TimedOut)
                RdpConnectionLogger.ConnectionTimeout(_lastServer?.Ip, ConnectTimeoutSeconds);

            RecordEvent(lastEvent ?? terminalState.ToString(), errorCode);
            TransitionState(terminalState, reason, errorCode);

            var message = userMessage ?? reason;
            ConnectionFailed?.Invoke(new RdpConnectionFailureInfo
            {
                State = terminalState,
                Message = message,
                Diagnostics = LastDiagnostics
            });
        }

        private void RecordEvent(string eventName, int? errorCode = null)
        {
            _diagnostics.LastEvent = eventName;
            _diagnostics.ErrorCode = errorCode;
            _diagnostics.ClientState = _client?.TryGetClientState();
            PushLiveTimeline($"LAST_EVENT={eventName}");
            if (errorCode.HasValue)
                PushLiveTimeline($"LAST_ERROR={errorCode.Value}");
            RdpConnectionLogger.AttemptEvent(_lastServer?.Ip, eventName, errorCode);
        }

        private void PushLiveTimeline(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (_diagnostics.LiveTimeline.Length > 0)
                _diagnostics.LiveTimeline += Environment.NewLine;
            _diagnostics.LiveTimeline += line;

            LiveTimeline?.Invoke(line);
            _timeline?.Record("Live", line);
        }

        private string BuildEmbeddedTimeoutMessage() =>
            _diagnostics.TcpReachable == true
                ? "Embedded ActiveX failure: connection timed out waiting for RDP server response."
                : "Connection timed out waiting for RDP server response.";

        private void TransitionState(RdpConnectionState state, string? reason = null, int? errorCode = null)
        {
            ConnectionState = state;
            _diagnostics.FinalState = state;
            _diagnostics.ElapsedMs = RdpConnectDeadline.ElapsedMs(_attemptStartedUtc);
            if (reason != null)
                _diagnostics.FailureReason = reason;
            if (errorCode.HasValue)
                _diagnostics.ErrorCode = errorCode;

            if (state.IsTerminal())
                RdpConnectionLogger.AttemptFinalized(_diagnostics);

            StateChanged?.Invoke(state, LastDiagnostics);
        }

        private void StopPollTimer()
        {
            if (_pollTimer == null) return;
            try
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to stop RDP poll timer.", ex);
            }
            finally
            {
                _pollTimer = null;
            }
        }

        private void TryAutoReconnect()
        {
            if (_isManualDisconnect || !AutoReconnectEnabled) return;
            if (_reconnectAttempts >= MaxReconnectAttempts) return;
            if (_lastServer == null) return;

            _reconnectAttempts++;
            Emit($"Reconnecting ({_reconnectAttempts}/{MaxReconnectAttempts})...");
            GlobalExceptionHandler.Observe(
                ReconnectAsync(CancellationToken.None),
                "RDP.AutoReconnect",
                notifyUser: false);
        }

        private void DestroyClient(bool fullGc)
        {
            StopPollTimer();
            try
            {
                InvokeOnUi(() =>
                {
                    UnhookClientEvents();
                    try { _detachClient?.Invoke(); } catch (Exception ex) { AppLogger.Error("RDP detach failed.", ex); }
                    if (_client != null)
                    {
                        try { _client.SafeDisconnect(); } catch (Exception ex) { AppLogger.Error("RDP disconnect failed.", ex); }
                        try { _client.Dispose(); } catch (Exception ex) { AppLogger.Error("RDP client dispose failed.", ex); }
                        _client = null;
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("DestroyClient failed.", ex);
            }

            if (fullGc)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void InvokeOnUi(Action action)
        {
            if (_client != null && _client.IsHandleCreated && !_client.IsDisposed)
            {
                if (_client.InvokeRequired)
                    _client.BeginInvoke(action);
                else
                    action();
                return;
            }

            if (_uiInvokeTarget.IsHandleCreated && !_uiInvokeTarget.IsDisposed)
            {
                if (_uiInvokeTarget.InvokeRequired)
                    _uiInvokeTarget.BeginInvoke(action);
                else
                    action();
                return;
            }

            _marshalToUi(action);
        }

        private void MarshalToUi(Action action) => _marshalToUi(action);

        private void Emit(string message) => Status?.Invoke(message);

        private static void SafeTrySet(TaskCompletionSource<bool> tcs, bool result)
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(result);
        }

        private static void EnsureHandle(Control control)
        {
            if (!control.IsHandleCreated)
                _ = control.Handle;
        }
    }
}
