namespace Netvium.Models
{
    public static class AuditActions
    {
        public const string ApplicationStarted = "ApplicationStarted";
        public const string UserActive = "UserActive";
        public const string ServerSelected = "ServerSelected";
        public const string DashboardOpened = "DashboardOpened";
        public const string WidgetAdded = "WidgetAdded";
        public const string WidgetRemoved = "WidgetRemoved";
        public const string WidgetMoved = "WidgetMoved";
        public const string WidgetResized = "WidgetResized";
        public const string LayoutSaved = "LayoutSaved";
        public const string LayoutReset = "LayoutReset";
        public const string WidgetExpanded = "WidgetExpanded";
        public const string WidgetRefreshed = "WidgetRefreshed";
        public const string Connect = "Connect";
        public const string Disconnect = "Disconnect";
        public const string Reconnect = "Reconnect";
        public const string CtrlAltDel = "CtrlAltDel";
        public const string Shutdown = "Shutdown";
        public const string Restart = "Restart";
        public const string Logoff = "Logoff";
        public const string ServiceStart = "ServiceStart";
        public const string ServiceStop = "ServiceStop";
        public const string ServiceRestart = "ServiceRestart";
        public const string ClipboardEnable = "ClipboardEnable";
        public const string ClipboardDisable = "ClipboardDisable";
        public const string QuickConnect = "QuickConnect";
        public const string SettingsChange = "SettingsChange";
        public const string AlertCreated = "AlertCreated";
        public const string AlertUpdated = "AlertUpdated";
        public const string AlertDeleted = "AlertDeleted";
        public const string AlertResolved = "AlertResolved";
        public const string PremiumActivation = "PremiumActivation";
        public const string RiskAnalysis = "RiskAnalysis";
        public const string AiAnalysis = "AiAnalysis";
        public const string Export = "Export";
        public const string Import = "Import";
    }

    public static class AuditCategories
    {
        public const string Application = "Application";
        public const string Dashboard = "Dashboard";
        public const string Widget = "Widget";
        public const string Alert = "Alert";
        public const string Rdp = "RDP";
        public const string RemoteAdmin = "RemoteAdmin";
        public const string Security = "Security";
        public const string Configuration = "Configuration";
        public const string Analysis = "Analysis";
        public const string Data = "Data";
    }

    public sealed class AuditLogEntry
    {
        public string Id { get; init; } = System.Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string Severity { get; init; } = "Information";
        public string Username { get; init; } = string.Empty;
        public string MachineName { get; init; } = string.Empty;
        public string Source { get; init; } = "Netvium";
        public string Message { get; init; } = string.Empty;
        public string? DetailsJson { get; init; }
        public string ApplicationVersion { get; init; } = string.Empty;
        public string? ServerName { get; init; }
        public string? ServerIP { get; init; }
        public string? ServerIp => ServerIP;
        public string? SessionId { get; init; }
        public string Action { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Result { get; init; } = string.Empty;
        public long? DurationMs { get; init; }
        public string? ErrorMessage { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
        public string Level { get; init; } = "Information";
    }
}
