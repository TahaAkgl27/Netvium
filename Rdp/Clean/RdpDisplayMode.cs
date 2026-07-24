namespace Netvium.Rdp.Clean;

/// <summary>Authoritative display mode for the active Clean RDP host.</summary>
public enum RdpDisplayMode
{
    Native = 0,
    Fit = 1,
}

internal static class RdpDisplayModePolicy
{
    public static bool IsSmartSizingEnabled(RdpDisplayMode mode) => mode == RdpDisplayMode.Fit;

    public static string GetLabel(RdpDisplayMode mode) => mode == RdpDisplayMode.Fit ? "Fit" : "Native";
}