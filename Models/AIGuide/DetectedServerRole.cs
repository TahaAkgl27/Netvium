namespace Netvium.Models.AIGuide
{
    public enum DetectedServerRoleKind
    {
        Unknown = 0,
        DomainController,
        DnsServer,
        DhcpServer,
        HyperVHost,
        IISServer,
        FileServer,
        SqlServer,
        CertificateAuthority,
        RemoteDesktopHost,
        GenericWindowsServer
    }

    /// <summary>
    /// Server role derived from telemetry evidence only. Never inferred from vision.
    /// </summary>
    public sealed class DetectedServerRole
    {
        public DetectedServerRoleKind Role { get; init; } = DetectedServerRoleKind.Unknown;
        public string DisplayName { get; init; } = "Unknown";
        public double Confidence { get; init; }
        public List<string> Evidence { get; init; } = new();
    }
}
