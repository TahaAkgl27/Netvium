using Netvium.Models;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Conservative server role detection from WMI/Agent telemetry only. Never guesses.
    /// </summary>
    internal static class ServerRoleDetector
    {
        public static DetectedServerRole Detect(WmiSnapshot? snapshot)
        {
            if (snapshot == null || !snapshot.Connected)
            {
                return new DetectedServerRole
                {
                    Role = DetectedServerRoleKind.Unknown,
                    DisplayName = "Unknown",
                    Confidence = 0
                };
            }

            var candidates = new List<(DetectedServerRoleKind Role, string DisplayName, double Confidence, string Evidence)>();

            if (IsRunning(snapshot, "NTDS") && (IsRunning(snapshot, "ADWS") || IsRunning(snapshot, "Kdc")))
            {
                candidates.Add((
                    DetectedServerRoleKind.DomainController,
                    "Domain Controller",
                    0.95,
                    "NTDS and ADWS/Kdc services are running"));
            }

            if (IsRunning(snapshot, "DNS"))
            {
                candidates.Add((
                    DetectedServerRoleKind.DnsServer,
                    "DNS Server",
                    0.9,
                    "DNS Server service is running"));
            }

            if (IsRunning(snapshot, "DHCPServer"))
            {
                candidates.Add((
                    DetectedServerRoleKind.DhcpServer,
                    "DHCP Server",
                    0.9,
                    "DHCP Server service is running"));
            }

            if (IsRunning(snapshot, "vmms"))
            {
                candidates.Add((
                    DetectedServerRoleKind.HyperVHost,
                    "Hyper-V Host",
                    0.9,
                    "Hyper-V Virtual Machine Management (vmms) service is running"));
            }

            if (IsRunning(snapshot, "W3SVC"))
            {
                candidates.Add((
                    DetectedServerRoleKind.IISServer,
                    "IIS Server",
                    0.9,
                    "W3SVC (IIS) service is running"));
            }

            if (IsRunning(snapshot, "CertSvc"))
            {
                candidates.Add((
                    DetectedServerRoleKind.CertificateAuthority,
                    "Certificate Authority",
                    0.9,
                    "Certificate Services (CertSvc) service is running"));
            }

            if (IsRunning(snapshot, "MSSQLSERVER")
                || snapshot.Services.Any(s =>
                    s.State == "Running"
                    && s.Name.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((
                    DetectedServerRoleKind.SqlServer,
                    "SQL Server",
                    0.85,
                    "SQL Server service is running"));
            }

            if (IsRunning(snapshot, "UmRdpService") && IsRunning(snapshot, "SessionEnv"))
            {
                candidates.Add((
                    DetectedServerRoleKind.RemoteDesktopHost,
                    "Remote Desktop Host",
                    0.85,
                    "RDS role services UmRdpService and SessionEnv are running"));
            }

            var visibleShares = snapshot.SharedFolders.Count(f => !string.IsNullOrWhiteSpace(f) && !f.EndsWith('$'));
            if (IsRunning(snapshot, "LanmanServer") && visibleShares > 0)
            {
                candidates.Add((
                    DetectedServerRoleKind.FileServer,
                    "File Server",
                    0.8,
                    $"LanmanServer is running with {visibleShares} shared folder(s)"));
            }

            if (candidates.Count > 0)
            {
                var best = candidates.OrderByDescending(c => c.Confidence).First();
                return new DetectedServerRole
                {
                    Role = best.Role,
                    DisplayName = best.DisplayName,
                    Confidence = best.Confidence,
                    Evidence = new List<string> { best.Evidence }
                };
            }

            if (!string.IsNullOrWhiteSpace(snapshot.OSName))
            {
                return new DetectedServerRole
                {
                    Role = DetectedServerRoleKind.GenericWindowsServer,
                    DisplayName = "Generic Windows Server",
                    Confidence = 0.5,
                    Evidence = new List<string> { "Windows OS detected; no specific server role confirmed from telemetry" }
                };
            }

            return new DetectedServerRole
            {
                Role = DetectedServerRoleKind.Unknown,
                DisplayName = "Unknown",
                Confidence = 0
            };
        }

        private static bool IsRunning(WmiSnapshot snapshot, string serviceName) =>
            snapshot.Services.Any(s =>
                s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase)
                && s.State.Equals("Running", StringComparison.OrdinalIgnoreCase));
    }
}
