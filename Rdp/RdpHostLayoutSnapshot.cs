using System.Drawing;
using System.Text;

namespace Netvium.Rdp
{
    public sealed class RdpHostLayoutSnapshot
    {
        public const int MinSessionWidth = 1024;
        public const int MinSessionHeight = 640;

        public const int MinHostWidth = 1024;
        public const int MinHostHeight = 640;

        public double SessionViewRootWidth { get; init; }
        public double SessionViewRootHeight { get; init; }
        public double WorkspaceGridWidth { get; init; }
        public double WorkspaceGridHeight { get; init; }
        public double ContentGridWidth { get; init; }
        public double ContentGridHeight { get; init; }
        public double HostActualWidth { get; init; }
        public double HostActualHeight { get; init; }
        public int HostPixelWidth { get; init; }
        public int HostPixelHeight { get; init; }
        public int PanelClientWidth { get; init; }
        public int PanelClientHeight { get; init; }
        public nint PanelHwnd { get; init; }
        public int ActiveXWidth { get; init; }
        public int ActiveXHeight { get; init; }
        public int ActiveXClientWidth { get; init; }
        public int ActiveXClientHeight { get; init; }
        public Rectangle ActiveXBounds { get; init; }
        public nint HostHwnd { get; init; }
        public nint ActiveXHwnd { get; init; }
        public bool SessionViewVisible { get; init; }
        public bool HostLoaded { get; init; }
        public bool ContentGridVisible { get; init; }

        public bool IsConnectReady =>
            SessionViewVisible
            && HostLoaded
            && ContentGridVisible
            && SessionViewRootWidth > 0
            && SessionViewRootHeight > 0
            && HostActualWidth > 0
            && HostActualHeight > 0
            && PanelClientWidth > 0
            && PanelClientHeight > 0
            && ActiveXWidth > 0
            && ActiveXHeight > 0
            && ActiveXClientWidth > 0
            && ActiveXClientHeight > 0
            && HostHwnd != nint.Zero
            && PanelHwnd != nint.Zero
            && ActiveXHwnd != nint.Zero;

        public string ToLogBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"RDP host root ActualWidth/Height: {SessionViewRootWidth:F0}x{SessionViewRootHeight:F0}");
            if (ContentGridWidth > 0 || ContentGridHeight > 0)
                sb.AppendLine($"Parent workspace/content grid: {ContentGridWidth:F0}x{ContentGridHeight:F0}");
            sb.AppendLine($"WindowsFormsHost actual size: {HostActualWidth:F0}x{HostActualHeight:F0}");
            sb.AppendLine($"WindowsFormsHost pixel size: {HostPixelWidth}x{HostPixelHeight}");
            sb.AppendLine($"WinForms panel ClientSize: {PanelClientWidth}x{PanelClientHeight}");
            sb.AppendLine($"ActiveX Width/Height: {ActiveXWidth}x{ActiveXHeight}");
            sb.AppendLine($"ActiveX ClientSize: {ActiveXClientWidth}x{ActiveXClientHeight}");
            sb.AppendLine($"ActiveX Bounds: {ActiveXBounds.Width}x{ActiveXBounds.Height} @ ({ActiveXBounds.X},{ActiveXBounds.Y})");
            sb.AppendLine($"HWND host=0x{HostHwnd:X} panel=0x{PanelHwnd:X} activeX=0x{ActiveXHwnd:X}");
            sb.AppendLine($"HostLoaded={HostLoaded} ConnectReady={IsConnectReady}");
            return sb.ToString().TrimEnd();
        }
    }
}
