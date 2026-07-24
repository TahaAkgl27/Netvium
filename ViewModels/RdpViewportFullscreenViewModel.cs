using System.Windows;
using System.Windows.Controls;

namespace Netvium.ViewModels
{
    /// <summary>
    /// RDP focus mode layout state — hides center-column chrome so the RDP viewport expands upward.
    /// Sidebar, app header and status bar remain visible.
    /// </summary>
    public sealed class RdpViewportFullscreenViewModel
    {
        public bool IsRdpFocusMode { get; set; }

        public int SavedWorkspaceGridRow { get; private set; }
        public int SavedWorkspaceGridRowSpan { get; private set; }
        public Thickness SavedRdpColumnPadding { get; private set; }
        public double SavedTopChromeHeight { get; private set; }

        public void Capture(
            FrameworkElement workspaceGrid,
            Thickness rdpColumnPadding,
            double currentTopChromeHeight)
        {
            SavedWorkspaceGridRow = Grid.GetRow(workspaceGrid);
            SavedWorkspaceGridRowSpan = Grid.GetRowSpan(workspaceGrid);
            SavedRdpColumnPadding = rdpColumnPadding;
            SavedTopChromeHeight = currentTopChromeHeight;
        }

        public void Restore(FrameworkElement workspaceGrid, Border rdpColumnBorder)
        {
            Grid.SetRow(workspaceGrid, SavedWorkspaceGridRow);
            Grid.SetRowSpan(workspaceGrid, SavedWorkspaceGridRowSpan);
            rdpColumnBorder.Padding = SavedRdpColumnPadding;
        }
    }
}
