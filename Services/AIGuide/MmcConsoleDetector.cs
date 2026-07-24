using System.Text.RegularExpressions;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class MmcConsoleDetector
    {
        private static readonly (MmcConsoleKind Kind, string[] Patterns)[] Catalog =
        [
            (MmcConsoleKind.EventViewer, ["event viewer", "olay görüntüleyici", "olay goruntuleyici"]),
            (MmcConsoleKind.ActiveDirectoryUsersAndComputers,
                ["active directory users and computers", "ad users and computers", "etkin dizin kullanıcıları", "etkin dizin kullanicilari"]),
            (MmcConsoleKind.DnsManager, ["dns manager", "dns yönetimi", "dns yonetimi", "dns snap-in"]),
            (MmcConsoleKind.Dhcp, ["dhcp", "dynamic host configuration"]),
            (MmcConsoleKind.GroupPolicyManagement,
                ["group policy management", "grup ilkesi yönetimi", "grup ilkesi yonetimi", "gpmc"]),
            (MmcConsoleKind.ComputerManagement, ["computer management", "bilgisayar yönetimi", "bilgisayar yonetimi"]),
            (MmcConsoleKind.Services, ["services (local)", "services.msc", "hizmetler (yerel)", "hizmetler"]),
            (MmcConsoleKind.IisManager, ["internet information services", "iis manager", "iis yöneticisi"]),
            (MmcConsoleKind.HyperV, ["hyper-v manager", "hyper-v yöneticisi", "hyper-v yoneticisi"]),
            (MmcConsoleKind.FailoverCluster, ["failover cluster manager", "yük devretme kümesi", "yuk devretme kumesi"]),
            (MmcConsoleKind.TaskScheduler, ["task scheduler", "görev zamanlayıcı", "gorev zamanlayici", "scheduled tasks"]),
            (MmcConsoleKind.RegistryEditor, ["registry editor", "kayıt defteri", "kayit defteri", "regedit"]),
            (MmcConsoleKind.ServerManager, ["server manager", "sunucu yöneticisi", "sunucu yoneticisi"]),
            (MmcConsoleKind.FileExplorer, ["file explorer", "dosya gezgini", "windows explorer", "explorer"]),
            (MmcConsoleKind.EndpointSecurity, ["check point", "checkpoint", "endpoint security", "windows defender", "microsoft defender", "sentinelone", "crowdstrike"]),
            (MmcConsoleKind.NetworkSettings, ["network connections", "ağ bağlantıları", "network and sharing", "ncpa.cpl", "adapter settings"])
        ];

        public static (MmcConsoleKind Console, string SnapInName, string ConsoleType) Detect(string combinedText)
        {
            var haystack = Normalize(combinedText);
            if (string.IsNullOrWhiteSpace(haystack))
                return (MmcConsoleKind.Unknown, string.Empty, string.Empty);

            foreach (var (kind, patterns) in Catalog)
            {
                foreach (var pattern in patterns)
                {
                    if (haystack.Contains(pattern, StringComparison.Ordinal))
                        return (kind, SnapInDisplayName(kind), "MMC");
                }
            }

            if (haystack.Contains("microsoft management console", StringComparison.Ordinal)
                || haystack.Contains("mmc", StringComparison.Ordinal))
            {
                return (MmcConsoleKind.GenericMmc, "Microsoft Management Console", "MMC");
            }

            if (haystack.Contains("powershell", StringComparison.Ordinal)
                || haystack.Contains("windows terminal", StringComparison.Ordinal)
                || haystack.Contains("cmd.exe", StringComparison.Ordinal))
            {
                return (MmcConsoleKind.Unknown, string.Empty, "Console");
            }

            if (haystack.Contains("remote desktop", StringComparison.Ordinal)
                || haystack.Contains("rdp", StringComparison.Ordinal))
            {
                return (MmcConsoleKind.Unknown, string.Empty, "RDP");
            }

            return (MmcConsoleKind.Unknown, string.Empty, string.Empty);
        }

        public static string SnapInDisplayName(MmcConsoleKind kind) => kind switch
        {
            MmcConsoleKind.EventViewer => "Event Viewer",
            MmcConsoleKind.ActiveDirectoryUsersAndComputers => "Active Directory Users and Computers",
            MmcConsoleKind.DnsManager => "DNS Manager",
            MmcConsoleKind.Dhcp => "DHCP",
            MmcConsoleKind.GroupPolicyManagement => "Group Policy Management",
            MmcConsoleKind.ComputerManagement => "Computer Management",
            MmcConsoleKind.Services => "Services",
            MmcConsoleKind.IisManager => "IIS Manager",
            MmcConsoleKind.HyperV => "Hyper-V Manager",
            MmcConsoleKind.FailoverCluster => "Failover Cluster Manager",
            MmcConsoleKind.TaskScheduler => "Task Scheduler",
            MmcConsoleKind.RegistryEditor => "Registry Editor",
            MmcConsoleKind.ServerManager => "Server Manager",
            MmcConsoleKind.FileExplorer => "File Explorer",
            MmcConsoleKind.EndpointSecurity => "Endpoint Security",
            MmcConsoleKind.NetworkSettings => "Network Settings",
            MmcConsoleKind.GenericMmc => "Microsoft Management Console",
            _ => string.Empty
        };

        private static string Normalize(string text) =>
            Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
    }
}
