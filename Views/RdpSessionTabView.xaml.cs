using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Netvium.Native;
using Netvium.Rdp;
using Netvium.Rdp.Clean;
using Netvium.ViewModels;

namespace Netvium.Views
{
    public partial class RdpSessionTabView : System.Windows.Controls.UserControl
    {
        private System.Windows.Forms.Integration.WindowsFormsHost? _formsHost;
        private RdpHostPanel? _hostPanel;
        private RdpSessionHostControl? _hostControl;
        private bool _hostPrepared;
        private bool _isViewLoaded;
        private bool _connectStartRequested;
        private TaskCompletionSource<bool>? _loadedTcs;
        private bool _isRehostingFormsHost;
        private bool _suspendRemoteDisplayUpdates;
        private DateTime _lastFormsHostRehostUtc;
        private int _lastCompletedViewportPixelWidth;
        private int _lastCompletedViewportPixelHeight;
        private int _pendingViewportPixelWidth;
        private int _pendingViewportPixelHeight;
        private bool _forceNextResize;
        private readonly SemaphoreSlim _hostLifecycleGate = new(1, 1);
        private DateTime _lastRenderingDiagnosticsLogUtc = DateTime.MinValue;

        public event EventHandler? ReconnectRequested;
        public event EventHandler? DisconnectRequested;
        public event EventHandler? SendCtrlAltDelRequested;
        public event EventHandler<RdpResolutionProfile>? ResolutionProfileReconnectRequested;
        public event EventHandler? CopyDiagnosticsRequested;
        public event EventHandler? FailureRetryRequested;

        private DispatcherTimer? _resizeDebounce;
        private DispatcherTimer? _displayEnvironmentCommitTimer;
        private DispatcherTimer? _layoutVerificationTimer;
        private RdpResolutionProfile _resolutionProfile = RdpViewportSizing.DefaultProfile;
        private double _sidebarWidthDip;
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

        private void LogRdp(string message) => RdpCleanHostLogger.Log(message, SessionId);

        public RdpResolutionProfile ResolutionProfile
        {
            get => _resolutionProfile;
            set => _resolutionProfile = value;
        }

        public double SidebarWidthDip
        {
            get => _sidebarWidthDip;
            set => _sidebarWidthDip = value;
        }

        internal System.Windows.Forms.Integration.WindowsFormsHost? FormsHostElement => _formsHost;
        internal System.Windows.Controls.Border SessionLoadingOverlay => LoadingOverlay;
        internal System.Windows.Controls.TextBlock SessionLoadingStatusText => LoadingStatusText;

        internal RdpSessionHostControl HostControl
        {
            get
            {
                if (!_hostPrepared || _hostControl == null)
                    throw new InvalidOperationException("RDP host is not prepared. Call PrepareHostForConnectAsync first.");
                return _hostControl;
            }
        }

        internal RdpSessionTabView()
        {
            InitializeComponent();
        }

        public async Task WaitForViewLoadedAsync(int timeoutMs = 15000)
        {
            if (_isViewLoaded)
                return;

            _loadedTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (IsLoaded)
            {
                MarkViewLoaded();
                return;
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            await using var reg = cts.Token.Register(() => _loadedTcs.TrySetResult(false));
            var loaded = await _loadedTcs.Task.ConfigureAwait(true);
            if (!loaded && !_isViewLoaded)
                LogRdp("RdpSessionTabView Loaded timeout");
        }

        public async Task<bool> PrepareHostForConnectAsync(int timeoutMs = 10000)
        {
            await _hostLifecycleGate.WaitAsync().ConfigureAwait(true);
            try
            {
                if (_hostPrepared)
                    return IsHostReady();

                await WaitForViewLoadedAsync(timeoutMs).ConfigureAwait(true);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task.ConfigureAwait(true);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ConfigureAwait(true);

                if (!await WaitForNonZeroSizeAsync(timeoutMs).ConfigureAwait(true))
                {
                    LogHostMetrics("PrepareHost surface timeout", connectAllowed: false);
                    return false;
                }

                var created = await Dispatcher.InvokeAsync(CreateHostAfterSurfaceSized, DispatcherPriority.Loaded).Task.ConfigureAwait(true);
                var preparedHost = _hostControl;
                if (!created || preparedHost == null || _hostPanel == null || _formsHost == null)
                {
                    LogHostMetrics("PrepareHost create failed", connectAllowed: false);
                    return false;
                }

                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render).Task.ConfigureAwait(true);
                await Task.Delay(50).ConfigureAwait(true);

                var activated = await Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_hostControl, preparedHost) || preparedHost.IsDisposed)
                        return false;

                    preparedHost.EnsureActiveXCreated();
                    preparedHost.SyncRdpBounds();
                    return true;
                }, DispatcherPriority.Send).Task.ConfigureAwait(true);

                if (!activated || !ReferenceEquals(_hostControl, preparedHost))
                {
                    LogHostMetrics("PrepareHost superseded", connectAllowed: false);
                    return false;
                }

                _hostPrepared = true;
                var ready = IsHostReady();
                LogHostMetrics("PrepareHost complete", connectAllowed: ready);
                return ready;
            }
            finally
            {
                _hostLifecycleGate.Release();
            }
        }

        internal bool IsWpfSurfaceReady() =>
            IsVisible
            && ActualWidth > 0
            && ActualHeight > 0
            && RdpSurface.ActualWidth > 0
            && RdpSurface.ActualHeight > 0;

        internal bool IsHostReady()
        {
            if (!IsWpfSurfaceReady() || _hostPanel == null || _hostControl == null)
                return false;

            return ReadPanelAndHostReady();
        }

        private (int Width, int Height) GetSurfaceSize()
        {
            var w = (int)Math.Max(1, Math.Floor(RdpSurface.ActualWidth));
            var h = (int)Math.Max(1, Math.Floor(RdpSurface.ActualHeight));
            return (w, h);
        }

        /// <summary>WinForms pixel size for the hosted panel (device pixels).</summary>
        private (int Width, int Height) GetSurfacePixelSize()
        {
            var dipW = RdpSurface.ActualWidth;
            var dipH = RdpSurface.ActualHeight;
            if (dipW <= 0 || dipH <= 0)
                return (0, 0);

            var dpi = VisualTreeHelper.GetDpi(RdpSurface);
            return (
                (int)Math.Max(1, Math.Ceiling(dipW * dpi.DpiScaleX)),
                (int)Math.Max(1, Math.Ceiling(dipH * dpi.DpiScaleY)));
        }

        public void ApplyLayoutResize(bool updateSessionDisplay = false)
        {
            // External layout notifications must not bypass the tab's stabilization queue.
            SyncHostLayoutFromSurface(updateSessionDisplay: false);
            if (updateSessionDisplay)
                ScheduleLayoutResize();
        }

        public RdpSizingPlan BuildConnectSizingPlan()
        {
            var dpi = VisualTreeHelper.GetDpi(RdpSurface);
            return RdpViewportSizing.Compute(
                RdpSurface.ActualWidth,
                RdpSurface.ActualHeight,
                dpi.DpiScaleX,
                dpi.DpiScaleY,
                _resolutionProfile,
                _sidebarWidthDip);
        }

        public void LogSizingDiagnostics(RdpSizingPlan plan)
        {
            var panelClient = _hostPanel != null
                ? $"{_hostPanel.ClientSize.Width}x{_hostPanel.ClientSize.Height}"
                : "not-created";
            var (_, _, _, axBounds) = _hostControl?.GetActiveXMetrics()
                ?? (0, 0, nint.Zero, System.Drawing.Rectangle.Empty);
            LogRdp(
                RdpViewportSizing.FormatDiagnostics(plan, _hostPanel?.ClientSize.Width ?? 0, _hostPanel?.ClientSize.Height ?? 0, axBounds.Width, axBounds.Height));
        }

        public void LogSizingDiagnostics()
        {
            if (RdpSurface.ActualWidth > 0 && RdpSurface.ActualHeight > 0)
                LogSizingDiagnostics(BuildConnectSizingPlan());
        }

        private void SyncHostLayoutFromSurface(bool updateSessionDisplay = false)
        {
            if (_hostPanel == null || _hostControl == null)
                return;

            if (RdpSurface.ActualWidth <= 0 || RdpSurface.ActualHeight <= 0)
                return;

            var (pixelWidth, pixelHeight) = ForceFormsHostToSurfaceBounds();
            _hostPanel.SyncFillLayout();
            if (pixelWidth > 0 && pixelHeight > 0)
                _hostControl.SyncLocalViewport(pixelWidth, pixelHeight, updateSessionDisplay);

            if (_formsHost == null)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                if (_hostPanel == null || _hostControl == null)
                    return;

                var (deferredPixelWidth, deferredPixelHeight) = ForceFormsHostToSurfaceBounds();
                _hostPanel.SyncFillLayout();
                // The debounced resize performs the single remote display update.
                if (deferredPixelWidth > 0 && deferredPixelHeight > 0)
                    _hostControl.SyncLocalViewport(deferredPixelWidth, deferredPixelHeight, updateSessionDisplay: false);
            }, DispatcherPriority.Render);
        }

        private (int PixelWidth, int PixelHeight) ForceFormsHostToSurfaceBounds()
        {
            if (_formsHost == null)
                return (0, 0);

            var width = RdpSurface.ActualWidth;
            var height = RdpSurface.ActualHeight;
            if (width <= 0 || height <= 0)
                return (0, 0);

            // Keep the WPF host in normal stretch layout. Explicit Width/Height on HwndHost
            // can remain arranged at the previous monitor size after a per-monitor DPI move.
            _formsHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            _formsHost.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            _formsHost.Margin = new Thickness(0);
            _formsHost.Width = double.NaN;
            _formsHost.Height = double.NaN;

            _formsHost.InvalidateMeasure();
            _formsHost.InvalidateArrange();
            RdpSurface.InvalidateMeasure();
            RdpSurface.InvalidateArrange();
            RdpSurface.UpdateLayout();

            // HwndHost occasionally misses the arrange pass while its parent is being moved
            // between monitors. Arrange it explicitly before sizing the native HWND.
            _formsHost.Measure(new System.Windows.Size(width, height));
            _formsHost.Arrange(new Rect(0, 0, width, height));
            _formsHost.UpdateLayout();

            var (pixelWidth, pixelHeight) = GetSurfacePixelSize();
            if (pixelWidth <= 0 || pixelHeight <= 0)
                return (0, 0);

            ForceNativeHostPixelBounds(pixelWidth, pixelHeight);
            return (pixelWidth, pixelHeight);
        }

        private void ForceNativeHostPixelBounds(int pixelWidth, int pixelHeight)
        {
            if (_formsHost == null || pixelWidth <= 0 || pixelHeight <= 0)
                return;

            try
            {
                var hwnd = _formsHost.Handle;
                if (hwnd != nint.Zero)
                {
                    const uint swpNoZOrder = 0x0004;
                    const uint swpNoActivate = 0x0010;
                    const uint swpShowWindow = 0x0040;
                    NativeMethods.SetWindowPos(
                        hwnd,
                        nint.Zero,
                        0,
                        0,
                        pixelWidth,
                        pixelHeight,
                        swpNoZOrder | swpNoActivate | swpShowWindow);
                }

                // Keep the complete native child chain at the same physical-pixel size even
                // if WindowsFormsHost reports a stale WPF ActualHeight for one layout cycle.
                if (_hostPanel != null)
                {
                    _hostPanel.SetBounds(
                        0,
                        0,
                        pixelWidth,
                        pixelHeight,
                        System.Windows.Forms.BoundsSpecified.All);
                    _hostPanel.SyncFillLayout();
                }
            }
            catch (Exception ex)
            {
                LogRdp($"Native host resize failed ({pixelWidth}x{pixelHeight}): {ex.Message}");
            }
        }
        private bool RehostFormsHostIfStale()
        {
            if (_isRehostingFormsHost
                || _formsHost == null
                || _hostPanel == null
                || _hostControl?.IsRdpConnected != true)
            {
                return false;
            }

            var surfaceWidth = RdpSurface.ActualWidth;
            var surfaceHeight = RdpSurface.ActualHeight;
            var (pixelWidth, pixelHeight) = GetSurfacePixelSize();
            if (surfaceWidth <= 0 || surfaceHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
                return false;

            var dipMismatch = Math.Abs(_formsHost.ActualWidth - surfaceWidth) > 1
                || Math.Abs(_formsHost.ActualHeight - surfaceHeight) > 1;
            var panelMismatch = Math.Abs(_hostPanel.ClientSize.Width - pixelWidth) > 2
                || Math.Abs(_hostPanel.ClientSize.Height - pixelHeight) > 2;
            var (activeXWidth, activeXHeight, _, _) = _hostControl.GetActiveXMetrics();
            var activeXMismatch = Math.Abs(activeXWidth - pixelWidth) > 2
                || Math.Abs(activeXHeight - pixelHeight) > 2;

            var nativeWidth = 0;
            var nativeHeight = 0;
            var nativeMismatch = true;
            try
            {
                if (_formsHost.Handle != nint.Zero
                    && NativeMethods.GetClientRect(_formsHost.Handle, out var rect))
                {
                    nativeWidth = rect.Right - rect.Left;
                    nativeHeight = rect.Bottom - rect.Top;
                    nativeMismatch = Math.Abs(nativeWidth - pixelWidth) > 2
                        || Math.Abs(nativeHeight - pixelHeight) > 2;
                }
            }
            catch
            {
                nativeMismatch = true;
            }

            if (!dipMismatch && !panelMismatch && !activeXMismatch && !nativeMismatch)
                return false;

            var now = DateTime.UtcNow;
            // Rehosting is only reached from a settled resize/monitor commit. Keep a
            // short guard against duplicate WPF notifications without delaying a real
            // maximize or restore that follows a monitor transition.
            if (now - _lastFormsHostRehostUtc < TimeSpan.FromMilliseconds(150))
                return false;

            var oldHost = _formsHost;
            var oldHostHwnd = oldHost.Handle;
            var panelHwnd = _hostPanel.IsHandleCreated ? _hostPanel.Handle : nint.Zero;
            var replacement = new System.Windows.Forms.Integration.WindowsFormsHost
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Black,
                Margin = new Thickness(0)
            };

            _isRehostingFormsHost = true;
            try
            {
                oldHost.Child = null;
                RdpSurface.Children.Remove(oldHost);

                _formsHost = replacement;
                RdpSurface.Children.Add(replacement);
                replacement.Child = _hostPanel;

                RdpSurface.InvalidateMeasure();
                RdpSurface.InvalidateArrange();
                RdpSurface.UpdateLayout();
                replacement.Measure(new System.Windows.Size(surfaceWidth, surfaceHeight));
                replacement.Arrange(new Rect(0, 0, surfaceWidth, surfaceHeight));
                replacement.UpdateLayout();

                ForceNativeHostPixelBounds(pixelWidth, pixelHeight);
                _hostPanel.SyncFillLayout();
                _hostControl.SyncLocalViewport(pixelWidth, pixelHeight, updateSessionDisplay: false);

                try { oldHost.Dispose(); } catch { /* detached shell only */ }

                _lastFormsHostRehostUtc = now;
                LogRdp(
                    $"WindowsFormsHost rehosted oldHwnd=0x{oldHostHwnd:X} newHwnd=0x{replacement.Handle:X} "
                    + $"panelHwnd=0x{panelHwnd:X} oldNative={nativeWidth}x{nativeHeight} "
                    + $"target={pixelWidth}x{pixelHeight} surface={surfaceWidth:F0}x{surfaceHeight:F0}");
                return true;
            }
            catch (Exception ex)
            {
                LogRdp($"WindowsFormsHost rehost failed: {ex.Message}");
                try
                {
                    replacement.Child = null;
                    RdpSurface.Children.Remove(replacement);
                    replacement.Dispose();
                }
                catch { /* best-effort rollback */ }

                _formsHost = oldHost;
                try
                {
                    if (!RdpSurface.Children.Contains(oldHost))
                        RdpSurface.Children.Add(oldHost);
                    if (oldHost.Child == null)
                        oldHost.Child = _hostPanel;
                }
                catch { /* diagnostics will expose any remaining mismatch */ }

                return false;
            }
            finally
            {
                _isRehostingFormsHost = false;
            }
        }

        public void BeginInteractiveWindowMove()
        {
            _suspendRemoteDisplayUpdates = true;
            _resizeDebounce?.Stop();
        }

        public void EndInteractiveWindowMove()
        {
            _suspendRemoteDisplayUpdates = false;
            HandleDisplayEnvironmentChanged();
        }

        public void HandleWindowStateChanged()
        {
            if (_suspendRemoteDisplayUpdates)
                return;

            // Maximize/restore does not consistently emit WM_EXITSIZEMOVE. Route it
            // through the same settled resize path so the native HwndHost cannot keep
            // the previous window height and leave a black strip below the desktop.
            _forceNextResize = true;
            SyncHostLayoutFromSurface(updateSessionDisplay: false);
            ScheduleLayoutResize();
        }

        public void HandleDisplayEnvironmentChanged()
        {
            _forceNextResize = true;
            _resizeDebounce?.Stop();
            ApplyDpiScaleToHost();
            RdpSurface.InvalidateMeasure();
            RdpSurface.InvalidateArrange();
            RdpSurface.UpdateLayout();
            SyncHostLayoutFromSurface(updateSessionDisplay: false);

            if (_suspendRemoteDisplayUpdates)
                return;

            ScheduleDisplayEnvironmentCommit();
            Dispatcher.BeginInvoke(() =>
            {
                if (_suspendRemoteDisplayUpdates)
                    return;
                ApplyDpiScaleToHost();
                RdpSurface.InvalidateMeasure();
                RdpSurface.UpdateLayout();
                SyncHostLayoutFromSurface(updateSessionDisplay: false);
                ScheduleDisplayEnvironmentCommit();
            }, DispatcherPriority.Render);
        }

        private void ScheduleDisplayEnvironmentCommit()
        {
            if (_suspendRemoteDisplayUpdates)
                return;

            _displayEnvironmentCommitTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _displayEnvironmentCommitTimer.Stop();
            _displayEnvironmentCommitTimer.Tick -= OnDisplayEnvironmentCommitTick;
            _displayEnvironmentCommitTimer.Tick += OnDisplayEnvironmentCommitTick;
            _displayEnvironmentCommitTimer.Start();
        }

        private void OnDisplayEnvironmentCommitTick(object? sender, EventArgs e)
        {
            _displayEnvironmentCommitTimer?.Stop();
            if (_suspendRemoteDisplayUpdates)
                return;

            ApplyDpiScaleToHost();
            RdpSurface.InvalidateMeasure();
            RdpSurface.InvalidateArrange();
            RdpSurface.UpdateLayout();
            SyncHostLayoutFromSurface(updateSessionDisplay: false);

            // HwndHost can retain its previous monitor DPI context. Recreate only this
            // lightweight wrapper once after the monitor transition has settled.
            var rehosted = RehostFormsHostIfStale();
            _forceNextResize = true;
            ApplyViewportResizeNow();

            Dispatcher.BeginInvoke(() =>
            {
                if (_suspendRemoteDisplayUpdates)
                    return;
                ApplyDpiScaleToHost();
                RdpSurface.InvalidateMeasure();
                RdpSurface.InvalidateArrange();
                RdpSurface.UpdateLayout();
                SyncHostLayoutFromSurface(updateSessionDisplay: false);
                _forceNextResize = true;
                ApplyViewportResizeNow();
                LogRdp($"Monitor transition committed rehosted={rehosted}");
            }, DispatcherPriority.ContextIdle);
        }

        private void ScheduleLayoutResize()
        {
            if (_displayEnvironmentCommitTimer?.IsEnabled == true)
                return;

            var (pixelWidth, pixelHeight) = GetSurfacePixelSize();
            if (pixelWidth <= 0 || pixelHeight <= 0)
                return;

            if (!_forceNextResize
                && pixelWidth == _lastCompletedViewportPixelWidth
                && pixelHeight == _lastCompletedViewportPixelHeight)
                return;

            var samePendingSize = pixelWidth == _pendingViewportPixelWidth
                && pixelHeight == _pendingViewportPixelHeight;
            if (samePendingSize && _resizeDebounce?.IsEnabled == true)
                return;

            _pendingViewportPixelWidth = pixelWidth;
            _pendingViewportPixelHeight = pixelHeight;
            _resizeDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _resizeDebounce.Stop();
            _resizeDebounce.Tick -= OnResizeDebounceTick;
            _resizeDebounce.Tick += OnResizeDebounceTick;
            _resizeDebounce.Start();
        }

        private void OnResizeDebounceTick(object? sender, EventArgs e)
        {
            _resizeDebounce?.Stop();
            if (_suspendRemoteDisplayUpdates
                || RdpSurface.ActualWidth <= 0
                || RdpSurface.ActualHeight <= 0)
            {
                return;
            }

            ApplyViewportResizeNow();
        }

        private void ApplyViewportResizeNow()
        {
            if (_hostPanel == null || _hostControl == null)
                return;

            var (pixelWidth, pixelHeight) = GetSurfacePixelSize();
            if (pixelWidth <= 0 || pixelHeight <= 0)
                return;

            var updateRemote = _hostControl.IsRdpConnected;
            _forceNextResize = false;

            // First settle the complete local HWND chain. Sending the new desktop size
            // while ActiveX is still at the old height produces a correct remote desktop
            // inside a stale local viewport (the visible black strip in maximized mode).
            SyncHostLayoutFromSurface(updateSessionDisplay: false);
            var rehosted = RehostFormsHostIfStale();
            SyncHostLayoutFromSurface(updateSessionDisplay: updateRemote);
            _lastCompletedViewportPixelWidth = pixelWidth;
            _lastCompletedViewportPixelHeight = pixelHeight;
            ScheduleLayoutVerification();
            if (rehosted)
                LogRdp($"Settled viewport rehosted at {pixelWidth}x{pixelHeight}");
            LogRenderingDiagnostics(RdpDiagnosticsPhase.AfterResize);
        }

        private void ScheduleLayoutVerification()
        {
            _layoutVerificationTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _layoutVerificationTimer.Stop();
            _layoutVerificationTimer.Tick -= OnLayoutVerificationTick;
            _layoutVerificationTimer.Tick += OnLayoutVerificationTick;
            _layoutVerificationTimer.Start();
        }

        private void OnLayoutVerificationTick(object? sender, EventArgs e)
        {
            _layoutVerificationTimer?.Stop();
            if (_suspendRemoteDisplayUpdates || _hostControl?.IsRdpConnected != true)
                return;

            SyncHostLayoutFromSurface(updateSessionDisplay: false);
            if (!RehostFormsHostIfStale())
                return;

            // A late WPF arrange pass replaced our bounds with the previous size.
            // Rehost once, then reassert both the native bounds and remote resolution.
            SyncHostLayoutFromSurface(updateSessionDisplay: true);
            LogRdp("Late viewport mismatch corrected after settled resize");
        }

        private void OnRdpSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (RdpSurface.ActualWidth <= 0
                || RdpSurface.ActualHeight <= 0)
            {
                return;
            }

            SyncHostLayoutFromSurface(updateSessionDisplay: false);
            if (_suspendRemoteDisplayUpdates)
                return;

            ScheduleLayoutResize();
        }

        private bool ReadPanelAndHostReady()
        {
            if (_hostPanel == null || _hostControl == null)
                return false;

            if (_hostPanel.InvokeRequired)
                return (bool)_hostPanel.Invoke(ReadPanelAndHostReady)!;

            if (_hostPanel.ClientSize.Width < RdpHostPanel.StartupMinWidth
                || _hostPanel.ClientSize.Height < RdpHostPanel.StartupMinHeight)
                return false;

            return _hostControl.IsLayoutReadyForConnect();
        }

        public void ShowConnecting(string message)
        {
            HideFailure();
            LoadingStatusText.Text = message;
            LiveTimelineText.Text = string.Empty;
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        public void HideConnecting() => LoadingOverlay.Visibility = Visibility.Collapsed;

        public void AppendTimeline(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            LiveTimelineText.Text = string.IsNullOrWhiteSpace(LiveTimelineText.Text)
                ? line
                : LiveTimelineText.Text + Environment.NewLine + line;
        }

        public void ShowFailure(string title, string message, string? diagnostics = null)
        {
            HideConnecting();
            FailureOverlay.Visibility = Visibility.Visible;
            FailureTitle.Text = title;
            FailureMessage.Text = message;
            DiagLayout.Text = string.IsNullOrWhiteSpace(diagnostics) ? "—" : diagnostics;
        }

        public void HideFailure() => FailureOverlay.Visibility = Visibility.Collapsed;

        public void SetSessionStatus(string message, bool connected)
        {
            if (connected)
                HideConnecting();
        }

        public void SetConnectedStatus()
        {
            HideConnecting();
            HideFailure();
        }

        public void SetCaptureUnavailableStatus()
        {
        }

        public void SetResolutionProfile(RdpResolutionProfile profile) =>
            _resolutionProfile = profile;

        public void ApplyDpiScaleToHost()
        {
            var (desktopScale, deviceScale) = RdpDpiHelper.GetScalePercentsFromVisual(RdpSurface);
            _hostControl?.SetDpiScale(desktopScale, deviceScale);
        }

        public string BuildRenderingDiagnosticsReport(RdpDiagnosticsPhase phase) =>
            RdpRenderingDiagnostics.BuildReport(
                phase,
                this,
                this,
                RdpSurface,
                _formsHost,
                _hostPanel,
                _hostControl,
                RdpSurface.ActualWidth > 0 ? BuildConnectSizingPlan() : null,
                sessionId: SessionId);

        public void LogRenderingDiagnostics(RdpDiagnosticsPhase phase, string? notes = null)
        {
            // Resize can raise a burst of events while the window is being dragged.
            // Keep diagnostics useful without turning every event into a disk write.
            if (phase == RdpDiagnosticsPhase.AfterResize)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastRenderingDiagnosticsLogUtc).TotalMilliseconds < 500)
                    return;
                _lastRenderingDiagnosticsLogUtc = now;
            }

            RdpRenderingDiagnostics.LogReport(
                phase,
                this,
                this,
                RdpSurface,
                _formsHost,
                _hostPanel,
                _hostControl,
                RdpSurface.ActualWidth > 0 ? BuildConnectSizingPlan() : null,
                notes,
                SessionId);
        }

        public string BuildLayoutDiagnostics() => BuildRenderingDiagnosticsReport(RdpDiagnosticsPhase.Manual);

        public string BuildDetailedMeasurements()
        {
            var wfhW = _formsHost?.ActualWidth ?? 0;
            var wfhH = _formsHost?.ActualHeight ?? 0;
            var panelClient = _hostPanel != null
                ? $"{_hostPanel.ClientSize.Width}x{_hostPanel.ClientSize.Height}"
                : "not-created";
            var (axW, axH, axHandle, axBounds) = _hostControl?.GetActiveXMetrics() ?? (0, 0, nint.Zero, System.Drawing.Rectangle.Empty);
            var hostDiag = _hostControl?.GetViewportDiagnostics();

            var (surfaceW, surfaceH) = GetSurfaceSize();
            var (pixelW, pixelH) = GetSurfacePixelSize();
            var plan = BuildConnectSizingPlan();
            var dpi = VisualTreeHelper.GetDpi(RdpSurface);
            var lastResize = hostDiag?.LastResizeUtc.HasValue == true
                ? hostDiag.Value.LastResizeUtc.Value.ToString("O")
                : "n/a";
            var updateSessionResult = hostDiag?.LastUpdateSessionDisplaySucceeded switch
            {
                true => "yes",
                false => "no",
                _ => "n/a",
            };

            return $"SessionId={SessionId}{Environment.NewLine}"
                + $"Root={ActualWidth:F0}x{ActualHeight:F0}{Environment.NewLine}"
                + $"SurfaceDIP={surfaceW}x{surfaceH}{Environment.NewLine}"
                + $"SurfacePixels={pixelW}x{pixelH}{Environment.NewLine}"
                + $"Profile={plan.ProfileLabel}{Environment.NewLine}"
                + $"RequestedRemote={plan.RequestedDesktopWidth}x{plan.RequestedDesktopHeight}{Environment.NewLine}"
                + $"SidebarDIP={_sidebarWidthDip:F0}{Environment.NewLine}"
                + $"WFH Actual={wfhW:F0}x{wfhH:F0}{Environment.NewLine}"
                + $"Panel ClientSize={panelClient}{Environment.NewLine}"
                + $"HostClient={hostDiag?.HostClientWidth ?? 0}x{hostDiag?.HostClientHeight ?? 0}{Environment.NewLine}"
                + $"ActiveX Version={_hostControl?.ActiveControlType ?? "none"}{Environment.NewLine}"
                + $"ActiveX CLSID={_hostControl?.ActiveControlClsid ?? "n/a"}{Environment.NewLine}"
                + $"ActiveX Size={axW}x{axH}{Environment.NewLine}"
                + $"ActiveX Bounds={axBounds.Width}x{axBounds.Height}{Environment.NewLine}"
                + $"Desktop={hostDiag?.DesktopWidth ?? 0}x{hostDiag?.DesktopHeight ?? 0}{Environment.NewLine}"
                + $"DisplayMode={hostDiag?.RenderingMode ?? "n/a"}{Environment.NewLine}"
                + $"SmartSizing={(hostDiag?.SmartSizing == true ? "true" : "false")}{Environment.NewLine}"
                + $"DisplayUpdateFallback={hostDiag?.DisplayUpdateFallbackApplied == true}{Environment.NewLine}"
                + $"RenderingMode={hostDiag?.RenderingMode ?? "n/a"}{Environment.NewLine}"
                + $"ScaleFactors={hostDiag?.DesktopScalePercent ?? 0}/{hostDiag?.DeviceScalePercent ?? 0}{Environment.NewLine}"
                + $"DPI Scale={dpi.DpiScaleX:F2}x{dpi.DpiScaleY:F2}{Environment.NewLine}"
                + $"DPI X/Y={dpi.PixelsPerInchX:F1}/{dpi.PixelsPerInchY:F1}{Environment.NewLine}"
                + $"LastResizeUtc={lastResize}{Environment.NewLine}"
                + $"UpdateSessionDisplaySettings={updateSessionResult}{Environment.NewLine}"
                + $"ActiveX Handle={axHandle}{Environment.NewLine}"
                + $"Connect allowed={IsHostReady()}";
        }

        public string BuildDiagnosticsSnapshot() =>
            BuildRenderingDiagnosticsReport(RdpDiagnosticsPhase.Manual);

        internal void TriggerConnectIfReady()
        {
            if (_connectStartRequested)
                return;

            if (DataContext is not RdpSessionViewModel vm)
            {
                LogRdp("FATAL ViewModelBound=no — RdpSessionTabView Loaded without RdpSessionViewModel");
                ShowFailure(
                    "Binding error",
                    "RdpSessionViewModel is not bound to this tab.",
                    BuildDetailedMeasurements());
                return;
            }

            _connectStartRequested = true;
            _ = vm.StartConnectAsync();
        }

        private async Task<bool> WaitForNonZeroSizeAsync(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                await ForceLayoutPassAsync().ConfigureAwait(true);
                if (ActualWidth > 0
                    && RdpSurface.ActualWidth >= RdpHostPanel.StartupMinWidth
                    && RdpSurface.ActualHeight >= RdpHostPanel.StartupMinHeight)
                {
                    var (surfaceW, surfaceH) = GetSurfaceSize();
                    LogRdp(
                        $"RdpSurface size ready tab={ActualWidth:F0}x{ActualHeight:F0} "
                        + $"surface={surfaceW}x{surfaceH}");
                    return true;
                }

                await Task.Delay(50).ConfigureAwait(true);
            }

            return false;
        }

        private bool CreateHostAfterSurfaceSized()
        {
            if (_formsHost != null && _hostPanel != null && _hostControl != null && !_hostControl.IsDisposed)
                return IsHostReady();

            _formsHost = new System.Windows.Forms.Integration.WindowsFormsHost
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Black
            };

            RdpSurface.Children.Add(_formsHost);

            _hostPanel = new RdpHostPanel();
            _hostControl = new RdpSessionHostControl { SessionId = SessionId };
            _hostPanel.AttachControl(_hostControl);
            _formsHost.Child = _hostPanel;

            UpdateLayout();
            RdpSurface.UpdateLayout();
            _formsHost.UpdateLayout();
            _hostPanel.PerformLayout();
            _hostControl.PerformLayout();
            SyncHostLayoutFromSurface();

            var (surfaceW, surfaceH) = GetSurfaceSize();
            var (pixelW, pixelH) = GetSurfacePixelSize();
            LogRdp(
                $"RDP host created surface={surfaceW}x{surfaceH} "
                + $"pixels={pixelW}x{pixelH} panel={_hostPanel.ClientSize.Width}x{_hostPanel.ClientSize.Height}");

            return surfaceW >= RdpHostPanel.StartupMinWidth && surfaceH >= RdpHostPanel.StartupMinHeight;
        }

        private void LogHostMetrics(string phase, bool connectAllowed)
        {
            var panelClient = _hostPanel != null
                ? $"{_hostPanel.ClientSize.Width}x{_hostPanel.ClientSize.Height}"
                : "not-created";
            var (axW, axH, axHandle, axBounds) = _hostControl?.GetActiveXMetrics() ?? (0, 0, nint.Zero, System.Drawing.Rectangle.Empty);
            var wfhW = _formsHost?.ActualWidth ?? 0;
            var wfhH = _formsHost?.ActualHeight ?? 0;

            LogRdp(
                $"{phase} RdpSurface={RdpSurface.ActualWidth:F0}x{RdpSurface.ActualHeight:F0} "
                + $"WFH={wfhW:F0}x{wfhH:F0} "
                + $"Panel ClientSize={panelClient} ActiveX Bounds={axBounds.Width}x{axBounds.Height} "
                + $"ActiveX Handle={axHandle} Connect allowed={connectAllowed}");
        }

        private async Task ForceLayoutPassAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                RootGrid?.UpdateLayout();
                RdpSurface?.UpdateLayout();
                _formsHost?.UpdateLayout();
                UpdateLayout();
                _hostPanel?.SyncAttachedControlBounds();
                _hostControl?.SyncRdpBounds();
            }, DispatcherPriority.Render).Task.ConfigureAwait(true);
        }

        private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e) => CopyDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
        private void OnFailureRetryClick(object sender, RoutedEventArgs e) => FailureRetryRequested?.Invoke(this, EventArgs.Empty);

        private void OnSessionViewLoaded(object sender, RoutedEventArgs e)
        {
            MarkViewLoaded();
            TriggerConnectIfReady();
        }

        private void MarkViewLoaded()
        {
            if (_isViewLoaded)
                return;

            _isViewLoaded = true;
            LogRdp(
                $"RdpSessionTabView Loaded ActualWidth={ActualWidth:F0} ActualHeight={ActualHeight:F0} "
                + $"RdpSurface={RdpSurface.ActualWidth:F0}x{RdpSurface.ActualHeight:F0}");
            _loadedTcs?.TrySetResult(true);
        }

        private void OnSessionViewSizeChanged(object sender, SizeChangedEventArgs e) =>
            OnRdpSurfaceSizeChanged(sender, e);

        public async Task ResetHostForReconnectAsync()
        {
            await _hostLifecycleGate.WaitAsync().ConfigureAwait(true);
            try
            {
                DisposeHostResourcesCore();
            }
            finally
            {
                _hostLifecycleGate.Release();
            }
        }

        public void DisposeHostResources()
        {
            if (Dispatcher.CheckAccess())
            {
                DisposeHostResourcesCore();
                return;
            }

            try { Dispatcher.Invoke(DisposeHostResourcesCore); } catch { /* teardown is best effort */ }
        }

        private void DisposeHostResourcesCore()
        {
            _hostPrepared = false;
            _connectStartRequested = false;

            var formsHost = _formsHost;
            var hostPanel = _hostPanel;
            var hostControl = _hostControl;
            _formsHost = null;
            _hostPanel = null;
            _hostControl = null;

            try { if (formsHost != null) formsHost.Child = null; } catch { /* optional */ }
            try { if (formsHost != null) RdpSurface.Children.Remove(formsHost); } catch { /* optional */ }
            try { hostControl?.Dispose(); } catch { /* optional */ }
            try { hostPanel?.Dispose(); } catch { /* optional */ }
            try { formsHost?.Dispose(); } catch { /* optional */ }
        }    }
}