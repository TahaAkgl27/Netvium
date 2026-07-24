using System.Drawing;
using System.Windows.Forms;

namespace Netvium.Rdp
{
    /// <summary>WinForms surface used by the active Clean RDP host.</summary>
    public sealed class RdpHostPanel : Panel
    {
        public const int StartupMinWidth = 200;
        public const int StartupMinHeight = 150;
        public const int MinPanelWidth = StartupMinWidth;
        public const int MinPanelHeight = StartupMinHeight;

        public RdpHostPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.Black;
            Visible = true;
            Resize += (_, _) => SyncAttachedControlBounds();
            SizeChanged += (_, _) => SyncAttachedControlBounds();
        }

        public void EnsureHandlesCreated()
        {
            if (!IsHandleCreated)
                CreateControl();
            foreach (Control control in Controls)
                if (!control.IsHandleCreated)
                    control.CreateControl();
        }

        public void AttachControl(Control control)
        {
            ArgumentNullException.ThrowIfNull(control);
            Controls.Clear();
            control.Dock = DockStyle.Fill;
            control.Visible = true;
            control.Margin = Padding.Empty;
            Controls.Add(control);
            EnsureHandlesCreated();
            SyncAttachedControlBounds();
        }

        public void SyncFillLayout()
        {
            Dock = DockStyle.Fill;
            SyncAttachedControlBounds();
            PerformLayout();
        }

        [Obsolete("Use SyncFillLayout")]
        public void ApplyFillSize(int width, int height) => SyncFillLayout();

        public void SyncAttachedControlBounds()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;
            var rect = ClientRectangle;
            foreach (Control control in Controls)
            {
                control.Dock = DockStyle.Fill;
                control.Bounds = rect;
                control.Width = rect.Width;
                control.Height = rect.Height;
                control.PerformLayout();
                TryUpdateControlBounds(control);
            }
        }

        public void SyncClientBounds() => SyncAttachedControlBounds();

        public bool IsLayoutReady()
        {
            if (!IsHandleCreated || Handle == nint.Zero
                || ClientSize.Width < StartupMinWidth || ClientSize.Height < StartupMinHeight
                || Controls.Count == 0)
                return false;
            var attached = Controls[0];
            return attached.IsHandleCreated && attached.Handle != nint.Zero
                && attached.ClientSize.Width >= StartupMinWidth
                && attached.ClientSize.Height >= StartupMinHeight;
        }

        public RdpHostLayoutSnapshot CaptureLayout(
            double sessionRootWidth, double sessionRootHeight,
            double hostActualWidth, double hostActualHeight,
            int hostPixelWidth, int hostPixelHeight,
            bool hostLoaded, nint hostHwnd, bool sessionViewVisible)
        {
            EnsureHandlesCreated();
            SyncAttachedControlBounds();
            var bounds = Rectangle.Empty;
            var clientW = 0;
            var clientH = 0;
            var axW = 0;
            var axH = 0;
            nint activeHwnd = nint.Zero;
            if (Controls.Count > 0)
            {
                var attached = Controls[0];
                if (attached.IsHandleCreated)
                    activeHwnd = attached.Handle;
                bounds = attached.Bounds;
                clientW = attached.ClientSize.Width;
                clientH = attached.ClientSize.Height;
                axW = attached.Width;
                axH = attached.Height;
            }
            return new RdpHostLayoutSnapshot
            {
                SessionViewRootWidth = sessionRootWidth,
                SessionViewRootHeight = sessionRootHeight,
                PanelClientWidth = ClientSize.Width,
                PanelClientHeight = ClientSize.Height,
                PanelHwnd = IsHandleCreated ? Handle : nint.Zero,
                HostActualWidth = hostActualWidth,
                HostActualHeight = hostActualHeight,
                HostPixelWidth = hostPixelWidth,
                HostPixelHeight = hostPixelHeight,
                ActiveXWidth = axW,
                ActiveXHeight = axH,
                ActiveXClientWidth = clientW,
                ActiveXClientHeight = clientH,
                ActiveXBounds = bounds,
                HostHwnd = hostHwnd,
                ActiveXHwnd = activeHwnd,
                HostLoaded = hostLoaded,
                ContentGridVisible = true,
                SessionViewVisible = sessionViewVisible,
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private static void TryUpdateControlBounds(Control control)
        {
            try
            {
                control.GetType().GetMethod("UpdateBounds",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(control, null);
            }
            catch { }
        }
    }
}