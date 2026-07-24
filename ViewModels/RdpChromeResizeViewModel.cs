using System;

namespace Netvium.ViewModels
{
    /// <summary>
    /// Tracks top chrome height while the user drags the RDP resize handle.
    /// </summary>
    public sealed class RdpChromeResizeViewModel
    {
        public const double CollapseThreshold = 40;
        public const double MinRdpWorkspaceHeight = 200;

        public bool IsMeasured { get; set; }

        public double DefaultDashboardHeight { get; set; }
        public double DefaultActiveHeaderHeight { get; set; }
        public double DefaultToolbarHeight { get; set; }
        public double DefaultTopChromeHeight { get; set; }
        public double DefaultBottomChromeHeight { get; set; }

        public double CurrentTopChromeHeight { get; set; }
        public double SavedTopChromeHeight { get; set; }

        public double OpacityRatio =>
            DefaultTopChromeHeight <= 0
                ? 1
                : Math.Clamp(CurrentTopChromeHeight / DefaultTopChromeHeight, 0, 1);

        public bool IsFullyCollapsed => CurrentTopChromeHeight < CollapseThreshold;

        public bool IsAtDefault =>
            IsMeasured && Math.Abs(CurrentTopChromeHeight - DefaultTopChromeHeight) < 1;
    }
}
