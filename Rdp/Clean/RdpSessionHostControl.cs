using AxMSTSCLib;
using MSTSCLib;
using Netvium.Models;
using Netvium.Native;
using Netvium.Rdp;
using System.Runtime.InteropServices;

namespace Netvium.Rdp.Clean
{
    /// <summary>
    /// Clean WinForms host — tries MsRdpClient11, then 10, then 9.
    /// All ActiveX access is marshaled to the control's UI thread.
    /// </summary>
    public sealed class RdpSessionHostControl : System.Windows.Forms.UserControl
    {
        public int ConnectTimeoutSeconds { get; set; } = 60;
        private const int MinConnectWidth = 200;
        private const int MinConnectHeight = 150;

        private System.Windows.Forms.Control? _rdp;
        private System.Windows.Forms.Timer? _pollTimer;
        private int _disposeState;
        private volatile bool _disposeRequested;
        private bool _transportConnectedRaised;
        private bool _loginCompleteRaised;
        private int? _lastPollConnected;
        private RdpConnectionQuality _connectionQuality = RdpConnectionQuality.Lan;
        private string _activeType = "none";
        private TaskCompletionSource? _connectWaitTcs;
        private string? _activeClsid;
        private RdpSizingPlan? _connectSizingPlan;
        private int _lockedDesktopWidth;
        private int _lockedDesktopHeight;
        private DateTime? _lastResizeUtc;
        private bool? _lastUpdateSessionDisplaySucceeded;
        private int _lastAppliedDisplayWidth;
        private int _lastAppliedDisplayHeight;
        private int _lastAppliedDesktopScale;
        private int _lastAppliedDeviceScale;
        private int _desktopScalePercent = 100;
        private int _deviceScalePercent = 100;
        private RdpDisplayMode _displayMode = RdpDisplayMode.Native;
        private long _viewportRequestGeneration;
        private int _lastRequestedDisplayWidth;
        private int _lastRequestedDisplayHeight;
        private int _lastRequestedDesktopScale;
        private int _lastRequestedDeviceScale;
        private bool _displayUpdateFallbackApplied;

        public event EventHandler? Connected;
        public event EventHandler? LoginComplete;
        public event EventHandler<int>? Disconnected;
        public event EventHandler<int>? FatalError;
        public event EventHandler<int>? LogonError;
        public event EventHandler<string>? StatusChanged;

        public string ActiveControlType => _activeType;
        public string? ActiveControlClsid => _activeClsid;
        public bool IsRdpConnected { get; private set; }

        /// <summary>Stable identifier used to separate diagnostics from concurrent RDP tabs.</summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

        public RdpSessionHostControl()
        {
            Dock = System.Windows.Forms.DockStyle.Fill;
            BackColor = System.Drawing.Color.Black;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Resize += (_, _) => OnUi(() => SyncRdpBoundsCore());
        }

        public bool IsLayoutReadyForConnect() =>
            OnUi(IsLayoutReadyForConnectCore);

        public (int Width, int Height) GetClientPixelSize() =>
            OnUi(GetClientPixelSizeCore);

        public (int Width, int Height, nint Handle, System.Drawing.Rectangle Bounds) GetActiveXMetrics() =>
            OnUi(GetActiveXMetricsCore);

        public void EnsureActiveXCreated() =>
            OnUi(EnsureActiveXCreatedCore);

        public Task ConnectAsync(ServerInfo server, CancellationToken ct) =>
            OnUiAsync(() => ConnectAsyncCore(server, ct));

        public void DisconnectSession() =>
            OnUi(DisconnectSessionCore);

        public void SendCtrlAltDel() =>
            OnUi(SendCtrlAltDelCore);

        public void SetFitToTab(bool fitToTab) =>
            SetDisplayMode(fitToTab ? RdpDisplayMode.Fit : RdpDisplayMode.Native);

        public void SetDisplayMode(RdpDisplayMode mode) =>
            OnUi(() => SetDisplayModeCore(mode));

        public RdpDisplayMode DisplayMode =>
            OnUi(() => _displayMode);

        public bool TryGetFitToTabEnabled() =>
            OnUi(TryGetFitToTabEnabledCore);

        public void ApplyFillFromSurface(bool syncSessionDisplay = false) =>
            OnUi(() => ApplyFillFromSurfaceCore(syncSessionDisplay));

        public int? TryGetExtendedDisconnectReason() =>
            OnUi(TryReadExtendedDisconnectReasonCore);

        public int? TryGetClientState() =>
            OnUi(TryReadClientStateCore);

        public void SyncLocalViewport(bool updateSessionDisplay = false)
        {
            var generation = Interlocked.Increment(ref _viewportRequestGeneration);
            OnUi(() => SyncLocalViewportCore(updateRemote: updateSessionDisplay, requestGeneration: generation));
        }

        public void SyncLocalViewport(int width, int height, bool updateSessionDisplay = false)
        {
            var generation = Interlocked.Increment(ref _viewportRequestGeneration);
            OnUi(() => SyncLocalViewportCore(width, height, updateRemote: updateSessionDisplay, requestGeneration: generation));
        }
        public RdpHostViewportDiagnostics GetViewportDiagnostics() =>
            OnUi(GetViewportDiagnosticsCore);

        [Obsolete("Use SyncLocalViewport")]
        public void SyncSessionDisplaySettings() => SyncLocalViewport(updateSessionDisplay: true);

        public void SyncRdpBounds() =>
            OnUi(() => SyncRdpBoundsCore());

        public void SetSmartSizing(bool enabled) => SetFitToTab(enabled);

        public bool TryGetSmartSizingEnabled() => TryGetFitToTabEnabled();

        public void ApplyConnectionQuality(RdpConnectionQuality quality) =>
            OnUi(() => ApplyConnectionQualityCore(quality));

        public void ApplyPerformanceLevel(string level) =>
            OnUi(() => ApplyPerformanceLevelCore(level));

        public void SetClipboardRedirect(bool enabled) =>
            OnUi(() => SetClipboardRedirectCore(enabled));

        public void SetKeyboardCapture(bool enabled) =>
            OnUi(() => SetKeyboardCaptureCore(enabled));

        public void SetMouseCapture(bool enabled) =>
            OnUi(() => SetMouseCaptureCore(enabled));

        public void SetConnectSizing(RdpSizingPlan plan) =>
            OnUi(() => _connectSizingPlan = plan);

        public RdpSizingPlan? GetConnectSizingPlan() =>
            OnUi(() => _connectSizingPlan);

        public void SetDpiScale(int desktopScalePercent, int deviceScalePercent) =>
            OnUi(() =>
            {
                _desktopScalePercent = Math.Clamp(desktopScalePercent, 100, 500);
                _deviceScalePercent = Math.Clamp(deviceScalePercent, 100, 500);
            });

        public IReadOnlyList<string> GetActiveXPropertyDump() =>
            OnUi(GetActiveXPropertyDumpCore);

        public (int DesktopWidth, int DesktopHeight) GetLockedDesktopSize() =>
            OnUi(() => (_lockedDesktopWidth, _lockedDesktopHeight));

        public string GetRenderingModeLabel() =>
            OnUi(() => RdpDisplayModePolicy.GetLabel(_displayMode));

        private void OnUi(Action action)
        {
            if (IsDisposed || _disposeRequested)
                return;

            if (InvokeRequired)
                BeginInvoke(new Action(() => RunWithSession(action)));
            else
                RunWithSession(action);
        }

        private T OnUi<T>(Func<T> func)
        {
            if (IsDisposed || _disposeRequested)
                return default!;

            if (InvokeRequired)
                return (T)Invoke(new Func<T>(() => RunWithSession(func)))!;

            return RunWithSession(func);
        }

        private Task OnUiAsync(Func<Task> action)
        {
            if (IsDisposed || _disposeRequested)
                return Task.CompletedTask;

            if (InvokeRequired)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                BeginInvoke(new Action(async () =>
                {
                    using var sessionScope = RdpCleanHostLogger.PushSessionId(SessionId);
                    try
                    {
                        await action().ConfigureAwait(true);
                        tcs.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }));
                return tcs.Task;
            }

            return RunWithSessionAsync(action);
        }

        private void RunWithSession(Action action)
        {
            using var sessionScope = RdpCleanHostLogger.PushSessionId(SessionId);
            action();
        }

        private T RunWithSession<T>(Func<T> func)
        {
            using var sessionScope = RdpCleanHostLogger.PushSessionId(SessionId);
            return func();
        }

        private async Task RunWithSessionAsync(Func<Task> action)
        {
            using var sessionScope = RdpCleanHostLogger.PushSessionId(SessionId);
            await action().ConfigureAwait(true);
        }
        private bool IsLayoutReadyForConnectCore()
        {
            var (w, h) = GetClientPixelSizeCore();
            if (w < MinConnectWidth || h < MinConnectHeight)
                return false;

            if (!IsHandleCreated)
                return false;

            if (_rdp == null || !_rdp.IsHandleCreated || _rdp.Width <= 0 || _rdp.Height <= 0)
                return false;

            return _rdp.Handle != nint.Zero;
        }

        private void ApplyFillFromSurfaceCore(bool syncSessionDisplay)
        {
            Dock = System.Windows.Forms.DockStyle.Fill;
            if (Parent is System.Windows.Forms.Control parent)
                parent.PerformLayout();

            PerformLayout();
            SyncLocalViewportCore(updateRemote: syncSessionDisplay);
        }

        private void SyncLocalViewportCore(
            int? width = null,
            int? height = null,
            bool updateRemote = false,
            long? requestGeneration = null)
        {
            if (requestGeneration.HasValue
                && !RdpViewportUpdatePolicy.IsLatest(requestGeneration.Value, Volatile.Read(ref _viewportRequestGeneration)))
            {
                return;
            }

            if (Parent is RdpHostPanel panel)
            {
                panel.SyncFillLayout();
                panel.PerformLayout();
            }

            PerformLayout();
            SyncRdpBoundsCore(width, height);

            var w = width ?? GetClientPixelSizeCore().Width;
            var h = height ?? GetClientPixelSizeCore().Height;
            if (w <= 0 || h <= 0)
                return;

            // The selected display mode is authoritative across connect, login,
            // reconnect and resize. Login must never silently switch Native to Fit.
            ApplySmartSizingForViewport(_displayMode);

            if (updateRemote && IsRdpConnected && _rdp != null)
            {
                var sameDisplayRequest = RdpViewportUpdatePolicy.IsSamePhysicalRequest(
                    w,
                    h,
                    _desktopScalePercent,
                    _deviceScalePercent,
                    _lastRequestedDisplayWidth,
                    _lastRequestedDisplayHeight,
                    _lastRequestedDesktopScale,
                    _lastRequestedDeviceScale);
                if (sameDisplayRequest)
                    return;

                _lastResizeUtc = DateTime.UtcNow;
                _lastRequestedDisplayWidth = w;
                _lastRequestedDisplayHeight = h;
                _lastRequestedDesktopScale = _desktopScalePercent;
                _lastRequestedDeviceScale = _deviceScalePercent;
                _lastUpdateSessionDisplaySucceeded = TryUpdateSessionDisplaySettings(w, h, out _displayUpdateFallbackApplied);
                if (_lastUpdateSessionDisplaySucceeded == true)
                {
                    _lastAppliedDisplayWidth = w;
                    _lastAppliedDisplayHeight = h;
                    _lastAppliedDesktopScale = _desktopScalePercent;
                    _lastAppliedDeviceScale = _deviceScalePercent;
                    // A dynamic display update can rebuild the server graphics pipeline.
                    // Reassert the negotiated visual-quality profile after it succeeds.
                    RdpRenderingQuality.ApplyProfile(GetAdvancedSettings9Core(), _connectionQuality);
                }
                else
                {
                    // Keep the selected Native/Fit mode intact. A failed remote update
                    // is reported as a local fallback; it must never enable SmartSizing.
                    _displayUpdateFallbackApplied = true;
                }

                RdpCleanHostLogger.Log(
                    _lastUpdateSessionDisplaySucceeded == true
                        ? $"UpdateSessionDisplaySettings OK ({w}x{h}) scale={_desktopScalePercent}/{_deviceScalePercent} mode={RdpDisplayModePolicy.GetLabel(_displayMode)} fallback={_displayUpdateFallbackApplied}"
                        : $"UpdateSessionDisplaySettings failed ({w}x{h}) mode={RdpDisplayModePolicy.GetLabel(_displayMode)} fallback={_displayUpdateFallbackApplied}");
            }
        }

        private void ApplySmartSizingForViewport(RdpDisplayMode mode)
        {
            var adv = GetAdvancedSettings9Core();
            if (adv == null)
                return;

            try
            {
                adv.SmartSizing = RdpDisplayModePolicy.IsSmartSizingEnabled(mode);
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"SmartSizing apply failed mode={RdpDisplayModePolicy.GetLabel(mode)} type={ex.GetType().Name}");
            }
        }

        private (int Width, int Height) GetClientPixelSizeCore()
        {
            PerformLayout();
            var w = ClientSize.Width > 0 ? ClientSize.Width : Width;
            var h = ClientSize.Height > 0 ? ClientSize.Height : Height;

            if ((w <= 0 || h <= 0) && Parent is System.Windows.Forms.Control parent)
            {
                w = parent.ClientSize.Width;
                h = parent.ClientSize.Height;
            }

            return (Math.Max(w, 0), Math.Max(h, 0));
        }

        private (int Width, int Height, nint Handle, System.Drawing.Rectangle Bounds) GetActiveXMetricsCore()
        {
            if (_rdp == null)
                return (0, 0, nint.Zero, System.Drawing.Rectangle.Empty);

            return (_rdp.Width, _rdp.Height, _rdp.Handle, _rdp.Bounds);
        }

        private void EnsureActiveXCreatedCore()
        {
            if (_rdp != null)
                return;

            var selection = RdpActiveXSelector.Select(
                CreateActiveXCandidates(),
                InitializeActiveXCandidate,
                CleanupFailedActiveXCandidate,
                message => RdpCleanHostLogger.Log(message));

            _rdp = selection.Instance;
            _activeType = selection.Version.Label;
            _activeClsid = selection.Version.Clsid;
            SyncRdpBoundsCore();

            RdpCleanHostLogger.Log(
                $"ActiveX created type={_activeType} host={Width}x{Height} "
                + $"rdp={_rdp.Width}x{_rdp.Height} handle={_rdp.IsHandleCreated}");
        }

        private async Task ConnectAsyncCore(ServerInfo server, CancellationToken ct)
        {
            if (_rdp == null)
                EnsureActiveXCreatedCore();

            if (!IsLayoutReadyForConnectCore())
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!IsLayoutReadyForConnectCore() && DateTime.UtcNow < deadline)
                {
                    SyncRdpBoundsCore();
                    PerformLayout();
                    _rdp?.PerformLayout();
                    if (Parent is System.Windows.Forms.Control parent)
                        parent.PerformLayout();
                    await Task.Delay(50, ct).ConfigureAwait(true);
                }
            }

            if (!IsLayoutReadyForConnectCore())
            {
                var (axW, axH, axHandle, axBounds) = GetActiveXMetricsCore();
                RdpCleanHostLogger.Log(
                    $"Layout not ready host={Width}x{Height} panelClient={ClientSize.Width}x{ClientSize.Height} "
                    + $"rdp={axW}x{axH} handle={axHandle} bounds={axBounds.Width}x{axBounds.Height}");
                throw new InvalidOperationException(
                    $"RDP layout not ready: host={Width}x{Height} rdp={axW}x{axH}");
            }

            var port = server.Port <= 0 ? 3389 : server.Port;
            var (normalized, domain, userOnly) = RdpCredentialHelper.NormalizeUsername(server.Username);
            var assignUser = normalized.Contains('@', StringComparison.Ordinal)
                ? normalized
                : string.IsNullOrWhiteSpace(userOnly) ? normalized : userOnly;
            var assignDomain = normalized.Contains('@', StringComparison.Ordinal) ? string.Empty : domain;
            SyncRdpBoundsCore();
            var plan = _connectSizingPlan;
            var (clientW, clientH) = GetClientPixelSizeCore();
            var viewportW = clientW > 0 ? clientW : Math.Max(plan?.ViewportPixelWidth ?? 0, MinConnectWidth);
            var viewportH = clientH > 0 ? clientH : Math.Max(plan?.ViewportPixelHeight ?? 0, MinConnectHeight);
            // mRemoteNG/mstsc model: remote session resolution = host viewport pixels (1:1)
            var desktopW = viewportW;
            var desktopH = viewportH;
            _lockedDesktopWidth = desktopW;
            _lockedDesktopHeight = desktopH;

            ResetDisplayUpdateCache();

            RdpCleanHostLogger.LogBlock("Connect attempt", new[]
            {
                $"Host={server.Ip}",
                $"Port={port}",
                $"UsernameFormat={DescribeUsernameFormat(normalized)}",
                $"HostControlSize={Width}x{Height}",
                $"ViewportPx={viewportW}x{viewportH}",
                $"RequestedRemote={desktopW}x{desktopH}",
                $"ViewMode={plan?.ProfileLabel ?? "default"}",
                $"IsHandleCreated={_rdp!.IsHandleCreated}",
                $"ActiveXType={_activeType}",
            });

            if (plan.HasValue)
            {
                RdpCleanHostLogger.Log(
                    RdpViewportSizing.FormatDiagnostics(plan.Value, ClientSize.Width, ClientSize.Height, viewportW, viewportH));
            }

            SyncLocalViewportCore(viewportW, viewportH);
            ApplyConnectionSettings(server.Ip, port, assignUser, assignDomain, server.Password ?? string.Empty, desktopW, desktopH, viewportW, viewportH);
            ApplySmartSizingForViewport(_displayMode);
            ApplyConnectionQualityCore(_connectionQuality);

            _transportConnectedRaised = false;
            _loginCompleteRaised = false;
            _lastPollConnected = null;
            IsRdpConnected = false;
            _connectWaitTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            StartPollTimer();

            RdpCleanHostLogger.Log("Connect called");
            try
            {
                InvokeConnectCore();
                RdpCleanHostLogger.Log("Connect returned");
            }
            catch (Exception ex)
            {
                StopPollTimerCore();
                _connectWaitTcs.TrySetException(ex);
                RdpCleanHostLogger.Log($"Connect exception type={ex.GetType().Name} hresult={ex.HResult}");
                throw;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));

            try
            {
                var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);
                var completed = await Task.WhenAny(_connectWaitTcs.Task, timeoutTask).ConfigureAwait(true);
                if (completed != _connectWaitTcs.Task)
                    throw new TimeoutException($"RDP connection timed out after {ConnectTimeoutSeconds} seconds.");

                await _connectWaitTcs.Task.ConfigureAwait(true);
            }
            finally
            {
                if (!IsRdpConnected)
                    StopPollTimerCore();
                _connectWaitTcs = null;
            }
        }

        private void DisconnectSessionCore()
        {
            StopPollTimerCore();
            try
            {
                switch (_rdp)
                {
                    case AxMsRdpClient9NotSafeForScripting r9: r9.Disconnect(); break;
                    case AxMsRdpClient10NotSafeForScripting r10: r10.Disconnect(); break;
                    case AxMsRdpClient11NotSafeForScripting r11: r11.Disconnect(); break;
                }
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"Disconnect exception type={ex.GetType().Name} hresult={ex.HResult}");
            }

            IsRdpConnected = false;
            _transportConnectedRaised = false;
            _loginCompleteRaised = false;
            _lastPollConnected = null;
            ResetDisplayUpdateCache();
        }

        private void ResetDisplayUpdateCache()
        {
            _lastUpdateSessionDisplaySucceeded = null;
            _lastAppliedDisplayWidth = 0;
            _lastAppliedDisplayHeight = 0;
            _lastAppliedDesktopScale = 0;
            _lastAppliedDeviceScale = 0;
            _lastRequestedDisplayWidth = 0;
            _lastRequestedDisplayHeight = 0;
            _lastRequestedDesktopScale = 0;
            _lastRequestedDeviceScale = 0;
            _displayUpdateFallbackApplied = false;
        }

        private void ApplyConnectionQualityCore(RdpConnectionQuality quality)
        {
            _connectionQuality = quality;
            var adv = GetAdvancedSettings9Core();
            if (adv == null)
                return;

            try
            {
                RdpRenderingQuality.ApplyProfile(adv, quality);
                RdpCleanHostLogger.Log($"Connection quality profile: {quality}");
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"Connection quality profile failed type={ex.GetType().Name} hresult={ex.HResult}");
            }
        }

        private void ApplyPerformanceLevelCore(string level)
        {
            var adv = GetAdvancedSettings9Core();
            if (adv == null)
                return;

            try
            {
                dynamic d = adv;
                switch (level?.ToLowerInvariant())
                {
                    case "low":
                        try { d.Compress = 1; } catch { /* optional */ }
                        try { d.PerformanceFlags = 0; } catch { /* optional */ }
                        try { d.AllowFontSmoothing = 0; } catch { /* optional */ }
                        try { d.EnableDesktopComposition = 0; } catch { /* optional */ }
                        try { d.DisableWallpaper = 1; } catch { /* optional */ }
                        try { d.DisableThemes = 1; } catch { /* optional */ }
                        break;
                    case "high":
                        try { d.Compress = 0; } catch { /* optional */ }
                        try { d.PerformanceFlags = 0x00000010 | 0x00000010 | 0x00000080 | 0x00000100; } catch { /* optional */ }
                        try { d.AllowFontSmoothing = 1; } catch { /* optional */ }
                        try { d.EnableDesktopComposition = 1; } catch { /* optional */ }
                        try { d.DisableWallpaper = 0; } catch { /* optional */ }
                        try { d.DisableThemes = 0; } catch { /* optional */ }
                        break;
                    default:
                        try { d.Compress = 1; } catch { /* optional */ }
                        try { d.PerformanceFlags = 0x00000010 | 0x00000080 | 0x00000100; } catch { /* optional */ }
                        try { d.AllowFontSmoothing = 1; } catch { /* optional */ }
                        try { d.EnableDesktopComposition = 1; } catch { /* optional */ }
                        try { d.DisableWallpaper = 0; } catch { /* optional */ }
                        try { d.DisableThemes = 0; } catch { /* optional */ }
                        break;
                }

                RdpCleanHostLogger.Log($"Performance level set to: {level}");
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"Performance level failed type={ex.GetType().Name} hresult={ex.HResult}");
            }
        }

        private void SetClipboardRedirectCore(bool enabled)
        {
            var adv = GetAdvancedSettings9Core();
            if (adv == null)
                return;

            try
            {
                adv.RedirectClipboard = enabled;
                RdpCleanHostLogger.Log($"Clipboard redirect set to: {enabled}");
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"Clipboard redirect failed type={ex.GetType().Name} hresult={ex.HResult}");
            }
        }

        private void SetKeyboardCaptureCore(bool enabled)
        {
            var adv = GetAdvancedSettings9Core();
            if (adv == null)
                return;

            try
            {
                dynamic d = adv;
                try { d.GrabFocusOnConnect = enabled ? 1 : 0; } catch { /* optional */ }
                try { d.EnableWindowsKey = enabled ? 1 : 0; } catch { /* optional */ }
                RdpCleanHostLogger.Log($"Keyboard capture set to: {enabled}");
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"Keyboard capture failed type={ex.GetType().Name} hresult={ex.HResult}");
            }
        }

        private void SetMouseCaptureCore(bool enabled)
        {
            var adv = GetAdvancedSettings9Core();
            if (adv == null)
                return;

            try
            {
                dynamic d = adv;
                try { d.AllowBackgroundInput = enabled ? 0 : 1; } catch { /* optional */ }
                RdpCleanHostLogger.Log($"Mouse capture set to: {enabled}");
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"Mouse capture failed type={ex.GetType().Name} hresult={ex.HResult}");
            }
        }

        private void SendCtrlAltDelCore()
        {
            try
            {
                if (_rdp is not System.Windows.Forms.AxHost axHost)
                    return;

                if (axHost.GetOcx() is NativeMethods.IMsRdpClientNonScriptable nonScriptable)
                {
                    var pbKeyInterrupt = false;
                    object vKeys = new object[] { 0x11, 0x12, 0x2E };
                    nonScriptable.SendKeys(3, ref pbKeyInterrupt, ref vKeys);
                    RdpCleanHostLogger.Log("SendCtrlAltDel sent");
                }
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"SendCtrlAltDel failed type={ex.GetType().Name} hresult={ex.HResult}");
            }
        }

        private void SetDisplayModeCore(RdpDisplayMode mode)
        {
            _displayMode = mode;
            ApplySmartSizingForViewport(_displayMode);
            SyncLocalViewportCore(updateRemote: IsRdpConnected);
            RdpCleanHostLogger.Log($"DisplayMode={RdpDisplayModePolicy.GetLabel(_displayMode)} SmartSizing={RdpDisplayModePolicy.IsSmartSizingEnabled(_displayMode)}");
        }

        private bool TryGetFitToTabEnabledCore() =>
            RdpDisplayModePolicy.IsSmartSizingEnabled(_displayMode);

        private static void TrySetScaleFactor(
            System.Windows.Forms.Control rdp,
            IMsRdpClientAdvancedSettings8 adv,
            int desktopScalePercent,
            int deviceScalePercent)
        {
            desktopScalePercent = Math.Clamp(desktopScalePercent, 100, 500);
            deviceScalePercent = RdpDpiHelper.NormalizeDeviceScalePercent(deviceScalePercent);

            // These properties live on IMsRdpExtendedSettings on current mstscax builds.
            // Setting only AdvancedSettings9 silently did nothing on 150%-DPI displays.
            TrySetExtendedScaleFactors(rdp, desktopScalePercent, deviceScalePercent);

            try
            {
                dynamic d = adv;
                d.DesktopScaleFactor = desktopScalePercent;
            }
            catch { /* optional */ }

            try
            {
                dynamic d = adv;
                d.DeviceScaleFactor = deviceScalePercent;
            }
            catch { /* optional */ }
        }

        private static void TrySetExtendedScaleFactors(
            System.Windows.Forms.Control rdp,
            int desktopScalePercent,
            int deviceScalePercent)
        {
            if (rdp is not System.Windows.Forms.AxHost axHost)
                return;

            nint unknown = nint.Zero;
            nint settingsPtr = nint.Zero;
            try
            {
                var ocx = axHost.GetOcx();
                unknown = Marshal.GetIUnknownForObject(ocx);
                var iid = NativeMethods.IID_IMsRdpExtendedSettings;
                if (Marshal.QueryInterface(unknown, ref iid, out settingsPtr) != 0 || settingsPtr == nint.Zero)
                    return;

                var settings = (NativeMethods.IMsRdpExtendedSettings)Marshal.GetObjectForIUnknown(settingsPtr);
                settings.put_Property("DesktopScaleFactor", (uint)desktopScalePercent);
                settings.put_Property("DeviceScaleFactor", (uint)deviceScalePercent);
                RdpCleanHostLogger.Log($"RDP scale configured desktop={desktopScalePercent} device={deviceScalePercent}");
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"RDP extended scale configuration failed type={ex.GetType().Name} hresult={ex.HResult}");
            }
            finally
            {
                if (settingsPtr != nint.Zero)
                    Marshal.Release(settingsPtr);
                if (unknown != nint.Zero)
                    Marshal.Release(unknown);
            }
        }

        private void SyncRdpBoundsCore(int? width = null, int? height = null)
        {
            if (_rdp == null)
                return;

            var w = width ?? GetClientPixelSizeCore().Width;
            var h = height ?? GetClientPixelSizeCore().Height;
            if (w <= 0 || h <= 0)
                return;

            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            _rdp.Dock = System.Windows.Forms.DockStyle.Fill;
            _rdp.SetBounds(rect.X, rect.Y, rect.Width, rect.Height);
            _rdp.Width = w;
            _rdp.Height = h;
            _rdp.PerformLayout();

            try
            {
                UpdateBounds();
                _rdp.GetType()
                    .GetMethod("UpdateBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(_rdp, null);
            }
            catch { /* optional */ }
        }

        private IMsRdpClientAdvancedSettings8? GetAdvancedSettings9Core() =>
            _rdp switch
            {
                AxMsRdpClient9NotSafeForScripting r9 => r9.AdvancedSettings9,
                AxMsRdpClient10NotSafeForScripting r10 => r10.AdvancedSettings9,
                AxMsRdpClient11NotSafeForScripting r11 => r11.AdvancedSettings9,
                _ => null
            };

        private void InvokeConnectCore()
        {
            switch (_rdp)
            {
                case AxMsRdpClient9NotSafeForScripting r9: r9.Connect(); break;
                case AxMsRdpClient10NotSafeForScripting r10: r10.Connect(); break;
                case AxMsRdpClient11NotSafeForScripting r11: r11.Connect(); break;
                default: throw new InvalidOperationException("ActiveX control not ready.");
            }
        }

        private int? TryReadConnectedPropertyCore() =>
            _rdp switch
            {
                AxMsRdpClient9NotSafeForScripting r9 => r9.Connected,
                AxMsRdpClient10NotSafeForScripting r10 => r10.Connected,
                AxMsRdpClient11NotSafeForScripting r11 => r11.Connected,
                _ => null
            };

        private void StartPollTimer()
        {
            StopPollTimerCore();
            _pollTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _pollTimer.Tick += (_, _) =>
            {
                if (IsDisposed || _disposeRequested)
                return;

                var connected = TryReadConnectedPropertyCore();
                if (_lastPollConnected != connected)
                {
                    _lastPollConnected = connected;
                    RdpCleanHostLogger.Log($"rdp.Connected poll={connected?.ToString() ?? "?"}");
                }

                if (connected is > 0)
                {
                    if (!_transportConnectedRaised)
                        RaiseTransportConnectedCore("Poll:Timer");
                    TryPromoteLoginCompleteFromPollCore();
                }
                else if (connected is 0
                    && (_transportConnectedRaised || _loginCompleteRaised || IsRdpConnected))
                {
                    HandleDisconnectedCore(0, "SyncState");
                }
            };
            _pollTimer.Start();
        }

        private void StopPollTimerCore()
        {
            if (_pollTimer == null)
                return;

            _pollTimer.Stop();
            _pollTimer.Dispose();
            _pollTimer = null;
        }

        private int? TryReadExtendedDisconnectReasonCore()
        {
            if (_rdp == null)
                return null;

            try
            {
                dynamic ax = _rdp;
                return Convert.ToInt32(ax.ExtendedDisconnectReason);
            }
            catch
            {
                return null;
            }
        }
        private int? TryReadClientStateCore()
        {
            if (_rdp == null)
                return null;

            try
            {
                dynamic ax = _rdp;
                return (int)ax.ClientState;
            }
            catch
            {
                return null;
            }
        }

        private void RaiseTransportConnectedCore(string source)
        {
            if (_transportConnectedRaised)
                return;

            _transportConnectedRaised = true;
            RdpCleanHostLogger.Log(source == "OnConnected" ? "OnConnected" : $"Transport connected via {source}");
            StatusChanged?.Invoke(this, "Transport connected");
        }

        private void RaiseLoginCompleteCore(string source)
        {
            if (_loginCompleteRaised)
                return;

            _loginCompleteRaised = true;
            IsRdpConnected = true;

            SyncLocalViewportCore(updateRemote: true);
            RdpCleanHostLogger.Log(source == "OnLoginComplete" ? "OnLoginComplete" : $"Login complete via {source}");
            _connectWaitTcs?.TrySetResult();
            Connected?.Invoke(this, EventArgs.Empty);
            LoginComplete?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, "Login complete");
        }

        private void TryPromoteLoginCompleteFromPollCore()
        {
            if (_loginCompleteRaised)
                return;

            var connected = TryReadConnectedPropertyCore();
            if (connected is null or 0)
                return;

            var clientState = TryReadClientStateCore();
            if (clientState == 0)
                RaiseLoginCompleteCore("Poll:ClientState");
        }

        private static string DescribeUsernameFormat(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return "empty";
            if (normalized.Contains('@', StringComparison.Ordinal))
                return "UPN";
            if (normalized.Contains('\\', StringComparison.Ordinal))
                return "DOMAIN\\user";
            return "plain";
        }

        private static IReadOnlyList<RdpActiveXCandidate<System.Windows.Forms.Control>> CreateActiveXCandidates()
        {
            var versions = RdpActiveXVersions.CleanHostOrder;
            return new RdpActiveXCandidate<System.Windows.Forms.Control>[]
            {
                new(versions[0], () => new AxMsRdpClient11NotSafeForScripting()),
                new(versions[1], () => new AxMsRdpClient10NotSafeForScripting()),
                new(versions[2], () => new AxMsRdpClient9NotSafeForScripting()),
            };
        }

        private void InitializeActiveXCandidate(System.Windows.Forms.Control client)
        {
            client.Dock = System.Windows.Forms.DockStyle.Fill;
            client.Visible = true;
            Controls.Add(client);
            client.CreateControl();

            if (!client.IsHandleCreated)
            {
                _ = client.Handle;
                if (!client.IsHandleCreated)
                    throw new InvalidOperationException("ActiveX handle could not be created.");
            }

            WireEvents(client);
        }

        private void CleanupFailedActiveXCandidate(System.Windows.Forms.Control client)
        {
            // AxHost.Dispose releases the hosted OCX/COM instance as well as its WinForms handle.
            if (ReferenceEquals(client.Parent, this))
                Controls.Remove(client);

            client.Dispose();
        }

        private void WireEvents(System.Windows.Forms.Control rdp)
        {
            switch (rdp)
            {
                case AxMsRdpClient11NotSafeForScripting r11:
                    r11.OnConnected += (_, _) => OnUi(() => RaiseTransportConnectedCore("OnConnected"));
                    r11.OnLoginComplete += (_, _) => OnUi(() => RaiseLoginCompleteCore("OnLoginComplete"));
                    r11.OnDisconnected += (_, e) => OnUi(() => HandleDisconnectedCore(e.discReason));
                    r11.OnFatalError += (_, e) => OnUi(() => HandleFatalErrorCore(e.errorCode));
                    r11.OnLogonError += (_, e) => OnUi(() => HandleLogonErrorCore(e.lError));
                    break;
                case AxMsRdpClient10NotSafeForScripting r10:
                    r10.OnConnected += (_, _) => OnUi(() => RaiseTransportConnectedCore("OnConnected"));
                    r10.OnLoginComplete += (_, _) => OnUi(() => RaiseLoginCompleteCore("OnLoginComplete"));
                    r10.OnDisconnected += (_, e) => OnUi(() => HandleDisconnectedCore(e.discReason));
                    r10.OnFatalError += (_, e) => OnUi(() => HandleFatalErrorCore(e.errorCode));
                    r10.OnLogonError += (_, e) => OnUi(() => HandleLogonErrorCore(e.lError));
                    break;
                case AxMsRdpClient9NotSafeForScripting r9:
                    r9.OnConnected += (_, _) => OnUi(() => RaiseTransportConnectedCore("OnConnected"));
                    r9.OnLoginComplete += (_, _) => OnUi(() => RaiseLoginCompleteCore("OnLoginComplete"));
                    r9.OnDisconnected += (_, e) => OnUi(() => HandleDisconnectedCore(e.discReason));
                    r9.OnFatalError += (_, e) => OnUi(() => HandleFatalErrorCore(e.errorCode));
                    r9.OnLogonError += (_, e) => OnUi(() => HandleLogonErrorCore(e.lError));
                    break;
            }
        }

        private void HandleDisconnectedCore(int reason, string source = "OnDisconnected")
        {
            IsRdpConnected = false;
            _transportConnectedRaised = false;
            _loginCompleteRaised = false;
            _lastPollConnected = null;
            StopPollTimerCore();
            RdpCleanHostLogger.Log($"OnDisconnected source={source} reason={reason} extended={TryReadExtendedDisconnectReasonCore()?.ToString() ?? "n/a"}");
            Disconnected?.Invoke(this, reason);
            StatusChanged?.Invoke(this, "Connection disconnected");
        }

        private void HandleFatalErrorCore(int code)
        {
            RdpCleanHostLogger.Log($"OnFatalError code={code}");
            _connectWaitTcs?.TrySetException(new InvalidOperationException($"Fatal RDP error (code={code})."));
            FatalError?.Invoke(this, code);
            StatusChanged?.Invoke(this, "RDP client fatal error");
        }

        private void HandleLogonErrorCore(int code)
        {
            RdpCleanHostLogger.Log($"OnLogonError code={code}");
            _connectWaitTcs?.TrySetException(
                new InvalidOperationException($"Logon error (code={code}). Check username, domain, or password."));
            LogonError?.Invoke(this, code);
            StatusChanged?.Invoke(this, "RDP logon failed");
        }

        private bool TryUpdateSessionDisplaySettings(
            int w,
            int h,
            out bool fallbackApplied)
        {
            fallbackApplied = false;
            if (_rdp == null)
                return false;

            var synced = TryUpdateSessionDisplaySettings(_rdp, w, h, out fallbackApplied);
            if (!synced)
                return false;

            try
            {
                dynamic ax = _rdp;
                ax.DesktopWidth = w;
                ax.DesktopHeight = h;
            }
            catch { /* optional */ }

            _lockedDesktopWidth = w;
            _lockedDesktopHeight = h;
            return true;
        }

        private static bool TryUpdateSessionDisplaySettings(
            System.Windows.Forms.Control rdp,
            int w,
            int h,
            out bool fallbackApplied)
        {
            fallbackApplied = false;
            dynamic ocx = rdp;
            try
            {
                ocx.UpdateSessionDisplaySettings(w, h, w, h, 0, 100, 100);
                return true;
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"UpdateSessionDisplaySettings 7-arg failed type={ex.GetType().Name} hresult={ex.HResult}");
            }

            try
            {
                ocx.UpdateSessionDisplaySettings(w, h, w, h, 0);
                fallbackApplied = true;
                return true;
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"UpdateSessionDisplaySettings 5-arg failed type={ex.GetType().Name} hresult={ex.HResult}");
            }

            try
            {
                ocx.UpdateSessionDisplaySettings(w, h, 100);
                fallbackApplied = true;
                return true;
            }
            catch (Exception ex)
            {
                RdpCleanHostLogger.Log($"UpdateSessionDisplaySettings 3-arg failed type={ex.GetType().Name} hresult={ex.HResult}");
            }

            return false;
        }
        private RdpHostViewportDiagnostics GetViewportDiagnosticsCore()
        {
            var (clientW, clientH) = GetClientPixelSizeCore();
            var (axW, axH, _, axBounds) = GetActiveXMetricsCore();
            var desktopW = _lockedDesktopWidth;
            var desktopH = _lockedDesktopHeight;

            if (_rdp != null)
            {
                try
                {
                    dynamic ax = _rdp;
                    desktopW = (int)ax.DesktopWidth;
                    desktopH = (int)ax.DesktopHeight;
                }
                catch { /* optional */ }
            }

            return new RdpHostViewportDiagnostics
            {
                HostClientWidth = clientW,
                HostClientHeight = clientH,
                ActiveXWidth = axW,
                ActiveXHeight = axH,
                ActiveXBoundsWidth = axBounds.Width,
                ActiveXBoundsHeight = axBounds.Height,
                DesktopWidth = desktopW,
                DesktopHeight = desktopH,
                SmartSizing = TryGetFitToTabEnabledCore(),
                DesktopScalePercent = _desktopScalePercent,
                DeviceScalePercent = _deviceScalePercent,
                LastResizeUtc = _lastResizeUtc,
                LastUpdateSessionDisplaySucceeded = _lastUpdateSessionDisplaySucceeded,
                DisplayMode = _displayMode,
                DisplayUpdateFallbackApplied = _displayUpdateFallbackApplied,
                RenderingMode = RdpDisplayModePolicy.GetLabel(_displayMode),
            };
        }

        private IReadOnlyList<string> GetActiveXPropertyDumpCore()
        {
            if (_rdp == null)
                return new[] { "(ActiveX not created)" };

            var lines = new List<string>();
            try
            {
                dynamic ax = _rdp;
                TryAppend(lines, "DesktopWidth", (int)ax.DesktopWidth);
                TryAppend(lines, "DesktopHeight", (int)ax.DesktopHeight);
                TryAppend(lines, "Width", ax.Width);
                TryAppend(lines, "Height", ax.Height);
                TryAppend(lines, "ColorDepth", ax.ColorDepth);
                TryAppend(lines, "Connected", ax.Connected);
            }
            catch (Exception ex)
            {
                lines.Add($"Core properties error type={ex.GetType().Name} hresult={ex.HResult}");
            }

            var adv = GetAdvancedSettings9Core();
            if (adv != null)
            {
                try { TryAppend(lines, "SmartSizing", adv.SmartSizing); } catch { /* optional */ }
                try { TryAppend(lines, "BitmapPersistence", adv.BitmapPersistence); } catch { /* optional */ }
                try { TryAppend(lines, "Compress", adv.Compress); } catch { /* optional */ }
                try { TryAppend(lines, "RedirectClipboard", adv.RedirectClipboard); } catch { /* optional */ }
                try { TryAppend(lines, "EnableCredSspSupport", adv.EnableCredSspSupport); } catch { /* optional */ }
                try
                {
                    dynamic d = adv;
                    TryAppendDynamic(lines, "DesktopScaleFactor", () => (int)d.DesktopScaleFactor);
                    TryAppendDynamic(lines, "DeviceScaleFactor", () => (int)d.DeviceScaleFactor);
                    TryAppendDynamic(lines, "PerformanceFlags", () => (int)d.PerformanceFlags);
                    TryAppendDynamic(lines, "NetworkConnectionType", () => (uint)d.NetworkConnectionType);
                    TryAppendDynamic(lines, "BandwidthDetection", () => (bool)d.BandwidthDetection);
                    TryAppendDynamic(lines, "ShadowBitmap", () => (int)d.ShadowBitmap);
                    TryAppendDynamic(lines, "AllowFontSmoothing", () => (int)d.AllowFontSmoothing);
                    TryAppendDynamic(lines, "EnableDesktopComposition", () => (int)d.EnableDesktopComposition);
                    TryAppendDynamic(lines, "DisableWallpaper", () => (int)d.DisableWallpaper);
                    TryAppendDynamic(lines, "DisableThemes", () => (int)d.DisableThemes);
                    TryAppendDynamic(lines, "DisplayConnectionBar", () => (int)d.DisplayConnectionBar);
                    TryAppendDynamic(lines, "ContainerHandledFullScreen", () => (int)d.ContainerHandledFullScreen);
                    TryAppendDynamic(lines, "GrabFocusOnConnect", () => (int)d.GrabFocusOnConnect);
                }
                catch { /* optional */ }
            }

            return lines;
        }

        private static void TryAppend(List<string> lines, string name, object? value) =>
            lines.Add($"{name}={value ?? "null"}");

        private static void TryAppendDynamic(List<string> lines, string name, Func<object> read)
        {
            try
            {
                lines.Add($"{name}={read()}");
            }
            catch
            {
                lines.Add($"{name}=n/a");
            }
        }

        private void ApplyConnectionSettings(
            string host,
            int port,
            string user,
            string domain,
            string password,
            int desktopWidth,
            int desktopHeight,
            int viewportWidth,
            int viewportHeight)
        {
            switch (_rdp)
            {
                case AxMsRdpClient9NotSafeForScripting r9:
                    Configure(r9, r9.AdvancedSettings9, host, port, user, domain, password, desktopWidth, desktopHeight, viewportWidth, viewportHeight, _desktopScalePercent, _deviceScalePercent);
                    break;
                case AxMsRdpClient10NotSafeForScripting r10:
                    Configure(r10, r10.AdvancedSettings9, host, port, user, domain, password, desktopWidth, desktopHeight, viewportWidth, viewportHeight, _desktopScalePercent, _deviceScalePercent);
                    break;
                case AxMsRdpClient11NotSafeForScripting r11:
                    Configure(r11, r11.AdvancedSettings9, host, port, user, domain, password, desktopWidth, desktopHeight, viewportWidth, viewportHeight, _desktopScalePercent, _deviceScalePercent);
                    break;
                default:
                    throw new InvalidOperationException("Unknown ActiveX type.");
            }
        }

        private static void Configure(
            dynamic ax,
            IMsRdpClientAdvancedSettings8 adv,
            string host,
            int port,
            string user,
            string domain,
            string password,
            int desktopWidth,
            int desktopHeight,
            int viewportWidth,
            int viewportHeight,
            int desktopScalePercent,
            int deviceScalePercent)
        {
            ax.Server = host;
            ax.UserName = user;
            try { ax.Domain = domain; } catch { /* optional */ }

            adv.RDPPort = port;
            adv.ClearTextPassword = password;
            adv.EnableCredSspSupport = true;
            adv.AuthenticationLevel = 2;
            adv.SmartSizing = false;
            adv.RedirectClipboard = true;
            TrySetScaleFactor((System.Windows.Forms.Control)ax, adv, desktopScalePercent, deviceScalePercent);

            ax.DesktopWidth = desktopWidth;
            ax.DesktopHeight = desktopHeight;
            ax.Width = viewportWidth;
            ax.Height = viewportHeight;
            ax.ColorDepth = 32;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposeRequested = true;
                if (Interlocked.Exchange(ref _disposeState, 1) == 0)
                {
                    if (InvokeRequired && IsHandleCreated)
                    {
                        try { Invoke(DisposeCore); } catch { /* best effort on teardown */ }
                    }
                    else if (!InvokeRequired)
                    {
                        DisposeCore();
                    }
                }
            }

            base.Dispose(disposing);
        }

        private void DisposeCore()
        {
            StopPollTimerCore();
            DisconnectSessionCore();
        }    }
}
