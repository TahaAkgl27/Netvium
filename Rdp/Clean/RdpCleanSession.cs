
using Netvium.Models;
using Netvium.Rdp;

namespace Netvium.Rdp.Clean
{
    /// <summary>
    /// Tab-scoped clean RDP session. Failure classification and retry decisions are
    /// centralized here; the legacy RDP engine is intentionally not used.
    /// </summary>
    public sealed class RdpCleanSession : IDisposable
    {
        private RdpSessionHostControl? _host;
        private ServerInfo? _lastServer;
        private RdpDisplayMode _displayMode = RdpDisplayMode.Native;
        private bool _handlersAttached;
        private bool _disposed;
        private bool _manualDisconnectRequested;
        private bool _disconnectForRetry;
        private DateTime _lastFailureUtc;
        private string? _lastFailureKey;
        private EventHandler? _hostConnected;
        private EventHandler? _hostLoginComplete;
        private EventHandler<int>? _hostDisconnected;
        private EventHandler<int>? _hostFatalError;
        private EventHandler<int>? _hostLogonError;
        private EventHandler<string>? _hostStatus;
        private TaskCompletionSource<int>? _logonErrorTcs;
        private TaskCompletionSource<int>? _fatalErrorTcs;
        private TaskCompletionSource<RdpFailureInfo>? _failureTcs;

        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public RdpConnectionState ConnectionState { get; private set; } = RdpConnectionState.Idle;
        public bool IsConnected => ConnectionState is RdpConnectionState.Connected or RdpConnectionState.LoginComplete;
        public bool IsConnecting => ConnectionState.IsInProgress();
        public bool IsReadyForCapture => IsConnected;
        public bool IsDisposed => _disposed;
        public string? ActiveProfileLabel { get; private set; }
        public RdpDisplayMode DisplayMode => _displayMode;
        public DateTime? ConnectedAtUtc { get; private set; }
        public string? LastError { get; private set; }
        public RdpFailureInfo? LastFailure { get; private set; }
        public RdpConnectionAttemptDiagnostics LastDiagnostics { get; private set; } = new();

        public event Action? Connected;
        public event Action<string>? StatusChanged;
        public event Action<RdpConnectionFailureInfo>? ConnectionFailed;
        public event Action<RdpFailureInfo>? FailureDetected;
        public event Action<int, int>? Disconnected;

        public async Task ConnectAsync(ServerInfo server, RdpSessionHostControl host, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(host);
            if (_disposed)
                throw new ObjectDisposedException(nameof(RdpCleanSession));

            _manualDisconnectRequested = false;
            _disconnectForRetry = false;
            _lastFailureKey = null;
            _lastServer = server;
            _host = host;
            host.SessionId = SessionId;
            AttachHostHandlers(host);
            host.SetDisplayMode(_displayMode);

            ConnectionState = RdpConnectionState.Connecting;
            LastError = null;
            LastDiagnostics = new RdpConnectionAttemptDiagnostics
            {
                SessionId = SessionId,
                ServerName = server.Name,
                Host = server.Ip,
                Port = server.Port <= 0 ? 3389 : server.Port,
                HasUsername = !string.IsNullOrWhiteSpace(server.Username),
                ConnectStartedUtc = DateTime.UtcNow,
                LastEvent = "ConnectStarted",
                MaxRetryAttempts = RdpRetryPolicy.MaxAutoRetries,
            };
            RdpCleanHostLogger.Log("FailurePhase=started", SessionId);

            try
            {
                var tcpOk = await RdpPortProbe.CheckAsync(server.Ip, LastDiagnostics.Port, RdpPortProbe.DefaultTimeoutMs, ct).ConfigureAwait(true);
                LastDiagnostics.TcpReachable = tcpOk;
                if (tcpOk != true)
                {
                    var failure = RdpFailureClassifier.Classify(SessionId, "TcpProbe", detailHint: "unreachable");
                    RegisterFailure(failure, terminal: false);
                    ConnectionState = RdpConnectionState.Failed;
                    LastDiagnostics.FinalState = ConnectionState;
                    return;
                }

                _logonErrorTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _fatalErrorTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _failureTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var connectTask = host.ConnectAsync(server, ct);
                var completed = await Task.WhenAny(connectTask, _logonErrorTcs.Task, _fatalErrorTcs.Task, _failureTcs.Task).ConfigureAwait(true);
                if (completed == _failureTcs.Task)
                {
                    await _failureTcs.Task.ConfigureAwait(true);
                    return;
                }
                await connectTask.ConfigureAwait(true);
            }
            catch (TimeoutException ex)
            {
                var failure = RdpFailureClassifier.Classify(SessionId, "ConnectionTimeout", exception: ex);
                RegisterFailure(failure, terminal: false);
                ConnectionState = RdpConnectionState.TimedOut;
                LastDiagnostics.FinalState = ConnectionState;
            }
            catch (OperationCanceledException)
            {
                var source = _disposed ? "SessionClosed" : (_manualDisconnectRequested ? "UserDisconnect" : "ConnectCanceled");
                var failure = RdpFailureClassifier.Classify(SessionId, source, userInitiated: _manualDisconnectRequested || _disposed);
                RegisterFailure(failure, terminal: true);
                ConnectionState = RdpConnectionState.Cancelled;
                LastDiagnostics.FinalState = ConnectionState;
            }
            catch (Exception ex)
            {
                var failure = RdpFailureClassifier.Classify(SessionId, "ConnectException", exception: ex);
                RegisterFailure(failure, terminal: false);
                ConnectionState = RdpConnectionState.Failed;
                LastDiagnostics.FinalState = ConnectionState;
            }
            finally
            {
                ActiveProfileLabel = host.ActiveControlType;
                LastDiagnostics.ActiveXVersion = host.ActiveControlType;
                _logonErrorTcs = null;
                _fatalErrorTcs = null;
                _failureTcs = null;
            }
        }
        public void Disconnect()
        {
            if (_disposed)
                return;

            _manualDisconnectRequested = true;
            _disconnectForRetry = false;
            try { _host?.DisconnectSession(); } catch { }
            if (ConnectionState.IsSessionConnected() || ConnectionState.IsInProgress())
                ConnectionState = RdpConnectionState.Disconnected;
        }

        public void DisconnectForRetry()
        {
            if (_disposed)
                return;

            _disconnectForRetry = true;
            _manualDisconnectRequested = false;
            try { _host?.DisconnectSession(); } catch { }
            ConnectionState = RdpConnectionState.Idle;
        }

        public void SetRetryAttempt(int attempt)
        {
            LastDiagnostics.RetryAttempt = Math.Clamp(attempt, 0, RdpRetryPolicy.MaxAutoRetries);
        }

        public void ResetRetryState()
        {
            LastFailure = null;
            _lastFailureKey = null;
            _lastFailureUtc = DateTime.MinValue;
            LastDiagnostics.RetryAttempt = 0;
            LastDiagnostics.Retryable = false;
        }

        public void SendCtrlAltDel() => _host?.SendCtrlAltDel();

        public void SetDisplayMode(RdpDisplayMode mode)
        {
            _displayMode = mode;
            _host?.SetDisplayMode(mode);
        }

        public void SetSmartSizing(bool enabled) => SetDisplayMode(enabled ? RdpDisplayMode.Fit : RdpDisplayMode.Native);
        public bool TryGetSmartSizingEnabled() => _displayMode == RdpDisplayMode.Fit;
        public void SyncDisplaySettings() => _host?.SyncLocalViewport(updateSessionDisplay: true);
        public void ForceRefresh() => SyncDisplaySettings();
        public void EnsureHostFillsParent() => _host?.SyncRdpBounds();
        public void SetClipboardRedirect(bool enabled) => _host?.SetClipboardRedirect(enabled);
        public void SetKeyboardCapture(bool enabled) => _host?.SetKeyboardCapture(enabled);
        public void SetMouseCapture(bool enabled) => _host?.SetMouseCapture(enabled);
        public void ApplyConnectionQuality(RdpConnectionQuality quality) => _host?.ApplyConnectionQuality(quality);
        public void ApplyPerformanceLevel(string level) => _host?.ApplyPerformanceLevel(level);

        public async Task ReconnectAsync(RdpSessionHostControl host, CancellationToken ct)
        {
            if (_lastServer == null || _disposed)
                return;

            DisconnectForRetry();
            ConnectionState = RdpConnectionState.Idle;
            host.SyncRdpBounds();
            await ConnectAsync(_lastServer, host, ct).ConfigureAwait(true);
        }

        public RdpState SessionState =>
            IsConnected ? RdpState.Connected
            : ConnectionState.IsInProgress() ? RdpState.Connecting
            : RdpState.Disconnected;

        public int? TryGetClientState() => _host?.TryGetClientState();
        public int? TryGetExtendedDisconnectReason() => _host?.TryGetExtendedDisconnectReason();

        public Task<bool> TryExecuteRemoteShellCommandAsync(string command)
        {
            RdpCleanHostLogger.Log($"TryExecuteRemoteShellCommandAsync skipped: {command}", SessionId);
            return Task.FromResult(false);
        }

        public void PublishTerminalFailure(RdpFailureInfo failure)
        {
            var terminal = failure with { RetryAttempt = Math.Min(failure.RetryAttempt, RdpRetryPolicy.MaxAutoRetries) };
            LastFailure = terminal;
            LastError = terminal.SafeMessage;
            LastDiagnostics.FailureCategory = terminal.Category;
            LastDiagnostics.DisconnectReason = terminal.DisconnectReason;
            LastDiagnostics.ExtendedDisconnectReason = terminal.ExtendedDisconnectReason;
            LastDiagnostics.FatalErrorCode = terminal.FatalErrorCode;
            LastDiagnostics.LogonErrorCode = terminal.LogonErrorCode;
            LastDiagnostics.ComHResult = terminal.HResult;
            LastDiagnostics.ErrorCode = terminal.FatalErrorCode ?? terminal.LogonErrorCode ?? terminal.DisconnectReason;
            LastDiagnostics.Retryable = terminal.IsRetryable;
            LastDiagnostics.RetryAttempt = terminal.RetryAttempt;
            LastDiagnostics.MaxRetryAttempts = terminal.MaxRetryAttempts;
            LastDiagnostics.FailureReason = terminal.SafeMessage;
            RdpCleanHostLogger.Log(RdpFailureLogFormatter.Format(terminal, "terminal-final"), SessionId);
            ConnectionFailed?.Invoke(new RdpConnectionFailureInfo
            {
                State = ConnectionState,
                Message = terminal.SafeMessage,
                Diagnostics = LastDiagnostics.Clone(),
                Failure = terminal,
            });
        }
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _manualDisconnectRequested = true;
            DetachHostHandlers();
            try { _host?.DisconnectSession(); } catch { }
            _host = null;
            ConnectionState = RdpConnectionState.Disconnected;
        }

        private void AttachHostHandlers(RdpSessionHostControl host)
        {
            if (_handlersAttached && ReferenceEquals(_host, host))
                return;

            DetachHostHandlers();
            _host = host;
            _hostConnected = (_, _) => HandleHostConnected();
            _hostLoginComplete = (_, _) => HandleHostConnected();
            _hostDisconnected = (_, reason) => HandleHostDisconnected(reason);
            _hostFatalError = (_, code) => HandleHostFatalError(code);
            _hostLogonError = (_, code) => HandleHostLogonError(code);
            _hostStatus = (_, status) => StatusChanged?.Invoke(status);
            host.Connected += _hostConnected;
            host.LoginComplete += _hostLoginComplete;
            host.Disconnected += _hostDisconnected;
            host.FatalError += _hostFatalError;
            host.LogonError += _hostLogonError;
            host.StatusChanged += _hostStatus;
            _handlersAttached = true;
        }

        private void DetachHostHandlers()
        {
            if (!_handlersAttached || _host == null)
                return;

            if (_hostConnected != null) _host.Connected -= _hostConnected;
            if (_hostLoginComplete != null) _host.LoginComplete -= _hostLoginComplete;
            if (_hostDisconnected != null) _host.Disconnected -= _hostDisconnected;
            if (_hostFatalError != null) _host.FatalError -= _hostFatalError;
            if (_hostLogonError != null) _host.LogonError -= _hostLogonError;
            if (_hostStatus != null) _host.StatusChanged -= _hostStatus;

            _hostConnected = null;
            _hostLoginComplete = null;
            _hostDisconnected = null;
            _hostFatalError = null;
            _hostLogonError = null;
            _hostStatus = null;
            _handlersAttached = false;
        }

        private void HandleHostConnected()
        {
            if (_disposed)
                return;

            ConnectionState = RdpConnectionState.LoginComplete;
            ConnectedAtUtc = DateTime.UtcNow;
            LastDiagnostics.LastEvent = "LoginComplete";
            LastDiagnostics.FinalState = RdpConnectionState.LoginComplete;
            LastFailure = null;
            _lastFailureKey = null;
            Connected?.Invoke();
            StatusChanged?.Invoke("Login complete");
        }

        private void HandleHostDisconnected(int reason)
        {
            var ext = _host?.TryGetExtendedDisconnectReason();
            var failure = RdpFailureClassifier.Classify(
                SessionId,
                _manualDisconnectRequested
                    ? "UserDisconnect"
                    : _disconnectForRetry ? "RetryDisconnect" : "OnDisconnected",
                disconnectReason: reason,
                extendedDisconnectReason: ext,
                userInitiated: _manualDisconnectRequested || _disposed);
            RegisterFailure(failure, terminal: !_disconnectForRetry && !_manualDisconnectRequested && !_disposed);
            ConnectionState = failure.Category == RdpFailureCategory.UserInitiatedDisconnect
                ? RdpConnectionState.Disconnected
                : RdpConnectionState.Failed;
            Disconnected?.Invoke(reason, ext ?? 0);
        }

        private void HandleHostFatalError(int code)
        {
            var failure = RdpFailureClassifier.Classify(
                SessionId,
                "OnFatalError",
                fatalErrorCode: code);
            RegisterFailure(failure, terminal: true);
            ConnectionState = RdpConnectionState.Failed;
            _fatalErrorTcs?.TrySetResult(code);
        }

        private void HandleHostLogonError(int code)
        {
            var failure = RdpFailureClassifier.Classify(
                SessionId,
                "OnLogonError",
                logonErrorCode: code,
                detailHint: "logon");
            RegisterFailure(failure, terminal: true);
            ConnectionState = RdpConnectionState.Failed;
            _logonErrorTcs?.TrySetResult(code);
        }

        private void RegisterFailure(RdpFailureInfo failure, bool terminal)
        {
            var key = string.Join("|", failure.Category, failure.DisconnectReason,
                failure.ExtendedDisconnectReason, failure.FatalErrorCode,
                failure.LogonErrorCode, failure.HResult, failure.ExceptionType);
            var duplicate = _lastFailureKey == key
                || (LastFailure != null
                    && LastFailure.Category == failure.Category
                    && DateTime.UtcNow - _lastFailureUtc < TimeSpan.FromSeconds(2));

            if (duplicate)
            {
                RdpCleanHostLogger.Log(
                    RdpFailureLogFormatter.Format(failure, "duplicate-suppressed", duplicateSuppressed: true),
                    SessionId);
                return;
            }

            _lastFailureKey = key;
            _lastFailureUtc = DateTime.UtcNow;
            LastFailure = failure;
            LastError = failure.SafeMessage;
            LastDiagnostics.FailureCategory = failure.Category;
            LastDiagnostics.DisconnectReason = failure.DisconnectReason;
            LastDiagnostics.ExtendedDisconnectReason = failure.ExtendedDisconnectReason;
            LastDiagnostics.FatalErrorCode = failure.FatalErrorCode;
            LastDiagnostics.LogonErrorCode = failure.LogonErrorCode;
            LastDiagnostics.ComHResult = failure.HResult;
            LastDiagnostics.ErrorCode = failure.FatalErrorCode ?? failure.LogonErrorCode ?? failure.DisconnectReason;
            LastDiagnostics.Retryable = failure.IsRetryable;
            LastDiagnostics.RetryAttempt = failure.RetryAttempt;
            LastDiagnostics.MaxRetryAttempts = failure.MaxRetryAttempts;
            LastDiagnostics.LastEvent = failure.Source;
            LastDiagnostics.FailureReason = failure.SafeMessage;

            RdpCleanHostLogger.Log(
                RdpFailureLogFormatter.Format(failure, terminal ? "terminal" : "detected"),
                SessionId);
            FailureDetected?.Invoke(failure);

            if (terminal)
            {
                ConnectionFailed?.Invoke(new RdpConnectionFailureInfo
                {
                    State = ConnectionState,
                    Message = failure.SafeMessage,
                    Diagnostics = LastDiagnostics.Clone(),
                    Failure = failure,
                });
            }
        }
    }
}
