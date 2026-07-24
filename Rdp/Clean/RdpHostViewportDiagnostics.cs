namespace Netvium.Rdp.Clean
{
    public readonly struct RdpHostViewportDiagnostics
    {
        public int HostClientWidth { get; init; }
        public int HostClientHeight { get; init; }
        public int ActiveXWidth { get; init; }
        public int ActiveXHeight { get; init; }
        public int ActiveXBoundsWidth { get; init; }
        public int ActiveXBoundsHeight { get; init; }
        public int DesktopWidth { get; init; }
        public int DesktopHeight { get; init; }
        public bool SmartSizing { get; init; }
        public RdpDisplayMode DisplayMode { get; init; }
        public bool DisplayUpdateFallbackApplied { get; init; }
        public int DesktopScalePercent { get; init; }
        public int DeviceScalePercent { get; init; }
        public DateTime? LastResizeUtc { get; init; }
        public bool? LastUpdateSessionDisplaySucceeded { get; init; }
        public string RenderingMode { get; init; }
    }
}
