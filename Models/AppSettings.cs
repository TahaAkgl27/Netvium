namespace Netvium.Models
{
    public sealed class AppSettings
    {
        public string ApplicationName { get; set; } = "Netvium";
        public int SettingsVersion { get; set; }
        public string Language { get; set; } = "en";
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool CloseToTray { get; set; }
        public bool AutoUpdate { get; set; } = true;
        public bool ShowWelcomeOverlayOnStartup { get; set; } = true;

        public string Theme { get; set; } = "dark";
        public string AccentColor { get; set; } = "#3B82F6";
        public bool CompactMode { get; set; }
        public bool AnimationsEnabled { get; set; } = true;
        public int CardRadius { get; set; } = 14;
        public double SidebarWidth { get; set; } = 320;
        public bool ShowTooltips { get; set; } = true;
        public bool ShowStatusAnimations { get; set; } = true;
        public bool EnableBlurEffects { get; set; } = false;

        public int DefaultRefreshIntervalSeconds { get; set; } = 15;
        public bool EnableLiveMonitoring { get; set; } = true;
        public bool MonitorCpu { get; set; } = true;
        public bool MonitorRam { get; set; } = true;
        public bool MonitorDisk { get; set; } = true;
        public bool MonitorNetwork { get; set; } = true;
        public bool MonitorServices { get; set; } = true;
        public bool MonitorEventLogs { get; set; } = true;
        public int MaximumGraphHistory { get; set; } = 120;
        public int MonitoringTimeoutSeconds { get; set; } = 30;

        public bool EnableNotifications { get; set; } = true;
        public bool DesktopNotifications { get; set; } = true;
        public bool CriticalAlerts { get; set; } = true;
        public bool WarningAlerts { get; set; } = true;
        public bool InformationAlerts { get; set; } = true;
        public bool SoundAlerts { get; set; }
        public bool PlaySound { get; set; }
        public bool MuteAll { get; set; }
        public int NotificationDurationMs { get; set; } = 3000;

        public int DefaultConnectionTimeoutSeconds { get; set; } = 120;
        public bool ReconnectAutomatically { get; set; } = true;
        public int MaximumRetryCount { get; set; } = 3;
        public int PingIntervalSeconds { get; set; } = 30;
        public bool UseDefaultCredentials { get; set; }
        public string DefaultUsername { get; set; } = string.Empty;
        public string DefaultPasswordProtected { get; set; } = string.Empty;
        public bool CredentialManagerEnabled { get; set; } = true;
        public bool EncryptedCredentialStorage { get; set; } = true;
        public bool RememberLastConnectedServer { get; set; } = true;
        public string LastConnectedServerKey { get; set; } = string.Empty;

        public bool HardwareAcceleration { get; set; } = true;
        public bool GpuRendering { get; set; } = true;
        public int CacheSizeMb { get; set; } = 256;
        public bool MemoryOptimization { get; set; } = true;
        public bool EnableBackgroundTasks { get; set; } = true;
        public bool LimitRefreshRate { get; set; }
        public int MaxConcurrentConnections { get; set; } = 5;

        public bool EnableEncryption { get; set; } = true;
        public int SessionTimeoutMinutes { get; set; } = 60;
        public bool AutoLogout { get; set; }
        public bool WindowsAuthentication { get; set; }
        public bool RequireAdministratorPrivileges { get; set; }
        public bool AuditLogging { get; set; } = true;
        public bool SecureDeleteLogs { get; set; }

        public bool IsPremiumEnabled { get; set; }

        /// <summary>
        /// Enterprise Developer Edition — when true, all premium features are unlocked.
        /// Set to false before release builds that enforce subscription licensing.
        /// </summary>
        public bool DevelopmentMode { get; set; } = true;

        public bool EnableAiAnalysis { get; set; } = true;
        public string AiProvider { get; set; } = "OpenAI";
        public string AiApiKeyProtected { get; set; } = string.Empty;
        public string AgentMetricsKeyProtected { get; set; } = string.Empty;
        /// <summary>http or https — client-side agent endpoint scheme.</summary>
        public string AgentScheme { get; set; } = "http";
        public int AgentPort { get; set; } = 9841;
        public string AiModel { get; set; } = "gpt-4o-mini";
        public int AiTokenLimit { get; set; } = 400;
        public int AiTimeoutSeconds { get; set; } = 60;
        public string AnalysisDepth { get; set; } = "standard";
        public bool AutomaticRecommendations { get; set; } = true;
        public bool GenerateMaintenanceSuggestions { get; set; } = true;
        public bool GeneratePerformanceSuggestions { get; set; } = true;
        public bool AiCacheEnabled { get; set; } = true;

        public bool EnableAIGuideVision { get; set; }
        public bool EnableAIGuideCaptureDiagnostics { get; set; }
        public string VisionProvider { get; set; } = "OpenAI";
        public string VisionEndpoint { get; set; } = string.Empty;
        public string VisionModel { get; set; } = "gpt-4o-mini";
        public string VisionApiKeyProtected { get; set; } = string.Empty;
        public int VisionTimeoutSeconds { get; set; } = 30;

        public bool EncryptSnapshots { get; set; }
        public bool EnableLogging { get; set; } = true;
        public bool VerboseLogs { get; set; }
        public int MaximumLogSizeMb { get; set; } = 20;

        public static AppSettings CreateDefault() => new()
        {
            DevelopmentMode = true,
            EnableAIGuideVision = true
        };

        public AppSettings Clone() => (AppSettings)MemberwiseClone();
    }
}
