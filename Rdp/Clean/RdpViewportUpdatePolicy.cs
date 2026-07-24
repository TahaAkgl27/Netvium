namespace Netvium.Rdp.Clean;

internal static class RdpViewportUpdatePolicy
{
    public static bool IsLatest(long requestGeneration, long latestGeneration) =>
        requestGeneration == latestGeneration;

    public static bool IsSamePhysicalRequest(
        int width,
        int height,
        int desktopScalePercent,
        int deviceScalePercent,
        int lastWidth,
        int lastHeight,
        int lastDesktopScalePercent,
        int lastDeviceScalePercent) =>
        width == lastWidth
        && height == lastHeight
        && desktopScalePercent == lastDesktopScalePercent
        && deviceScalePercent == lastDeviceScalePercent;
}