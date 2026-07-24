
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using Netvium.Rdp;

namespace Netvium.Rdp.Clean
{
    public enum RdpDiagnosticsPhase
    {
        BeforeConnect,
        AfterConnect,
        AfterResize,
        Manual,
    }

    /// <summary>
    /// Builds the Phase 3 viewport diagnostics without performing any I/O.
    /// File persistence is delegated to the asynchronous RDP logger.
    /// </summary>
    public static class RdpRenderingDiagnostics
    {
        public static string BuildReport(
            RdpDiagnosticsPhase phase,
            Visual? wpfRoot,
            FrameworkElement? tabView,
            Grid? rdpSurface,
            WindowsFormsHost? formsHost,
            RdpHostPanel? hostPanel,
            RdpSessionHostControl? hostControl,
            RdpSizingPlan? plan = null,
            string? notes = null,
            string? sessionId = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== RDP Rendering Diagnostics [{phase}] {DateTime.UtcNow:O} ===");
            sb.AppendLine($"SessionId: {sessionId ?? hostControl?.SessionId ?? "n/a"}");
            if (!string.IsNullOrWhiteSpace(notes))
                sb.AppendLine($"Notes: {notes}");
            sb.AppendLine();

            sb.AppendLine("[WPF Host]");
            if (tabView != null)
                sb.AppendLine($"  TabView Actual={tabView.ActualWidth:F1}x{tabView.ActualHeight:F1}");
            if (rdpSurface != null)
            {
                var dpi = VisualTreeHelper.GetDpi(rdpSurface);
                var pxW = (int)Math.Ceiling(rdpSurface.ActualWidth * dpi.DpiScaleX);
                var pxH = (int)Math.Ceiling(rdpSurface.ActualHeight * dpi.DpiScaleY);
                sb.AppendLine($"  Viewport DIP={rdpSurface.ActualWidth:F1}x{rdpSurface.ActualHeight:F1}");
                sb.AppendLine($"  Viewport PhysicalPx={pxW}x{pxH} DPI={dpi.PixelsPerInchX:F1}x{dpi.PixelsPerInchY:F1}");
            }
            sb.AppendLine($"  WindowsFormsHost={(formsHost == null ? "not-created" : $"{formsHost.ActualWidth:F1}x{formsHost.ActualHeight:F1}")}");
            sb.AppendLine();

            sb.AppendLine("[DPI]");
            try
            {
                var dpi = RdpDpiHelper.CaptureSystemDpi(wpfRoot ?? tabView);
                sb.AppendLine($"  WPF Scale={dpi.WpfDpiScaleX:F3}x{dpi.WpfDpiScaleY:F3} ({dpi.WpfDpiScalePercent}%)");
                sb.AppendLine($"  Effective DPI={dpi.EffectiveDpiX}x{dpi.EffectiveDpiY} ({dpi.WindowsScalingPercent}%)");
            }
            catch
            {
                sb.AppendLine("  DPI unavailable");
            }
            sb.AppendLine();

            var hostDiag = hostControl?.GetViewportDiagnostics();
            sb.AppendLine("[WinForms/ActiveX]");
            if (hostDiag.HasValue)
            {
                var d = hostDiag.Value;
                sb.AppendLine($"  HostClient={d.HostClientWidth}x{d.HostClientHeight}");
                sb.AppendLine($"  ActiveX={d.ActiveXWidth}x{d.ActiveXHeight} Bounds={d.ActiveXBoundsWidth}x{d.ActiveXBoundsHeight}");
                sb.AppendLine($"  Remote={d.DesktopWidth}x{d.DesktopHeight}");
                sb.AppendLine($"  DisplayMode={d.RenderingMode} SmartSizing={d.SmartSizing}");
                sb.AppendLine($"  DPI Scale={d.DesktopScalePercent}/{d.DeviceScalePercent}");
                sb.AppendLine($"  UpdateSessionDisplaySettings={FormatResult(d.LastUpdateSessionDisplaySucceeded)} Fallback={d.DisplayUpdateFallbackApplied}");
                sb.AppendLine($"  LastResizeUtc={d.LastResizeUtc?.ToString("O") ?? "n/a"}");
            }
            else
            {
                sb.AppendLine("  Host diagnostics unavailable");
            }

            if (hostControl != null)
            {
                var metrics = hostControl.GetActiveXMetrics();
                sb.AppendLine($"  ActiveX Version={hostControl.ActiveControlType} CLSID={hostControl.ActiveControlClsid ?? "n/a"}");
                sb.AppendLine($"  ActiveX ClientSize={metrics.Width}x{metrics.Height}");
            }

            if (plan.HasValue)
            {
                var p = plan.Value;
                sb.AppendLine("[Sizing]");
                sb.AppendLine($"  Profile={p.ProfileLabel}");
                sb.AppendLine($"  RequestedRemote={p.RequestedDesktopWidth}x{p.RequestedDesktopHeight}");
                sb.AppendLine($"  ViewportPixels={p.ViewportPixelWidth}x{p.ViewportPixelHeight}");
            }

            return sb.ToString().TrimEnd();
        }

        public static void LogReport(
            RdpDiagnosticsPhase phase,
            Visual? wpfRoot,
            FrameworkElement? tabView,
            Grid? rdpSurface,
            WindowsFormsHost? formsHost,
            RdpHostPanel? hostPanel,
            RdpSessionHostControl? hostControl,
            RdpSizingPlan? plan = null,
            string? notes = null,
            string? sessionId = null)
        {
            RdpCleanHostLogger.LogBlock(
                $"RDP diagnostics [{phase}]",
                BuildReport(phase, wpfRoot, tabView, rdpSurface, formsHost, hostPanel, hostControl, plan, notes, sessionId)
                    .Split(Environment.NewLine),
                sessionId ?? hostControl?.SessionId);
        }

        private static string FormatResult(bool? value) => value switch
        {
            true => "yes",
            false => "no",
            _ => "n/a",
        };
    }
}
