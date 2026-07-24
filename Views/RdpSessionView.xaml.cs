using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Netvium.Rdp;

namespace Netvium.Views
{
    public partial class RdpSessionView : System.Windows.Controls.UserControl
    {
        private readonly RdpHostPanel _hostPanel = new();
        private bool _panelAttached;

        public event EventHandler? ReconnectRequested;
        public event EventHandler? DisconnectRequested;
        public event EventHandler? SendCtrlAltDelRequested;
        public event EventHandler<bool>? SmartSizingToggled;
        public event EventHandler? CopyDiagnosticsRequested;
        public event EventHandler? FailureRetryRequested;

        public RdpHostPanel HostPanel => _hostPanel;

        public RdpSessionView()
        {
            InitializeComponent();
        }

        private void OnSessionViewLoaded(object sender, RoutedEventArgs e)
        {
            EnsurePanelAttached();
            ActiveTimeline?.Record(
                "RdpSessionView Loaded",
                $"root={ActualWidth:F0}x{ActualHeight:F0} visible={IsVisible}");
        }

        private void OnFormsHostLoaded(object sender, RoutedEventArgs e)
        {
            EnsurePanelAttached();
            UpdateLayout();
            FormsHost.UpdateLayout();
            _hostPanel.SyncClientBounds();
        }

        private void OnSessionViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            FormsHost?.UpdateLayout();
            _hostPanel.SyncClientBounds();
        }

        internal System.Windows.Forms.Integration.WindowsFormsHost FormsHostElement => FormsHost;

        internal RdpConnectionTimeline? ActiveTimeline { get; set; }

        internal System.Windows.Controls.Border SessionLoadingOverlay => LoadingOverlay;
        internal System.Windows.Controls.Border SessionFailureOverlay => FailureOverlay;
        internal System.Windows.Controls.TextBlock SessionLoadingStatusText => LoadingStatusText;
        internal System.Windows.Controls.TextBlock SessionLiveTimelineText => LiveTimelineText;

        public void EnsurePanelAttached()
        {
            if (_panelAttached || FormsHost == null)
                return;

            FormsHost.Child = _hostPanel;
            _hostPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            _hostPanel.EnsureHandlesCreated();
            _panelAttached = true;
        }

        public void AttachClient(EmbeddedRdpClient client)
        {
            EnsurePanelAttached();
            Visibility = Visibility.Visible;
            UpdateLayout();
            FormsHost.UpdateLayout();
            _hostPanel.AttachClient(client);
        }

        public void DetachClient()
        {
            _hostPanel.DetachClient(leaveClientAlive: true);
        }

        public void SyncClientBounds() => _hostPanel.SyncClientBounds();

        public async Task<RdpHostLayoutSnapshot?> WaitForLayoutReadyAsync(EmbeddedRdpClient? client, int timeoutMs = 10000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            RdpHostLayoutSnapshot? last = null;

            while (DateTime.UtcNow < deadline)
            {
                last = await Dispatcher.InvokeAsync(
                    () => CaptureLayout(client),
                    DispatcherPriority.Render).Task.ConfigureAwait(true);

                if (client != null && last.IsConnectReady)
                    return last;

                if (client == null && IsHostLayoutReady(last))
                    return last;

                await Task.Delay(50).ConfigureAwait(true);
            }

            return last;
        }

        public static bool IsHostLayoutReady(RdpHostLayoutSnapshot snap) =>
            snap.SessionViewVisible
            && snap.HostLoaded
            && snap.SessionViewRootWidth > 0
            && snap.SessionViewRootHeight > 0
            && snap.HostActualWidth > 0
            && snap.HostActualHeight > 0
            && snap.PanelClientWidth > 0
            && snap.PanelClientHeight > 0
            && snap.HostHwnd != nint.Zero
            && snap.PanelHwnd != nint.Zero;

        public RdpHostLayoutSnapshot CaptureLayout(EmbeddedRdpClient? client)
        {
            EnsurePanelAttached();
            UpdateLayout();
            FormsHost?.UpdateLayout();
            _hostPanel.EnsureHandlesCreated();
            _hostPanel.SyncClientBounds();

            if (client != null && !ReferenceEquals(_hostPanel.Client, client))
                _hostPanel.AttachClient(client);

            nint hostHwnd = nint.Zero;
            if (FormsHost != null && FormsHost.IsLoaded
                && PresentationSource.FromVisual(FormsHost) is HwndSource src)
            {
                hostHwnd = src.Handle;
            }

            var pixelW = 0;
            var pixelH = 0;
            if (hostHwnd != nint.Zero && NativeGetClientRect(hostHwnd, out var rc))
            {
                pixelW = Math.Max(0, rc.Right - rc.Left);
                pixelH = Math.Max(0, rc.Bottom - rc.Top);
            }

            if (pixelW <= 0 && FormsHost != null)
            {
                pixelW = (int)Math.Max(RdpHostPanel.MinPanelWidth, FormsHost.ActualWidth);
                pixelH = (int)Math.Max(RdpHostPanel.MinPanelHeight, FormsHost.ActualHeight);
            }

            return _hostPanel.CaptureLayout(
                ActualWidth,
                ActualHeight,
                FormsHost?.ActualWidth ?? 0,
                FormsHost?.ActualHeight ?? 0,
                pixelW,
                pixelH,
                FormsHost?.IsLoaded == true,
                hostHwnd,
                IsVisible);
        }

        public void ShowConnecting(string message)
        {
            HideFailure();
            LoadingStatusText.Text = message;
            LiveTimelineText.Text = string.Empty;
            LoadingOverlay.Visibility = Visibility.Visible;
            FailureOverlay.Visibility = Visibility.Collapsed;
            SessionStatusText.Text = message;
        }

        public void HideConnecting()
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        public void AppendTimeline(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            LiveTimelineText.Text = string.IsNullOrWhiteSpace(LiveTimelineText.Text)
                ? line
                : LiveTimelineText.Text + Environment.NewLine + line;
        }

        public void ShowFailure(RdpConnectionFailureInfo info)
        {
            HideConnecting();
            FailureOverlay.Visibility = Visibility.Visible;
            FailureTitle.Text = info.State switch
            {
                RdpConnectionState.TimedOut => "Connection timed out",
                RdpConnectionState.Cancelled => "Connection cancelled",
                _ => "Connection failed"
            };
            FailureMessage.Text = info.Message;

            var d = info.Diagnostics;
            DiagHost.Text = $"Host: {d.Host ?? "—"}";
            DiagPort.Text = $"Port: {d.Port}";
            DiagTcp.Text = $"TCP {d.Port}: {(d.TcpReachable.HasValue ? (d.TcpReachable.Value ? "open" : "closed") : "—")}";
            DiagActiveX.Text = $"ActiveX: {d.ActiveXVersion ?? "—"}";
            DiagLastEvent.Text = $"Last event: {d.LastEvent}";
            DiagErrorCode.Text = $"Last error: {(d.ErrorCode.HasValue ? d.ErrorCode.Value.ToString() : "—")}";
            DiagHResult.Text = $"COM HRESULT: {(d.ComHResult.HasValue ? $"0x{d.ComHResult.Value:X8}" : "—")}";
            DiagLayout.Text = string.IsNullOrWhiteSpace(d.LiveTimeline) ? "—" : d.LiveTimeline;
            SessionStatusText.Text = info.Message;
        }

        public void HideFailure() => FailureOverlay.Visibility = Visibility.Collapsed;

        public void SetSessionStatus(string message, bool connected)
        {
            SessionStatusText.Text = message;
            if (connected)
                HideConnecting();
        }

        public void SetSmartSizingToggle(bool enabled)
        {
            SmartSizingToggle.IsChecked = enabled;
        }

        private void OnReconnectClick(object sender, RoutedEventArgs e) => ReconnectRequested?.Invoke(this, EventArgs.Empty);
        private void OnDisconnectClick(object sender, RoutedEventArgs e) => DisconnectRequested?.Invoke(this, EventArgs.Empty);
        private void OnSendCtrlAltDelClick(object sender, RoutedEventArgs e) => SendCtrlAltDelRequested?.Invoke(this, EventArgs.Empty);
        private void OnSmartSizingToggleClick(object sender, RoutedEventArgs e) =>
            SmartSizingToggled?.Invoke(this, SmartSizingToggle.IsChecked == true);
        private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e) => CopyDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
        private void OnFailureRetryClick(object sender, RoutedEventArgs e) => FailureRetryRequested?.Invoke(this, EventArgs.Empty);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(nint hWnd, out NativeRect lpRect);

        private static bool NativeGetClientRect(nint hWnd, out NativeRect rc) => GetClientRect(hWnd, out rc);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
