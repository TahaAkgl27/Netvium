using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Netvium.Models;
using Netvium.Models.AIGuide;
using Netvium.Rdp;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Resolves capture geometry on the UI thread. Must never be called from background threads.
    /// </summary>
    internal static class CaptureTargetResolver
    {
        public static ResolvedCaptureTarget Resolve(
            Func<bool> hasRdpViewport,
            Func<bool> isAnyConnecting,
            Func<(bool IsConnected, bool IsReady, RdpState State)> getRdpReadiness,
            Func<nint> getRdpHostHandle,
            FrameworkElement? rdpHostContainer,
            FrameworkElement? rdpMainContainer,
            FrameworkElement? selectedServerPanel,
            FrameworkElement? rdpColumnBorder,
            FrameworkElement? rdpHost,
            Window clipWindow,
            ServerInfo? server,
            string? serverKey)
        {
            AIGuideAnalysisLogger.CaptureTargetResolving();

            var readiness = getRdpReadiness();
            var isConnecting = isAnyConnecting();
            var rdpStateLabel = readiness.State.ToString();

            if (hasRdpViewport()
                && rdpHostContainer != null
                && rdpHost != null
                && rdpHost.Visibility == Visibility.Visible)
            {
                var bounds = CaptureBoundsHelper.GetPhysicalScreenRect(rdpHostContainer, clipWindow);
                var parentBounds = rdpMainContainer != null
                    ? CaptureBoundsHelper.GetPhysicalScreenRect(rdpMainContainer, clipWindow)
                    : null;
                var clipBounds = CaptureBoundsHelper.GetWindowPhysicalRect(clipWindow);
                var dpi = CaptureBoundsHelper.GetDpiScale(rdpHostContainer);
                nint appHwnd = 0;
                try
                {
                    appHwnd = new WindowInteropHelper(clipWindow).Handle;
                }
                catch { /* ignore */ }

                CapturePhysicalRect? visibleScreen = null;
                if (clipBounds != null)
                    visibleScreen = CaptureBoundsHelper.GetVisibleScreenBoundsForRect(clipBounds.Value);

                return new ResolvedCaptureTarget
                {
                    Source = ScreenCaptureSource.RdpSession,
                    TargetName = rdpHostContainer.GetType().Name,
                    BoundsInScreen = bounds,
                    ParentBoundsInScreen = parentBounds,
                    ClipWindowBounds = clipBounds,
                    VisibleScreenBounds = visibleScreen,
                    DpiScale = dpi,
                    IsVisible = rdpHostContainer.IsVisible,
                    IsConnected = readiness.IsConnected,
                    IsReady = readiness.IsReady && !isConnecting,
                    IsConnecting = isConnecting,
                    RdpState = rdpStateLabel,
                    RdpHostHandle = getRdpHostHandle(),
                    AppWindowHandle = appHwnd,
                    ServerKey = serverKey,
                    ServerName = server?.Name
                };
            }

            if (selectedServerPanel != null && selectedServerPanel.Visibility == Visibility.Visible)
            {
                return new ResolvedCaptureTarget
                {
                    Source = ScreenCaptureSource.LocalDashboard,
                    TargetName = selectedServerPanel.GetType().Name,
                    DpiScale = CaptureBoundsHelper.GetDpiScale(selectedServerPanel),
                    IsVisible = true,
                    IsReady = true,
                    ServerKey = serverKey,
                    ServerName = server?.Name,
                    WpfCaptureRoot = selectedServerPanel
                };
            }

            if (rdpColumnBorder != null)
            {
                return new ResolvedCaptureTarget
                {
                    Source = ScreenCaptureSource.LocalWindow,
                    TargetName = rdpColumnBorder.GetType().Name,
                    DpiScale = CaptureBoundsHelper.GetDpiScale(rdpColumnBorder),
                    IsVisible = true,
                    IsReady = true,
                    ServerKey = serverKey,
                    ServerName = server?.Name,
                    WpfCaptureRoot = rdpColumnBorder
                };
            }

            return new ResolvedCaptureTarget
            {
                Source = ScreenCaptureSource.Unknown,
                ServerKey = serverKey,
                ServerName = server?.Name
            };
        }

        public static nint SafeGetWinFormsHandle(Control? control)
        {
            if (control == null)
                return 0;

            if (control.InvokeRequired)
            {
                return (nint)control.Invoke(new Func<nint>(() =>
                    control.IsHandleCreated ? control.Handle : 0));
            }

            return control.IsHandleCreated ? control.Handle : 0;
        }

        public static ScreenCaptureOptions ToCaptureOptions(
            ResolvedCaptureTarget snapshot,
            System.Windows.FrameworkElement fallbackRoot) =>
            new()
            {
                CaptureRoot = snapshot.WpfCaptureRoot ?? fallbackRoot,
                Source = snapshot.Source,
                MonitorIndex = 0,
                ServerKey = snapshot.ServerKey,
                ServerName = snapshot.ServerName,
                ClipToWindow = null,
                RdpHostHandle = snapshot.RdpHostHandle,
                AppWindowHandle = snapshot.AppWindowHandle,
                IsRdpSessionConnected = () => snapshot.IsConnected,
                ParentCaptureRoot = null,
                ResolvedBounds = snapshot.BoundsInScreen,
                ParentBounds = snapshot.ParentBoundsInScreen,
                ClipWindowBounds = snapshot.ClipWindowBounds,
                VisibleScreenBounds = snapshot.VisibleScreenBounds,
                DpiScale = snapshot.DpiScale,
                TargetElementName = snapshot.TargetName,
                RdpStateLabel = snapshot.RdpState
            };
    }
}
