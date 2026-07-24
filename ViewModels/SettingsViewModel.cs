using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Services;
using Netvium.ViewModels.Settings;

namespace Netvium.ViewModels
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly Action<string> _showToast;
        private readonly Func<string, bool> _confirm;
        private readonly Action<AppSettings>? _onSettingsApplied;
        private readonly Func<Task>? _exportConnections;
        private readonly Func<Task>? _importConnections;
        private readonly List<SettingRowViewModel> _allRows = new();
        private AppSettings _working = AppSettings.CreateDefault();
        private AppSettings _saved = AppSettings.CreateDefault();
        private SettingsCategory _selectedCategory = SettingsCategory.General;
        private string _searchText = string.Empty;
        private bool _isDirty;
        private bool _isBusy;
        private System.Threading.CancellationTokenSource? _autoSaveCts;

        public SettingsViewModel(
            Action<string> showToast,
            Func<string, bool> confirm,
            Action<AppSettings>? onSettingsApplied = null,
            Func<Task>? exportConnections = null,
            Func<Task>? importConnections = null)
        {
            _showToast = showToast;
            _confirm = confirm;
            _exportConnections = exportConnections;
            _importConnections = importConnections;
            _onSettingsApplied = onSettingsApplied;

            Categories = new ObservableCollection<SettingsNavItem>
            {
                new() { Category = SettingsCategory.General, Title = "General", IconGlyph = "\uE713" },
                new() { Category = SettingsCategory.Appearance, Title = "Appearance", IconGlyph = "\uE790" },
                new() { Category = SettingsCategory.Monitoring, Title = "Monitoring", IconGlyph = "\uE9D2" },
                new() { Category = SettingsCategory.Notifications, Title = "Notifications", IconGlyph = "\uEA8F" },
                new() { Category = SettingsCategory.Connections, Title = "Connections", IconGlyph = "\uE968" },
                new() { Category = SettingsCategory.Performance, Title = "Performance", IconGlyph = "\uE9F5" },
                new() { Category = SettingsCategory.Security, Title = "Security", IconGlyph = "\uE72E" },
                new() { Category = SettingsCategory.AI, Title = "AI", IconGlyph = "\uE99A" },
                new() { Category = SettingsCategory.License, Title = "License", IconGlyph = "\uE72E" },
                new() { Category = SettingsCategory.Logs, Title = "Logs", IconGlyph = "\uE8F9" },
                new() { Category = SettingsCategory.About, Title = "About", IconGlyph = "\uE946" }
            };

            FilteredRows = new ObservableCollection<SettingRowViewModel>();

            SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => IsDirty && !IsBusy);
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsDirty && !IsBusy);
            RestoreDefaultsCommand = new AsyncRelayCommand(_ => RestoreDefaultsAsync());
            RestoreAppearanceCommand = new RelayCommand(_ => RestoreAppearanceDefaults());

            LoadFromService();
            BuildRows();
            RefreshFilteredRows();
        }

        public ObservableCollection<SettingsNavItem> Categories { get; }
        public ObservableCollection<SettingRowViewModel> FilteredRows { get; }

        public SettingsCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                    RefreshFilteredRows();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RefreshFilteredRows();
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string UnsavedIndicator => SettingsTextCatalog.Common(
            IsBusy ? "Saving..." : IsDirty ? "Unsaved changes" : "All changes saved");

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand RestoreAppearanceCommand { get; }

        private void LoadFromService()
        {
            _working = SettingsService.Instance.Current.Clone();
            _saved = _working.Clone();
        }

        private void MarkDirty()
        {
            IsDirty = true;
            OnPropertyChanged(nameof(UnsavedIndicator));
        }

        private void BuildRows()
        {
            _allRows.Clear();
            var s = _working;

            string ReadSensitive(string value) => s.EnableEncryption ? CredentialProtector.Unprotect(value) : value;
            void Track(SettingRowViewModel row)
            {
                row.ValueChanged += (_, _) =>
                {
                    MarkDirty();
                    ScheduleAutoSave();
                };
                _allRows.Add(row);
            }

            // General
            Track(new TextSettingRowViewModel("appName", "Application Name", "Displayed in the title bar and about screen.", SettingsCategory.General, s.ApplicationName, searchTags: "name title"));
            Track(new ChoiceSettingRowViewModel("language", "Language", "Interface language preference.", SettingsCategory.General, new[] { "English", "Turkish" }, s.Language == "tr" ? "Turkish" : "English", "lang locale"));
            Track(new BoolSettingRowViewModel("startWindows", "Start with Windows", "Launch Netvium when Windows starts.", SettingsCategory.General, s.StartWithWindows, "startup boot"));
            Track(new BoolSettingRowViewModel("minTray", "Minimize to Tray", "Hide to system tray when minimized.", SettingsCategory.General, s.MinimizeToTray, "tray"));
            Track(new BoolSettingRowViewModel("closeTray", "Close to Tray", "Keep running in tray when the window is closed.", SettingsCategory.General, s.CloseToTray, "tray exit"));
            Track(new BoolSettingRowViewModel("autoUpdate", "Auto Update", "Check for new releases automatically when Netvium starts.", SettingsCategory.General, s.AutoUpdate, "update"));
            Track(new BoolSettingRowViewModel("welcomeOnStartup", "Show Startup Overview", "Show the daily server overview when Netvium opens.", SettingsCategory.General, s.ShowWelcomeOverlayOnStartup, "welcome startup daily analysis overview"));
            Track(new ActionSettingRowViewModel("checkUpdates", "Check Updates", "Search for the latest Netvium release.", SettingsCategory.General, "Check Updates", new AsyncRelayCommand(_ => CheckUpdatesAsync())));
            Track(new ActionSettingRowViewModel("exportConfig", "Export Configuration", "Save settings to a JSON file.", SettingsCategory.General, "Export", new AsyncRelayCommand(_ => ExportConfigAsync())));
            Track(new ActionSettingRowViewModel("importConfig", "Import Configuration", "Load settings from a JSON file.", SettingsCategory.General, "Import", new AsyncRelayCommand(_ => ImportConfigAsync())));
            Track(new ActionSettingRowViewModel("resetSettings", "Reset Settings", "Restore all settings to factory defaults.", SettingsCategory.General, "Reset Settings", new AsyncRelayCommand(_ => RestoreDefaultsAsync()), isDestructive: true));

            // Appearance
            Track(new ChoiceSettingRowViewModel("theme", "Theme", "Dark theme (v1.0 production default). Light mode is not available yet.", SettingsCategory.Appearance, new[] { "Dark" }, "Dark", "dark light"));
            Track(new ChoiceSettingRowViewModel("accent", "Accent Color", "Primary accent used across the UI.", SettingsCategory.Appearance, new[] { "#3B82F6", "#7C3AED", "#22C55E", "#F59E0B", "#EF4444" }, s.AccentColor, "color blue"));
            Track(new BoolSettingRowViewModel("compact", "Compact Mode", "Reduce padding in lists and panels.", SettingsCategory.Appearance, s.CompactMode, "density"));
            Track(new BoolSettingRowViewModel("animations", "Animations", "Enable UI transitions and fades.", SettingsCategory.Appearance, s.AnimationsEnabled, "motion"));
            Track(new NumberSettingRowViewModel("cardRadius", "Card Radius", "Corner radius for cards (px).", SettingsCategory.Appearance, s.CardRadius, 6, 24, "radius corners"));
            Track(new SliderSettingRowViewModel("sidebarWidth", "Sidebar Width", "Left sidebar width in pixels.", SettingsCategory.Appearance, s.SidebarWidth, 260, 480, "sidebar panel"));
            Track(new BoolSettingRowViewModel("tooltips", "Show Tooltips", "Display tooltips on hover.", SettingsCategory.Appearance, s.ShowTooltips, "help"));
            Track(new BoolSettingRowViewModel("statusAnim", "Show Status Animations", "Animate health and status indicators.", SettingsCategory.Appearance, s.ShowStatusAnimations, "pulse"));
            Track(new BoolSettingRowViewModel("blur", "Enable Blur Effects", "Drop shadows and glass effects.", SettingsCategory.Appearance, s.EnableBlurEffects, "glass shadow"));
            Track(new ActionSettingRowViewModel("restoreAppearance", "Restore Default Appearance", "Reset appearance options only.", SettingsCategory.Appearance, "Restore Defaults", RestoreAppearanceCommand));

            // Monitoring
            Track(new ChoiceSettingRowViewModel("refreshInterval", "Default Refresh Interval", "Dashboard polling interval.", SettingsCategory.Monitoring, new[] { "5 sec", "10 sec", "15 sec", "30 sec", "60 sec" }, IntervalLabel(s.DefaultRefreshIntervalSeconds), "poll timer"));
            Track(new BoolSettingRowViewModel("liveMonitoring", "Enable Live Monitoring", "Collect metrics from selected servers.", SettingsCategory.Monitoring, s.EnableLiveMonitoring, "wmi"));
            Track(new BoolSettingRowViewModel("monCpu", "Monitor CPU", "Include CPU metrics.", SettingsCategory.Monitoring, s.MonitorCpu, "processor"));
            Track(new BoolSettingRowViewModel("monRam", "Monitor RAM", "Include memory metrics.", SettingsCategory.Monitoring, s.MonitorRam, "memory"));
            Track(new BoolSettingRowViewModel("monDisk", "Monitor Disk", "Include disk usage metrics.", SettingsCategory.Monitoring, s.MonitorDisk, "storage"));
            Track(new BoolSettingRowViewModel("monNet", "Monitor Network", "Include latency and network metrics.", SettingsCategory.Monitoring, s.MonitorNetwork, "ping latency"));
            Track(new BoolSettingRowViewModel("monSvc", "Monitor Services", "Track Windows services state.", SettingsCategory.Monitoring, s.MonitorServices, "services"));
            Track(new BoolSettingRowViewModel("monEvents", "Monitor Event Logs", "Include recent event log entries.", SettingsCategory.Monitoring, s.MonitorEventLogs, "events timeline"));
            Track(new NumberSettingRowViewModel("graphHistory", "Maximum Graph History", "Data points kept on performance charts.", SettingsCategory.Monitoring, s.MaximumGraphHistory, 30, 500, "chart points"));
            Track(new NumberSettingRowViewModel("monTimeout", "Monitoring Timeout", "WMI query timeout in seconds.", SettingsCategory.Monitoring, s.MonitoringTimeoutSeconds, 5, 120, "timeout wmi"));

            // Notifications
            Track(new BoolSettingRowViewModel("notifEnable", "Enable Notifications", "Master switch for in-app notifications.", SettingsCategory.Notifications, s.EnableNotifications, "toast"));
            Track(new BoolSettingRowViewModel("notifDesktop", "Desktop Notifications", "Show Windows toast notifications.", SettingsCategory.Notifications, s.DesktopNotifications, "system"));
            Track(new BoolSettingRowViewModel("notifCritical", "Critical Alerts", "Show critical severity alerts.", SettingsCategory.Notifications, s.CriticalAlerts, "red"));
            Track(new BoolSettingRowViewModel("notifWarning", "Warning Alerts", "Show warning severity alerts.", SettingsCategory.Notifications, s.WarningAlerts, "amber"));
            Track(new BoolSettingRowViewModel("notifInfo", "Information Alerts", "Show informational alerts.", SettingsCategory.Notifications, s.InformationAlerts, "info"));
            Track(new BoolSettingRowViewModel("soundAlerts", "Sound Alerts", "Enable notification sounds.", SettingsCategory.Notifications, s.SoundAlerts, "audio"));
            Track(new BoolSettingRowViewModel("playSound", "Play Sound", "Play sound when alerts fire.", SettingsCategory.Notifications, s.PlaySound, "beep"));
            Track(new BoolSettingRowViewModel("muteAll", "Mute All", "Suppress all notification sounds.", SettingsCategory.Notifications, s.MuteAll, "silent"));
            Track(new NumberSettingRowViewModel("notifDuration", "Notification Duration", "Toast display time in milliseconds.", SettingsCategory.Notifications, s.NotificationDurationMs, 1000, 15000, "timeout ms"));
            Track(new ActionSettingRowViewModel("testNotif", "Test Notification", "Preview the current notification settings.", SettingsCategory.Notifications, "Test Notification", new RelayCommand(_ => TestNotification())));

            // Connections
            Track(new NumberSettingRowViewModel("connTimeout", "Default Connection Timeout", "RDP connection timeout in seconds.", SettingsCategory.Connections, s.DefaultConnectionTimeoutSeconds, 5, 120, "rdp timeout"));
            Track(new BoolSettingRowViewModel("reconnect", "Reconnect Automatically", "Retry failed RDP connections.", SettingsCategory.Connections, s.ReconnectAutomatically, "retry"));
            Track(new NumberSettingRowViewModel("maxRetry", "Maximum Retry Count", "Connection retry attempts.", SettingsCategory.Connections, s.MaximumRetryCount, 0, 10, "retry"));
            Track(new NumberSettingRowViewModel("pingInterval", "Ping Interval", "Server reachability check interval (sec).", SettingsCategory.Connections, s.PingIntervalSeconds, 5, 300, "icmp"));
            Track(new BoolSettingRowViewModel("useDefaultCreds", "Default Credentials", "Pre-fill username/password for new servers.", SettingsCategory.Connections, s.UseDefaultCredentials, "login"));
            Track(new TextSettingRowViewModel("defaultUser", "Default Username", "Username used when default credentials are enabled.", SettingsCategory.Connections, s.DefaultUsername, searchTags: "user login"));
            Track(new TextSettingRowViewModel("defaultPass", "Default Password", "Password stored with DPAPI when encryption is enabled.", SettingsCategory.Connections, ReadSensitive(s.DefaultPasswordProtected), isPassword: true, searchTags: "password"));
            Track(new BoolSettingRowViewModel("credMgr", "Credential Manager", "Use Windows credential manager integration.", SettingsCategory.Connections, s.CredentialManagerEnabled, "vault"));
            Track(new BoolSettingRowViewModel("encCreds", "Encrypted Credential Storage", "Encrypt stored server passwords on disk.", SettingsCategory.Connections, s.EncryptedCredentialStorage, "aes dpapi"));
            Track(new BoolSettingRowViewModel("rememberServer", "Remember Last Connected Server", "Restore last server selection on startup.", SettingsCategory.Connections, s.RememberLastConnectedServer, "session"));
            if (_exportConnections != null)
            {
                Track(new ActionSettingRowViewModel(
                    "exportConnections",
                    "Sunucular\u0131 D\u0131\u015far\u0131 Aktar",
                    "Sunucu, klas\u00f6r ve oturum bilgilerini parola korumal\u0131 XML dosyas\u0131na kaydet.",
                    SettingsCategory.Connections,
                    "D\u0131\u015far\u0131 Aktar",
                    new AsyncRelayCommand(_ => _exportConnections()),
                    searchTags: "server export xml backup connections"));
            }
            if (_importConnections != null)
            {
                Track(new ActionSettingRowViewModel(
                    "importConnections",
                    "Sunucular\u0131 \u0130\u00e7eri Aktar",
                    "Netvium veya mRemoteNG XML dosyas\u0131ndaki RDP sunucular\u0131n\u0131 mevcut listeye ekle.",
                    SettingsCategory.Connections,
                    "\u0130\u00e7eri Aktar",
                    new AsyncRelayCommand(_ => _importConnections()),
                    searchTags: "server import xml restore mremote connections"));
            }

            // Performance
            Track(new BoolSettingRowViewModel("hwAccel", "Hardware Acceleration", "Use GPU compositing for WPF rendering.", SettingsCategory.Performance, s.HardwareAcceleration, "gpu render"));
            Track(new BoolSettingRowViewModel("gpuRender", "GPU Rendering", "Prefer GPU pipeline for visual effects.", SettingsCategory.Performance, s.GpuRendering, "directx"));
            Track(new NumberSettingRowViewModel("cacheSize", "Cache Size", "Local cache limit in megabytes.", SettingsCategory.Performance, s.CacheSizeMb, 64, 2048, "disk cache"));
            Track(new ActionSettingRowViewModel("clearCache", "Clear Cache", "Delete cached WMI snapshots and temp data.", SettingsCategory.Performance, "Clear Cache", new RelayCommand(_ => ClearCache()), isDestructive: true));
            Track(new BoolSettingRowViewModel("memOpt", "Memory Optimization", "Release unused memory after heavy operations.", SettingsCategory.Performance, s.MemoryOptimization, "gc ram"));
            Track(new BoolSettingRowViewModel("bgTasks", "Enable Background Tasks", "Allow background refresh and analysis.", SettingsCategory.Performance, s.EnableBackgroundTasks, "async"));
            Track(new BoolSettingRowViewModel("limitRefresh", "Limit Refresh Rate", "Throttle dashboard refresh to reduce load.", SettingsCategory.Performance, s.LimitRefreshRate, "throttle"));
            Track(new NumberSettingRowViewModel("maxConn", "Maximum Concurrent Connections", "Maximum simultaneous RDP sessions.", SettingsCategory.Performance, s.MaxConcurrentConnections, 1, 20, "sessions tabs"));

            // Security
            Track(new BoolSettingRowViewModel("encryption", "Enable Encryption", "Encrypt sensitive local data at rest.", SettingsCategory.Security, s.EnableEncryption, "aes"));
            Track(new InfoSettingRowViewModel("aesStatus", "AES-256 Status", "Credential protection algorithm in use.", SettingsCategory.Security, s.EnableEncryption ? "AES-256 (DPAPI envelope)" : "Disabled", "crypto"));
            Track(new ActionSettingRowViewModel("certMgr", "Certificate Management", "Open Windows certificate manager.", SettingsCategory.Security, "Open Certificates", new RelayCommand(_ => SettingsApplicator.OpenCertificates())));
            Track(new NumberSettingRowViewModel("sessionTimeout", "Session Timeout", "Inactive session timeout in minutes (0 = off).", SettingsCategory.Security, s.SessionTimeoutMinutes, 0, 480, "idle logout"));
            Track(new BoolSettingRowViewModel("autoLogout", "Auto Logout", "Clear session state after timeout.", SettingsCategory.Security, s.AutoLogout, "idle"));
            Track(new BoolSettingRowViewModel("winAuth", "Windows Authentication", "Prefer current Windows credentials for RDP.", SettingsCategory.Security, s.WindowsAuthentication, "sso"));
            Track(new BoolSettingRowViewModel("requireAdmin", "Require Administrator Privileges", "Warn when not running elevated.", SettingsCategory.Security, s.RequireAdministratorPrivileges, "uac admin"));
            Track(new BoolSettingRowViewModel("auditLog", "Audit Logging", "Write security-relevant actions to the log.", SettingsCategory.Security, s.AuditLogging, "audit"));
            Track(new BoolSettingRowViewModel("secureDelete", "Secure Delete Logs", "Overwrite log files before deletion.", SettingsCategory.Security, s.SecureDeleteLogs, "wipe"));

            // AI
            Track(new BoolSettingRowViewModel("aiEnable", "Enable AI Analysis", "Use AI-powered server insights.", SettingsCategory.AI, s.EnableAiAnalysis, "openai gpt"));
            Track(new TextSettingRowViewModel("aiApiKey", "AI API Key", "OpenAI/Azure key (DPAPI when encryption is on). Or set NETVIUM_OPENAI_KEY env var.", SettingsCategory.AI, ReadSensitive(s.AiApiKeyProtected), isPassword: true, searchTags: "openai key token"));
            Track(new TextSettingRowViewModel("agentKey", "Agent Metrics Key", "Shared key for Netvium.Agent /metrics (DPAPI when encryption is on). Or set NETVIUM_AGENT_KEY env var.", SettingsCategory.Connections, ReadSensitive(s.AgentMetricsKeyProtected), isPassword: true, searchTags: "agent metrics key"));
            Track(new ChoiceSettingRowViewModel("aiProvider", "AI Provider", "Cloud AI provider for analysis requests.", SettingsCategory.AI, new[] { "OpenAI", "Azure OpenAI" }, s.AiProvider, "provider"));
            Track(new ChoiceSettingRowViewModel("aiModel", "AI Model Selection", "Model used for cloud analysis requests.", SettingsCategory.AI, new[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1" }, s.AiModel, "model"));
            Track(new NumberSettingRowViewModel("aiTokens", "Token Limit", "Maximum tokens per AI request.", SettingsCategory.AI, s.AiTokenLimit, 100, 4000, "tokens limit"));
            Track(new ChoiceSettingRowViewModel("aiDepth", "Analysis Depth", "Token budget and detail level.", SettingsCategory.AI, new[] { "quick", "standard", "deep" }, s.AnalysisDepth, "depth"));
            Track(new BoolSettingRowViewModel("aiAuto", "Automatic Recommendations", "Run AI analysis during dashboard refresh.", SettingsCategory.AI, s.AutomaticRecommendations, "auto"));
            Track(new BoolSettingRowViewModel("aiMaint", "Generate Maintenance Suggestions", "Include patch and reboot recommendations.", SettingsCategory.AI, s.GenerateMaintenanceSuggestions, "maintenance"));
            Track(new BoolSettingRowViewModel("aiPerf", "Generate Performance Suggestions", "Include CPU/RAM/disk optimization tips.", SettingsCategory.AI, s.GeneratePerformanceSuggestions, "performance"));
            Track(new BoolSettingRowViewModel("aiCache", "AI Cache", "Cache AI responses per server (60s cooldown).", SettingsCategory.AI, s.AiCacheEnabled, "cache"));
            Track(new ActionSettingRowViewModel("clearAiCache", "Clear AI Cache", "Remove cached AI analysis results.", SettingsCategory.AI, "Clear AI Cache", new RelayCommand(_ => ClearAiCache())));

            Track(new BoolSettingRowViewModel("aiGuideVision", "Enable AI Guide Vision", "Send captured screenshots to the vision model for read-only observation.", SettingsCategory.AI, s.EnableAIGuideVision, "guide vision screen"));
            Track(new BoolSettingRowViewModel("aiGuideCaptureDiag", "AI Guide Capture Diagnostics", "Show capture metadata in AI Guide (developer diagnostic).", SettingsCategory.AI, s.EnableAIGuideCaptureDiagnostics, "capture debug diagnostic"));
            Track(new ChoiceSettingRowViewModel("visionProvider", "Vision Provider", "Cloud provider for AI Guide vision requests.", SettingsCategory.AI, new[] { "OpenAI", "Azure OpenAI" }, string.IsNullOrWhiteSpace(s.VisionProvider) ? s.AiProvider : s.VisionProvider, "vision provider"));
            Track(new TextSettingRowViewModel("visionEndpoint", "Vision Endpoint", "Optional custom endpoint URL (blank = provider default).", SettingsCategory.AI, s.VisionEndpoint, searchTags: "vision endpoint url"));
            Track(new ChoiceSettingRowViewModel("visionModel", "Vision Model", "Vision-capable model for AI Guide.", SettingsCategory.AI, new[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1" }, string.IsNullOrWhiteSpace(s.VisionModel) ? "gpt-4o-mini" : s.VisionModel, "vision model"));
            Track(new TextSettingRowViewModel("visionApiKey", "Vision API Key", "Optional separate key (falls back to AI API key). Or set NETVIUM_VISION_KEY.", SettingsCategory.AI, ReadSensitive(s.VisionApiKeyProtected), isPassword: true, searchTags: "vision key"));
            Track(new NumberSettingRowViewModel("visionTimeout", "Vision Timeout", "Vision API timeout in seconds.", SettingsCategory.AI, s.VisionTimeoutSeconds, 5, 120, "vision timeout"));

            // Logs
            Track(new BoolSettingRowViewModel("logEnable", "Enable Logging", "Write application logs to disk.", SettingsCategory.Logs, s.EnableLogging, "file"));
            Track(new BoolSettingRowViewModel("logVerbose", "Verbose Logs", "Include detailed diagnostic messages.", SettingsCategory.Logs, s.VerboseLogs, "debug trace"));
            Track(new NumberSettingRowViewModel("logSize", "Maximum Log Size", "Rotate log when size exceeds this (MB).", SettingsCategory.Logs, s.MaximumLogSizeMb, 1, 500, "rotate"));
            Track(new ActionSettingRowViewModel("exportLogs", "Export Logs", "Copy current log file to a chosen location.", SettingsCategory.Logs, "Export Logs", new RelayCommand(_ => ExportLogs())));
            Track(new ActionSettingRowViewModel("openLogs", "Open Log Folder", "Open the logs directory in Explorer.", SettingsCategory.Logs, "Open Folder", new RelayCommand(_ => SettingsApplicator.OpenLogFolder())));
            Track(new ActionSettingRowViewModel("deleteLogs", "Delete Logs", "Remove all log files.", SettingsCategory.Logs, "Delete Logs", new RelayCommand(_ => DeleteLogs()), isDestructive: true));

            // About
            var about = SettingsApplicator.GetAboutInfo();
            Track(new InfoSettingRowViewModel("aboutVersion", "Application Version", "Installed Netvium version.", SettingsCategory.About, about.Version, "version"));
            Track(new InfoSettingRowViewModel("aboutBuild", "Build Number", "Internal build identifier.", SettingsCategory.About, about.Build, "build"));
            Track(new InfoSettingRowViewModel("aboutRuntime", "Runtime Version", ".NET runtime information.", SettingsCategory.About, about.Runtime, "dotnet"));
            Track(new InfoSettingRowViewModel("aboutOs", "Operating System", "Host operating system.", SettingsCategory.About, about.Os, "windows"));
            Track(new InfoSettingRowViewModel("aboutMachine", "System Information", "Machine name.", SettingsCategory.About, about.Machine, "hostname"));
            Track(new InfoSettingRowViewModel("aboutLicense", "License", "Software license.", SettingsCategory.About, "Proprietary — Netvium Enterprise", "license"));
            Track(new ActionSettingRowViewModel("aboutWeb", "Website", "Open the Netvium website.", SettingsCategory.About, "Open Website", new RelayCommand(_ => SettingsApplicator.OpenUrl("https://netvium.app"))));
            Track(new ActionSettingRowViewModel("aboutDocs", "Documentation", "Open product documentation.", SettingsCategory.About, "Documentation", new RelayCommand(_ => SettingsApplicator.OpenUrl("https://docs.netvium.app"))));
            Track(new ActionSettingRowViewModel("aboutGitHub", "GitHub", "View source and releases.", SettingsCategory.About, "GitHub", new RelayCommand(_ => SettingsApplicator.OpenUrl("https://github.com/netvium/netvium"))));
            Track(new ActionSettingRowViewModel("aboutUpdates", "Check Updates", "Verify you are on the latest version.", SettingsCategory.About, "Check Updates", new AsyncRelayCommand(_ => CheckUpdatesAsync())));
        }

        private static string IntervalLabel(int seconds) => seconds switch
        {
            5 => "5 sec",
            10 => "10 sec",
            30 => "30 sec",
            60 => "60 sec",
            _ => "15 sec"
        };

        private static int ParseInterval(string label) => label switch
        {
            "5 sec" => 5,
            "10 sec" => 10,
            "30 sec" => 30,
            "60 sec" => 60,
            _ => 15
        };

        private void RefreshFilteredRows()
        {
            var query = (SearchText ?? string.Empty).Trim().ToLowerInvariant();
            var rows = _allRows.Where(r =>
                (string.IsNullOrEmpty(query) || r.SearchBlob.Contains(query)) &&
                (string.IsNullOrEmpty(query) || r.Category == SelectedCategory || query.Length > 0) &&
                (string.IsNullOrEmpty(query) ? r.Category == SelectedCategory : true));

            FilteredRows.Clear();
            foreach (var row in rows)
                FilteredRows.Add(row);
        }

        private T? Find<T>(string key) where T : SettingRowViewModel
            => _allRows.OfType<T>().FirstOrDefault(r => r.Key == key);

        private void ApplyRowsToSettings()
        {
            var s = _working;
            s.ApplicationName = Find<TextSettingRowViewModel>("appName")?.Value?.Trim() ?? "Netvium";
            if (string.IsNullOrWhiteSpace(s.ApplicationName))
                throw new InvalidOperationException("Application name cannot be empty.");

            s.Language = Find<ChoiceSettingRowViewModel>("language")?.CanonicalValue == "Turkish" ? "tr" : "en";
            s.StartWithWindows = Find<BoolSettingRowViewModel>("startWindows")?.Value ?? false;
            s.MinimizeToTray = Find<BoolSettingRowViewModel>("minTray")?.Value ?? false;
            s.CloseToTray = Find<BoolSettingRowViewModel>("closeTray")?.Value ?? false;
            s.AutoUpdate = Find<BoolSettingRowViewModel>("autoUpdate")?.Value ?? true;
            s.ShowWelcomeOverlayOnStartup = Find<BoolSettingRowViewModel>("welcomeOnStartup")?.Value ?? true;

            s.Theme = Find<ChoiceSettingRowViewModel>("theme")?.CanonicalValue == "System" ? "system" : "dark";
            s.AccentColor = Find<ChoiceSettingRowViewModel>("accent")?.CanonicalValue ?? "#3B82F6";
            s.CompactMode = Find<BoolSettingRowViewModel>("compact")?.Value ?? false;
            s.AnimationsEnabled = Find<BoolSettingRowViewModel>("animations")?.Value ?? true;
            s.CardRadius = Find<NumberSettingRowViewModel>("cardRadius")?.Value ?? 14;
            s.SidebarWidth = Find<SliderSettingRowViewModel>("sidebarWidth")?.Value ?? 320;
            s.ShowTooltips = Find<BoolSettingRowViewModel>("tooltips")?.Value ?? true;
            s.ShowStatusAnimations = Find<BoolSettingRowViewModel>("statusAnim")?.Value ?? true;
            s.EnableBlurEffects = Find<BoolSettingRowViewModel>("blur")?.Value ?? true;

            s.DefaultRefreshIntervalSeconds = ParseInterval(Find<ChoiceSettingRowViewModel>("refreshInterval")?.CanonicalValue ?? "15 sec");
            s.EnableLiveMonitoring = Find<BoolSettingRowViewModel>("liveMonitoring")?.Value ?? true;
            s.MonitorCpu = Find<BoolSettingRowViewModel>("monCpu")?.Value ?? true;
            s.MonitorRam = Find<BoolSettingRowViewModel>("monRam")?.Value ?? true;
            s.MonitorDisk = Find<BoolSettingRowViewModel>("monDisk")?.Value ?? true;
            s.MonitorNetwork = Find<BoolSettingRowViewModel>("monNet")?.Value ?? true;
            s.MonitorServices = Find<BoolSettingRowViewModel>("monSvc")?.Value ?? true;
            s.MonitorEventLogs = Find<BoolSettingRowViewModel>("monEvents")?.Value ?? true;
            s.MaximumGraphHistory = Find<NumberSettingRowViewModel>("graphHistory")?.Value ?? 120;
            s.MonitoringTimeoutSeconds = Find<NumberSettingRowViewModel>("monTimeout")?.Value ?? 30;

            s.EnableNotifications = Find<BoolSettingRowViewModel>("notifEnable")?.Value ?? true;
            s.DesktopNotifications = Find<BoolSettingRowViewModel>("notifDesktop")?.Value ?? true;
            s.CriticalAlerts = Find<BoolSettingRowViewModel>("notifCritical")?.Value ?? true;
            s.WarningAlerts = Find<BoolSettingRowViewModel>("notifWarning")?.Value ?? true;
            s.InformationAlerts = Find<BoolSettingRowViewModel>("notifInfo")?.Value ?? true;
            s.SoundAlerts = Find<BoolSettingRowViewModel>("soundAlerts")?.Value ?? false;
            s.PlaySound = Find<BoolSettingRowViewModel>("playSound")?.Value ?? false;
            s.MuteAll = Find<BoolSettingRowViewModel>("muteAll")?.Value ?? false;
            s.NotificationDurationMs = Find<NumberSettingRowViewModel>("notifDuration")?.Value ?? 3000;

            s.DefaultConnectionTimeoutSeconds = Find<NumberSettingRowViewModel>("connTimeout")?.Value ?? 30;
            s.ReconnectAutomatically = Find<BoolSettingRowViewModel>("reconnect")?.Value ?? true;
            s.MaximumRetryCount = Find<NumberSettingRowViewModel>("maxRetry")?.Value ?? 3;
            s.PingIntervalSeconds = Find<NumberSettingRowViewModel>("pingInterval")?.Value ?? 30;
            s.UseDefaultCredentials = Find<BoolSettingRowViewModel>("useDefaultCreds")?.Value ?? false;
            s.DefaultUsername = Find<TextSettingRowViewModel>("defaultUser")?.Value ?? string.Empty;
            s.EnableEncryption = Find<BoolSettingRowViewModel>("encryption")?.Value ?? true;
            var pass = Find<TextSettingRowViewModel>("defaultPass")?.Value ?? string.Empty;
            s.DefaultPasswordProtected = s.EnableEncryption ? CredentialProtector.Protect(pass) : pass;
            s.CredentialManagerEnabled = Find<BoolSettingRowViewModel>("credMgr")?.Value ?? true;
            s.EncryptedCredentialStorage = Find<BoolSettingRowViewModel>("encCreds")?.Value ?? true;
            s.RememberLastConnectedServer = Find<BoolSettingRowViewModel>("rememberServer")?.Value ?? true;

            s.HardwareAcceleration = Find<BoolSettingRowViewModel>("hwAccel")?.Value ?? true;
            s.GpuRendering = Find<BoolSettingRowViewModel>("gpuRender")?.Value ?? true;
            s.CacheSizeMb = Find<NumberSettingRowViewModel>("cacheSize")?.Value ?? 256;
            s.MemoryOptimization = Find<BoolSettingRowViewModel>("memOpt")?.Value ?? true;
            s.EnableBackgroundTasks = Find<BoolSettingRowViewModel>("bgTasks")?.Value ?? true;
            s.LimitRefreshRate = Find<BoolSettingRowViewModel>("limitRefresh")?.Value ?? false;
            s.MaxConcurrentConnections = Find<NumberSettingRowViewModel>("maxConn")?.Value ?? 5;

            s.SessionTimeoutMinutes = Find<NumberSettingRowViewModel>("sessionTimeout")?.Value ?? 60;
            s.AutoLogout = Find<BoolSettingRowViewModel>("autoLogout")?.Value ?? false;
            s.WindowsAuthentication = Find<BoolSettingRowViewModel>("winAuth")?.Value ?? false;
            s.RequireAdministratorPrivileges = Find<BoolSettingRowViewModel>("requireAdmin")?.Value ?? false;
            s.AuditLogging = Find<BoolSettingRowViewModel>("auditLog")?.Value ?? true;
            s.SecureDeleteLogs = Find<BoolSettingRowViewModel>("secureDelete")?.Value ?? false;

            s.EnableAiAnalysis = Find<BoolSettingRowViewModel>("aiEnable")?.Value ?? true;
            var aiKey = Find<TextSettingRowViewModel>("aiApiKey")?.Value ?? string.Empty;
            s.AiApiKeyProtected = SettingsApplicator.ProtectSensitiveValue(aiKey, s.EnableEncryption);
            var agentKey = Find<TextSettingRowViewModel>("agentKey")?.Value ?? string.Empty;
            s.AgentMetricsKeyProtected = SettingsApplicator.ProtectSensitiveValue(agentKey, s.EnableEncryption);
            s.AiProvider = Find<ChoiceSettingRowViewModel>("aiProvider")?.CanonicalValue ?? "OpenAI";
            s.AiModel = Find<ChoiceSettingRowViewModel>("aiModel")?.CanonicalValue ?? "gpt-4o-mini";
            s.AiTokenLimit = Find<NumberSettingRowViewModel>("aiTokens")?.Value ?? 400;
            s.AnalysisDepth = Find<ChoiceSettingRowViewModel>("aiDepth")?.CanonicalValue ?? "standard";
            s.AutomaticRecommendations = Find<BoolSettingRowViewModel>("aiAuto")?.Value ?? true;
            s.GenerateMaintenanceSuggestions = Find<BoolSettingRowViewModel>("aiMaint")?.Value ?? true;
            s.GeneratePerformanceSuggestions = Find<BoolSettingRowViewModel>("aiPerf")?.Value ?? true;
            s.AiCacheEnabled = Find<BoolSettingRowViewModel>("aiCache")?.Value ?? true;

            s.EnableAIGuideVision = Find<BoolSettingRowViewModel>("aiGuideVision")?.Value ?? false;
            s.EnableAIGuideCaptureDiagnostics = Find<BoolSettingRowViewModel>("aiGuideCaptureDiag")?.Value ?? false;
            s.VisionProvider = Find<ChoiceSettingRowViewModel>("visionProvider")?.CanonicalValue ?? s.AiProvider;
            s.VisionEndpoint = Find<TextSettingRowViewModel>("visionEndpoint")?.Value ?? string.Empty;
            s.VisionModel = Find<ChoiceSettingRowViewModel>("visionModel")?.CanonicalValue ?? "gpt-4o-mini";
            var visionKey = Find<TextSettingRowViewModel>("visionApiKey")?.Value ?? string.Empty;
            s.VisionApiKeyProtected = SettingsApplicator.ProtectSensitiveValue(visionKey, s.EnableEncryption);
            s.VisionTimeoutSeconds = Find<NumberSettingRowViewModel>("visionTimeout")?.Value ?? 30;

            s.EnableLogging = Find<BoolSettingRowViewModel>("logEnable")?.Value ?? true;
            s.VerboseLogs = Find<BoolSettingRowViewModel>("logVerbose")?.Value ?? false;
            s.MaximumLogSizeMb = Find<NumberSettingRowViewModel>("logSize")?.Value ?? 50;
        }

        private void ScheduleAutoSave()
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts = new System.Threading.CancellationTokenSource();
            var token = _autoSaveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(450, token);
                    if (token.IsCancellationRequested) return;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await SaveAsync(silent: true));
                }
                catch (OperationCanceledException) { }
            });
        }

        private async Task SaveAsync(bool silent = false)
        {
            using var audit = AuditOperationScope.BeginConfiguration(AuditActions.SettingsChange);
            try
            {
                IsBusy = true;
                OnPropertyChanged(nameof(UnsavedIndicator));
                ApplyRowsToSettings();
                var languageChanged = !string.Equals(_saved.Language, _working.Language, StringComparison.OrdinalIgnoreCase);
                var premiumActivated = !_saved.IsPremiumEnabled && _working.IsPremiumEnabled;
                await SettingsService.Instance.SaveAsync(_working);
                _saved = _working.Clone();
                _onSettingsApplied?.Invoke(_saved);
                audit.CompleteSuccess();
                if (premiumActivated)
                    AuditHelper.Configuration(AuditActions.PremiumActivation, AuditResults.Success);
                IsDirty = false;
                OnPropertyChanged(nameof(UnsavedIndicator));
                if (!silent)
                {
                    _showToast(languageChanged
                        ? (_working.Language == "tr"
                            ? "Dil değişikliği için uygulamayı yeniden başlatın."
                            : "Restart the application to apply the language change.")
                        : "Settings saved successfully.");
                    AppLogger.Audit("Settings saved.");
                }
            }
            catch (Exception ex)
            {
                audit.CompleteFailed(ex.Message, ex);
                var canContinue = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.AsyncCommand,
                    "Settings.Save",
                    notifyUser: true,
                    userMessage: "Ayarlar kaydedilemedi. Girdi\u011finizi kontrol edip yeniden deneyin.");

                if (!canContinue)
                    throw;
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(UnsavedIndicator));
            }
        }

        private void Cancel()
        {
            _working = _saved.Clone();
            RebuildRowsFromWorking();
            IsDirty = false;
            OnPropertyChanged(nameof(UnsavedIndicator));
        }

        private async Task RestoreDefaultsAsync()
        {
            if (!_confirm("Reset all settings to factory defaults? This cannot be undone."))
                return;

            IsBusy = true;
            try
            {
                _working = SettingsService.Instance.Reset();
                RebuildRowsFromWorking();
                await SettingsService.Instance.SaveAsync(_working);
                _saved = _working.Clone();
                _onSettingsApplied?.Invoke(_working);
                IsDirty = false;
                OnPropertyChanged(nameof(UnsavedIndicator));
                _showToast("Settings restored to defaults.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RestoreAppearanceDefaults()
        {
            var defaults = AppSettings.CreateDefault();
            _working.Theme = defaults.Theme;
            _working.AccentColor = defaults.AccentColor;
            _working.CompactMode = defaults.CompactMode;
            _working.AnimationsEnabled = defaults.AnimationsEnabled;
            _working.CardRadius = defaults.CardRadius;
            _working.SidebarWidth = defaults.SidebarWidth;
            _working.ShowTooltips = defaults.ShowTooltips;
            _working.ShowStatusAnimations = defaults.ShowStatusAnimations;
            _working.EnableBlurEffects = defaults.EnableBlurEffects;
            RebuildRowsFromWorking();
            MarkDirty();
            _showToast("Appearance defaults restored.");
        }

        private void RebuildRowsFromWorking()
        {
            _allRows.Clear();
            BuildRows();
            RefreshFilteredRows();
        }

        private async Task ExportConfigAsync()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "netvium-settings.json"
            };
            if (dlg.ShowDialog() != true) return;
            ApplyRowsToSettings();
            await SettingsService.Instance.SaveAsync(_working);
            await SettingsService.Instance.ExportAsync(dlg.FileName);
            _showToast("Configuration exported.");
        }

        private async Task ImportConfigAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            var merge = _confirm("Merge with existing settings? Choose No to replace all settings.");
            await SettingsService.Instance.ImportAsync(dlg.FileName, merge);
            LoadFromService();
            RebuildRowsFromWorking();
            _onSettingsApplied?.Invoke(SettingsService.Instance.Current);
            IsDirty = false;
            _showToast("Configuration imported.");
        }

        private async Task CheckUpdatesAsync()
        {
            IsBusy = true;
            try
            {
                var (available, message) = await UpdateService.CheckForUpdatesAsync();
                System.Windows.MessageBox.Show(message, available ? "Update Available" : "Check Updates",
                    MessageBoxButton.OK, available ? MessageBoxImage.Information : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var canContinue = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.AsyncCommand,
                    "Settings.CheckUpdates",
                    notifyUser: true,
                    userMessage: "G\u00fcncellemeler kontrol edilemedi. L\u00fctfen internet ba\u011flant\u0131n\u0131z\u0131 kontrol edip yeniden deneyin.");

                if (!canContinue)
                    throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void TestNotification()
        {
            ApplyRowsToSettings();
            if (!NotificationService.ShouldShow(NotificationService.AlertLevel.Info, _working))
            {
                System.Windows.MessageBox.Show(
                    UiLocalizationService.T(
                        "Enable notifications and information alerts before running the test.",
                        "Test i\u00e7in bildirimleri ve bilgi bildirimlerini etkinle\u015ftirin."),
                    UiLocalizationService.T("Notifications disabled", "Bildirimler kapal\u0131"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            NotificationService.Test(_working, _showToast);
        }

        private void ClearCache()
        {
            if (!_confirm("Clear all cached data?")) return;
            ApplyRowsToSettings();
            SettingsApplicator.ClearCache(_working);
            _showToast("Cache cleared.");
        }

        private void ClearAiCache()
        {
            SettingsApplicator.ClearAiCache();
            _showToast("AI cache cleared.");
        }

        private void ExportLogs()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Log files (*.log)|*.log",
                FileName = $"netvium-{DateTime.Now:yyyyMMdd}.log"
            };
            if (dlg.ShowDialog() != true) return;
            SettingsApplicator.ExportLogs(dlg.FileName);
            _showToast("Logs exported.");
        }

        private void DeleteLogs()
        {
            if (!_confirm("Delete all log files?")) return;
            ApplyRowsToSettings();
            SettingsApplicator.DeleteLogs(_working.SecureDeleteLogs, _working);
            _showToast("Logs deleted.");
        }

        public async Task<bool> TryCloseAsync()
        {
            if (!IsDirty)
                return true;

            var result = System.Windows.MessageBox.Show(
                "You have unsaved changes. Save before closing?",
                "Settings",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return false;
            if (result == MessageBoxResult.Yes)
            {
                await SaveAsync();
                return !IsDirty;
            }

            return true;
        }
    }
}
