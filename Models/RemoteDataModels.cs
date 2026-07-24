using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Netvium.Models
{
    public sealed record SessionRow(string User, string State, string Idle, string Logon, string Risk = "Normal", int? Id = null, string ClientName = "-");
    public sealed record ProcessRow(string Name, int Id, double? CpuPct, double? RamMb, string User = "System");
    public sealed record BitLockerVolumeRow(string Volume, string ProtectionStatus, string EncryptionPercent, string ConversionStatus, string RecoveryStatus);
    public sealed record OpenPortRow(string Protocol, int Port, int Pid, string Process, string Risk);

    public sealed record ServiceRow(
        string Name,
        string Display,
        string State,
        bool IsCritical = false,
        string StartMode = "Manual",
        string Dependencies = "",
        string LastStopHint = "")
    {
        [JsonIgnore]
        public System.Windows.Media.Brush StateUIBrush
        {
            get
            {
                if (State == "Running") return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // Emerald 500
                if (State == "Stopped") return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 63, 94)); // Rose 500
                if (State == "Paused") return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // Amber 500
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)); // Slate 500
            }
        }
    }

    public sealed record TimelineEvent(
        DateTime Timestamp,
        string Type, // "Service", "Logon", "System", "Resource"
        string Level, // "Error", "Warning", "Info"
        string Message,
        string Icon,
        string IpAddress = "-");

    public record ServerIntelligence(
        string ServerName,
        string DetectedRole,
        int Confidence,
        List<DetectedComponent> Components,
        List<IntelligenceInsight> Insights,
        List<IntelligenceRecommendation> Recommendations,
        List<SmartInsight>? SmartInsights = null);

    public record DetectedComponent(
        string Name,
        string Type, // "Service", "Port", "Folder", "Role"
        string Status, // "Active", "Open", "Detected"
        string Icon);

    public record IntelligenceInsight(
        string Title,
        string Description,
        InsightSeverity Severity,
        string Icon);

    public record IntelligenceRecommendation(
        string Action,
        string Description,
        string Priority); // "High", "Medium", "Low"


    public enum InsightSeverity
    {
        Info,
        Warning,
        Critical,
        Tip
    }

    public class SmartInsight
    {
        public string Text { get; set; }
        public string? Reason { get; set; }
        public string? Action { get; set; }
        public InsightSeverity Severity { get; set; }
        public string Icon { get; set; }
        public bool IsAi { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Section { get; set; }
        public bool IsSectionHeader { get; set; }
        public string? Priority { get; set; }
        public int SortKey { get; set; }

        public int SortOrder => SortKey > 0 ? SortKey : Severity switch
        {
            InsightSeverity.Critical => 0,
            InsightSeverity.Warning => 1,
            InsightSeverity.Info => 2,
            InsightSeverity.Tip => 3, // Tip is used for PREDICTIVE
            _ => 99
        };

        public SmartInsight(string text, InsightSeverity severity, string icon, bool isAi = false, string? reason = null, string? action = null)
        {
            Text = text;
            Severity = severity;
            Icon = icon;
            IsAi = isAi;
            Reason = reason;
            Action = action;
            Timestamp = DateTime.Now;
        }
    }

    public class WmiSnapshot
    {
        public string? OSName { get; set; }
        public string? CPUModel { get; set; }
        public int? CPUCoreCount { get; set; }
        public double? TotalRamGb { get; set; }
        public string? DomainName { get; set; }
        public List<string> IPAddresses { get; set; } = new();
        public List<string> DnsServers { get; set; } = new();
        public string? DefaultGateway { get; set; }
        public string? MacAddress { get; set; }
        
        public double? DiskUsedPct { get; set; }
        public double DiskFreeGb { get; set; }
        public double DiskTotalGb { get; set; }
        public int? CpuPct { get; set; }
        public double? RamUsedPct { get; set; }
        public double RamFreeGb { get; set; }
        public TimeSpan? Uptime { get; set; }
        public int? ActiveUsers { get; set; }
        public bool Connected { get; set; }
        public string? Error { get; set; }
        public string Status { get; set; } = "unknown";
        public string? Compatibility { get; set; }
        public string? OSVersion { get; set; }
        public string? OSBuildNumber { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? MachineType { get; set; }
        public string? GpuName { get; set; }
        public double? GpuMemoryGb { get; set; }
        public string? WindowsUpdateServiceStatus { get; set; }
        public string? WindowsUpdateLastCheck { get; set; }
        public string? WindowsUpdateLastInstall { get; set; }
        public string? WindowsUpdateAutomaticUpdates { get; set; }
        public bool? WindowsUpdateRebootRequired { get; set; }
        public int? WindowsUpdatePendingCount { get; set; }
        public bool? FirewallDomainEnabled { get; set; }
        public bool? FirewallPrivateEnabled { get; set; }
        public bool? FirewallPublicEnabled { get; set; }
        public string? FirewallServiceStatus { get; set; }
        public List<BitLockerVolumeRow> BitLockerVolumes { get; set; } = new();
        public bool? DefenderRealTimeProtectionEnabled { get; set; }
        public string? DefenderSignatureVersion { get; set; }
        public string? DefenderEngineVersion { get; set; }
        public string? DefenderLastScan { get; set; }
        public int? DefenderThreatCount { get; set; }
        public string? DefenderHealthState { get; set; }
        public List<OpenPortRow> OpenPorts { get; set; } = new();
        public string? NetworkAdapter { get; set; }
        public double? NetworkInBps { get; set; }
        public double? NetworkOutBps { get; set; }
        public ulong? NetworkBytesReceived { get; set; }
        public ulong? NetworkBytesSent { get; set; }
        public bool NetworkCountersAvailable { get; set; }
        public string? LastUser { get; set; }
        public string? LastLogon { get; set; }
        public string? LastSession { get; set; }
        public List<int> ListenPorts { get; set; } = new();
        public List<string> SharedFolders { get; set; } = new();
        public List<ServiceRow> Services { get; set; } = new();
        public List<ProcessRow> TopProcesses { get; set; } = new();
        public List<SessionRow> Sessions { get; set; } = new();
        public List<TimelineEvent> Timeline { get; set; } = new();
        public List<DetectedComponent> SystemStatusComponents { get; set; } = new();
        public DateTime CollectedAt { get; set; } = DateTime.Now;
        public int? PingMs { get; set; }
        public string Ip { get; set; } = "";
        public string? Username { get; set; }
        public string DataSource { get; set; } = SnapshotDataSources.Wmi;
    }
}
