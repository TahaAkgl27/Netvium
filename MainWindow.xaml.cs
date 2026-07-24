using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Text;
using System.Text.Json;
using System.IO;
using Netvium.Models;
using Netvium.ViewModels;
using System.Windows.Media.Animation;
using Netvium.Native;
using Netvium.Rdp;
using Netvium.Rdp.Clean;
using Netvium.Views;
using Netvium.Views.Licensing;
using Netvium.Services;
using Netvium.Services.Account;
using Netvium.Services.AIGuide;
using Netvium.Helpers;
using Netvium.Models.AIGuide;
using Netvium.Helpers.Logging;

namespace Netvium
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IAccountService? _accountService;
        private bool _accountLogoutInProgress;
        private readonly ShutdownPersistenceCoordinator _shutdownPersistence;
        private bool _shutdownCloseApproved;
        private bool _shutdownCloseInProgress;
        private bool _forceApplicationShutdown;
        private int _applicationShutdownRequested;
        private int _applicationShutdownIssued;
        private readonly RdpOptions _rdpOptions = new RdpOptions();
        private readonly ObservableCollection<ServerInfo> _servers = new();
        private readonly ObservableCollection<ConnectionTreeNode> _connectionTree = new();
        private string _sidebarSearchQuery = string.Empty;
        private string _globalSearchQuery = string.Empty;
        private int _insightsTickInFlight;
        private readonly List<string> _explicitFolders = new();
        private static readonly HttpClient _agentHttp = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        // --- Multi-session tab model ---
        private class RdpTabSession
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public ServerInfo Server { get; set; } = null!;
            public RdpCleanSession? CleanSession { get; set; }
            public Border? TabElement { get; set; }
            public string? CmdKeyTarget { get; set; }
            public bool UiActive { get; set; }
            public DateTime EmbedStartUtc { get; set; }
            public AuditOperationScope? ConnectAudit { get; set; }
            public bool IsQuickConnect { get; set; }
            public RdpSessionReadinessGate ReadinessGate { get; } = new();
            public CancellationTokenSource? ReadinessCts;
            public CancellationTokenSource? ConnectionCts;
            public RdpFillMode FillMode { get; set; } = RdpFillMode.Fit;
            public Views.RdpSessionTabView? TabView { get; set; }
            public TabContentType ContentType { get; set; } = TabContentType.RdpSession;
            public RdpSessionViewModel? ContentViewModel { get; set; }
        }

        private readonly RdpSessionReadinessGate _legacyReadinessGate = new();
        private CancellationTokenSource? _legacyReadinessCts;
        private bool _rdpHostInitialized;
        private bool _aiGuidePausedForRdpConnection;
        private readonly Stopwatch _connectUiWatch = new();

        private AuditOperationScope? _pendingConnectAudit;

        private readonly List<RdpTabSession> _rdpSessions = new();
        private RdpTabSession? _activeSession;

        private bool IsRdpActive() => HasAnyActiveSession();

        private bool HasAnyActiveSession() =>
            _rdpSessions.Any(s => s.CleanSession != null && s.CleanSession.IsConnected);

        private System.Windows.Forms.Control? _rdpUiAnchor;
        private DispatcherTimer? _connectionTimer;
        private ServerInfo? _pendingServer;
        private bool _suspendRdpSizing;
        private DateTime _lastRdpResizeUtc;
        private const int RdpResizeThrottleMs = 300;
        private DispatcherTimer? _rdpSizingDebounce;
        private ConnectionTreeNode? _selectedNode;
        private ServerInfo? _activeDashboardServer;
        private bool _syncingTreeSelection;
        private bool _syncingConfigUi;
        private DispatcherTimer? _insightsTimer;
        private CancellationTokenSource? _insightsCts;
        private string? _insightsKey;
        private int _sidebarScanInFlight;
        private readonly UiUpdateThrottle _insightsUiThrottle = new(TimeSpan.FromMilliseconds(1000));
        private readonly UiUpdateThrottle _chartUpdateThrottle = new(TimeSpan.FromSeconds(1.5));
        private readonly Dictionary<string, List<SmartInsight>> _cachedAiInsightsByServer = new(StringComparer.OrdinalIgnoreCase);
        private string? _lastIntelligenceFingerprint;
        private string? _lastIntelligenceServerKey;
        private string? _lastServicesFingerprint;
        private string? _lastTimelineFingerprint;
        private int _lastChartCpuCount;
        private int _lastChartRamCount;
        private int _lastChartCpuTail = int.MinValue;
        private double _lastChartRamTail = double.MinValue;
        private DateTime _dashboardLoadStartedUtc = DateTime.MinValue;
        private CancellationTokenSource? _deferredDeepCts;
        private const int DashboardDeepPhaseDelaySeconds = 5;
        private readonly ServerRefreshCoordinator _refreshCoordinator = new();
        private readonly IAlertEngine _alertEngine = new AlertEngine();
        private AIGuideService? _aiGuideService;
        private readonly ContextAwarenessEngine _contextAwarenessEngine = new();
        private bool IsAiGuideEntitled => FeatureEntitlementService.Instance.IsAiGuideEnabled;

        private AIGuideService GetAiGuideService()
        {
            if (!IsAiGuideEntitled)
                throw new InvalidOperationException("AI Guide requires Premium+.");
            return _aiGuideService ??= new AIGuideService();
        }

        private void ShowPremiumPlusRequiredPrompt()
        {
            if (PremiumPlusRequiredDialog.Show(this))
                OpenPlansWindow();
        }

        private void StopAiGuideRuntime()
        {
            CancelAiGuideGuidanceInFlight();
            try { _aiGuideCaptureCts?.Cancel(); } catch { /* ignore */ }
            try { _aiGuideCaptureCts?.Dispose(); } catch { /* ignore */ }
            _aiGuideCaptureCts = null;
            _aiGuideService?.CancelInFlightAnalysis();
            _aiGuideService?.ClearSession();
            _aiGuideService = null;
            _aiChatMessages.Clear();
        }

        private void EnforceAiGuideLicenseState()
        {
            if (IsAiGuideEntitled)
            {
                if (AiGuideLicenseLockPanel != null)
                    AiGuideLicenseLockPanel.Visibility = Visibility.Collapsed;
                if (AiGuideScrollViewer != null)
                    AiGuideScrollViewer.Visibility = Visibility.Visible;
                if (AiGuideInputRow != null)
                    AiGuideInputRow.Visibility = Visibility.Visible;
                return;
            }

            StopAiGuideRuntime();

            if (_isAiPanelOpen)
            {
                if (AiPanelBorder != null)
                    AiPanelBorder.Visibility = Visibility.Visible;
                if (AiGuideLicenseLockPanel != null)
                    AiGuideLicenseLockPanel.Visibility = Visibility.Visible;
                if (AiGuideScrollViewer != null)
                    AiGuideScrollViewer.Visibility = Visibility.Collapsed;
                if (AiGuideInputRow != null)
                    AiGuideInputRow.Visibility = Visibility.Collapsed;
            }
        }

        private CancellationTokenSource? _aiGuideCaptureCts;
        private CancellationTokenSource? _aiGuideGuidanceCts;
        private AIGuideFailureInfo? _lastAiGuideFailure;
        private AIGuideCaptureDiagnostics? _lastCaptureDiagnostics;
        private Stopwatch? _aiGuideAnalysisStopwatch;
        private int _aiGuideCaptureRunId;
        private string _aiGuidePipelineCheckpoint = "Start";
        private ScreenContext? _aiGuideLastScreen;
        private VisionAnalysisResult? _aiGuideLastVisionResult;

        private RdpConnectionFailureInfo? _lastRdpFailureInfo;
        private ServerInfo? _lastRdpFailureServer;

        private const string RdpWaitingForScreenMessage = "Waiting for RDP screen...";
        private LiveRefreshIndicatorController? _liveRefreshIndicator;
        private DashboardRefreshLifecycle? _refreshLifecycle;
        private int _refreshGeneration;
        private readonly LiveMetricChartSession _liveChartSession = new();
        private string? _liveChartServerKey;
        private bool _lastDashboardRdpOpen;
        private readonly DashboardStateOwner _dashboardState = new();
        private readonly NetworkThroughputTracker _networkTracker = new();
        private readonly LiveMetricSampler _liveMetricSampler = new();
        private bool _chartRedrawScheduled;
        private int _lastDrawnSampleCount;
        private AgentHealthInfo? _currentAgentHealth;
        private PerfChartTimeRange _perfChartRange = PerfChartTimeRange.OneMinute;
        private DispatcherTimer? _statusBarTimer;

        private enum DashboardUpdateTier
        {
            /// <summary>0-1 sn: ping/status veya önbellek placeholder.</summary>
            Placeholder,
            /// <summary>1-5 sn: CPU/RAM/Disk/servisler.</summary>
            Basic,
            /// <summary>5+ sn: event log, intelligence, health score, chart.</summary>
            Deep
        }

        private static readonly Dictionary<HealthBadge, SolidColorBrush> BadgeBrushCache = new();
        private readonly FastObservableCollection<SessionRow> _sessionRows = new();
        private readonly FastObservableCollection<ServiceRow> _serviceRows = new();
        private readonly FastObservableCollection<ProcessRow> _processRows = new();
        private readonly FastObservableCollection<OpenPortRow> _openPortRows = new();
        private readonly FastObservableCollection<TimelineEvent> _timelineEvents = new();
        private readonly FastObservableCollection<string> _ipAddresses = new();
        private readonly FastObservableCollection<SmartInsight> _smartInsights = new();
        private WmiSnapshot? _lastSnapshot => _dashboardState.CurrentSnapshot;
        private LiveDashboardMetrics? _currentLiveMetrics => _dashboardState.CurrentLiveMetrics;
        private SnapshotSectionTimestamps _sectionTimestamps => _dashboardState.SectionTimestamps;
        private ServerInfo? _selectedServer;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<WmiSnapshot>> _serverHistory = new();

        private bool IsLiveTelemetryAuthoritative(string? localKey) =>
            _dashboardState.IsLiveTelemetryAuthoritative(localKey);

        private bool IsLiveMetricsActive(string? localKey) =>
            _dashboardState.IsLiveMetricsActive(localKey);

        private sealed class DashPanelSlot
        {
            public System.Windows.Controls.Panel Parent { get; init; } = null!;
            public int Row { get; init; }
            public int Column { get; init; }
            public int RowSpan { get; init; } = 1;
            public int ColumnSpan { get; init; } = 1;
            public Thickness Margin { get; init; }
            public double Height { get; init; }
            public double MinHeight { get; init; }
            public double MaxHeight { get; init; }
        }

        private Border? _dashExpandedPanel;
        private DashPanelSlot? _dashExpandedSlot;
        private string? _dashExpandedKey;
        private Visibility _dashAddPanelVisibilityBeforeExpand = Visibility.Collapsed;
        private DispatcherTimer? _sidebarStatusTimer;
        private TrayService? _trayService;
        private DispatcherTimer? _sessionIdleTimer;
        private DateTime _lastUserActivityUtc = DateTime.UtcNow;
        private DateTime _lastInsightsRefreshUtc = DateTime.MinValue;
        private CancellationTokenSource _backgroundScanCts = new CancellationTokenSource();
        private volatile bool _rdpActive = false;
        private readonly ConnectionProfile[] _profiles =
        {
            // 1. NLA STRICT: Best for Server 2022 (NLA Required)
            new("NLA (Strict)", useMinimal: false, enableCredSsp: true, authenticationLevel: 2, negotiateSecurityLayer: true, promptForCredentials: false),
            // 2. NLA STANDARD: Fallback (NLA Recommended)
            new("NLA (Standard)", useMinimal: false, enableCredSsp: true, authenticationLevel: 1, negotiateSecurityLayer: true, promptForCredentials: false),
            // 3. COMPATIBILITY: Force negotiate
            new("Compatibility", useMinimal: true, enableCredSsp: false, authenticationLevel: 0, negotiateSecurityLayer: true, promptForCredentials: false),
            // 4. MANUAL: Prompt for credentials (mRemoteNG fallback)
            new("Manual Login", useMinimal: false, enableCredSsp: true, authenticationLevel: 0, negotiateSecurityLayer: true, promptForCredentials: true),
            // 5. LEGACY: Last resort
            new("Legacy RDP", useMinimal: true, enableCredSsp: false, authenticationLevel: 0, negotiateSecurityLayer: false, promptForCredentials: false)
        };

        // ADAPTIVE PERFORMANCE FIELDS
        private DateTime _lastAiAnalysisTime = DateTime.MinValue;
        private DateTime? _previousAiAnalysisUtc;
        private TimeSpan? _lastAiAnalysisDuration;
        private AiSmartAnalysisReport? _lastAiSmartReport;
        private ServerAnalysisContext? _lastAnalysisContext;
        private readonly Dictionary<string, DateTime> _lastRefreshTime = new Dictionary<string, DateTime>();
        
        // Timeline Pagination Fields
        private List<TimelineEvent> _currentTimelinePageItems = new List<TimelineEvent>();
        
        // AI Intelligence Models
        private ServerIntelligence _currentIntelligence = new(
            string.Empty,
            string.Empty,
            0,
            new List<DetectedComponent>(),
            new List<IntelligenceInsight>(),
            new List<IntelligenceRecommendation>(),
            new List<SmartInsight>());
        private int _currentTimelinePage = 1;
        private const int TimelinePageSize = 10;

        private readonly WelcomeOverlayViewModel _welcomeOverlayVm = new();
        private readonly RdpSessionToolbarViewModel _rdpToolbarVm = new();
        private readonly HashSet<string> _favoriteServerKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<ConnectionTreeNode> _favoriteNodes = new();
        private readonly List<ServerInfo> _ephemeralServers = new();
        private bool _connectAsQuickConnect;
        private readonly ObservableCollection<AIGuideMessage> _aiChatMessages = new();
        private readonly DailyServerAnalysisService _dailyAnalysisService = new();
        private bool _welcomeDismissedThisSession;

        private bool IsPremiumUser => FeatureEntitlementService.Instance.IsPremiumEnabled;

        private System.Windows.Media.Animation.Storyboard? CloneStoryboard(string resourceKey)
        {
            return FindResource(resourceKey) is System.Windows.Media.Animation.Storyboard source
                ? source.Clone()
                : null;
        }

        // Startup Welcome Screen Logic
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetupWelcomeScreen();
            _trayService = new TrayService(this);
            ApplyAppSettings(SettingsService.Instance.Current);
            UpdateLicensingUi();
            EnforceAiGuideLicenseState();
            AuditLogService.Log(
                AuditActions.ApplicationStarted,
                AuditCategories.Application,
                AuditResults.Success,
                LogLevels.Information,
                message: "Application started.");
            AuditLogService.Log(
                AuditActions.UserActive,
                AuditCategories.Application,
                AuditResults.Success,
                LogLevels.Information,
                message: $"Active Windows user: {WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName}");

            if (_welcomeOverlayVm.IsWelcomeOverlayVisible)
            {
                if (SettingsRuntime.Current.AnimationsEnabled)
                {
                    var fadeIn = CloneStoryboard("FadeInWelcome");
                    if (fadeIn != null)
                    {
                        WelcomeOverlay.RenderTransform = new ScaleTransform(0.96, 0.96);
                        WelcomeOverlay.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                        WelcomeOverlay.BeginStoryboard(fadeIn);
                    }
                }
            }

            Dispatcher.BeginInvoke(MeasureDefaultChromeHeights, DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(PrewarmRdpHostBackground, DispatcherPriority.Background);
            _ = CheckForUpdatesOnStartupAsync();
            ApplyAiGuideLanguageUi(refreshContent: false);
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                var (available, message) = await UpdateService.CheckForUpdatesAsync(respectAutoUpdateSetting: true);
                if (available)
                    ShowToast(message, "\uE895");
            }
            catch (Exception ex)
            {
                _ = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.FireAndForget,
                    "Startup.UpdateCheck",
                    notifyUser: false);
            }
        }

        private void ApplyStatusAnimations(bool enabled)
        {
            FrameworkElement[] indicators =
            {
                NotificationStatusDot,
                DashAgentStatusDot,
                StatusAiDot,
                AiGuideStatusDot
            };

            foreach (var indicator in indicators)
            {
                indicator.BeginAnimation(UIElement.OpacityProperty, null);
                indicator.Opacity = 1;
                if (!enabled)
                    continue;

                indicator.BeginAnimation(
                    UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1,
                        To = 0.45,
                        Duration = TimeSpan.FromSeconds(1),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                    });
            }
        }

        private void ApplyMonitoringVisibility(AppSettings settings)
        {
            static void Show(FrameworkElement element, bool visible) =>
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            Show(PerfCpuKpiCard, settings.MonitorCpu);
            Show(PerfRamKpiCard, settings.MonitorRam);
            Show(PerfDiskKpiCard, settings.MonitorDisk);
            Show(PerfNetworkKpiCard, settings.MonitorNetwork);

            Show(LiveCpuCard, settings.MonitorCpu);
            Show(LiveRamCard, settings.MonitorRam);
            Show(LiveDiskCard, settings.MonitorDisk);
            Show(LiveNetworkCard, settings.MonitorNetwork);

            Show(DashWidgetCpuPanel, settings.MonitorCpu);
            Show(DashWidgetRamPanel, settings.MonitorRam);
            Show(DashWidgetDiskPanel, settings.MonitorDisk);
            Show(DashWidgetNetworkPanel, settings.MonitorNetwork);
            Show(DashWidgetLatencyPanel, settings.MonitorNetwork);

            if (!settings.MonitorEventLogs && TimelineItemsControl != null)
                TimelineItemsControl.Visibility = Visibility.Collapsed;
        }

        private void ApplyAiGuideLanguageUi(bool refreshContent)
        {
            try
            {
                if (AiGuideSectionCurrentScreenText != null)
                    AiGuideSectionCurrentScreenText.Text = AIGuideLanguage.CurrentScreen;
                if (AiGuideSectionThingsNoticedText != null)
                    AiGuideSectionThingsNoticedText.Text = AIGuideLanguage.ThingsNoticed;
                if (AiGuideSectionWorkflowText != null)
                    AiGuideSectionWorkflowText.Text = AIGuideLanguage.CurrentWorkflow;
                if (AiGuideSectionQuestionsText != null)
                    AiGuideSectionQuestionsText.Text = AIGuideLanguage.SuggestedQuestions;
                if (AiGuideSectionLearnText != null)
                    AiGuideSectionLearnText.Text = AIGuideLanguage.LearnAboutScreen;
                if (AiGuideSectionWarningsText != null)
                    AiGuideSectionWarningsText.Text = AIGuideLanguage.Warnings;
                if (AiGuideSectionEvidenceHeaderText != null)
                    AiGuideSectionEvidenceHeaderText.Text = AIGuideLanguage.Evidence;
                if (AiGuideWorkflowHistoryHeaderText != null)
                    AiGuideWorkflowHistoryHeaderText.Text = AIGuideLanguage.WorkflowHistory;
                if (AiGuideFailureTitleText != null)
                    AiGuideFailureTitleText.Text = AIGuideLanguage.Failed;
                if (AiGuideCaptureDiagnosticsTitleText != null)
                    AiGuideCaptureDiagnosticsTitleText.Text = AIGuideLanguage.Diagnostics;
                if (AiGuideTechnicalSummaryTitleText != null)
                    AiGuideTechnicalSummaryTitleText.Text = AIGuideLanguage.TechnicalSummary;
                if (AiGuideAwarenessTitleText != null)
                    AiGuideAwarenessTitleText.Text = AIGuideLanguage.ContextAwareness;
                if (AiGuidePanelReminderText != null)
                    AiGuidePanelReminderText.Text = AIGuideLanguage.Reminder;
                if (AiGuideFailureRetryText != null)
                    AiGuideFailureRetryText.Text = AIGuideLanguage.RetryHint;

                if (refreshContent)
                {
                    if (_aiGuideService?.LastAnalysis?.Success == true)
                    {
                        var aiGuide = GetAiGuideService();
                        aiGuide.DetectWorkflow();
                        var dashboard = aiGuide.BuildVisualMentorDashboard(
                            summaryGenerated: aiGuide.LastSmartSummary?.Success == true);
                        ApplyVisualMentorDashboard(dashboard);
                        if (_isAiPanelOpen)
                        {
                            UpdateAiGuideStatus(
                                dashboard.IsReady ? AIGuideLanguage.Ready : AIGuideLanguage.NeedsFocus,
                                dashboard.IsReady ? "#22C55E" : "#FBBF24",
                                string.Empty);
                        }
                    }
                    else if (_isAiPanelOpen && _lastAiGuideFailure == null)
                    {
                        UpdateAiGuideStatus(AIGuideLanguage.OpenHint, "#64748B", string.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Failed to apply AI Guide language.", ex, category: "AIGuide", action: "Language");
            }
        }

        private void PrewarmRdpHostBackground()
        {
            GlobalExceptionHandler.Observe(
                EnsureRdpHostInitializedAsync(logFreeze: false),
                "RDP.PrewarmHost",
                notifyUser: false);
        }

        private void InitializeWelcomeOverlay()
        {
            WelcomeOverlay.DataContext = _welcomeOverlayVm;
            _welcomeOverlayVm.RequestClose += CloseWelcomeOverlay;
            _welcomeOverlayVm.StartAnalysisRequested += RunWelcomeDailyAnalysisAsync;
        }

        private void InitializeRdpToolbar()
        {
            ExtraIconToolbar.DataContext = _rdpToolbarVm;
            AiGuideMessagesList.ItemsSource = _aiChatMessages;

            _rdpToolbarVm.IsPremium = IsPremiumUser;
            _rdpToolbarVm.RequestToggleFocusMode += () =>
            {
                ToggleRdpViewportFullscreen();
                _rdpToolbarVm.IsFocusModeActive = _rdpViewportVm.IsRdpFocusMode;
            };
            _rdpToolbarVm.RequestSendCtrlAltDel += async () =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var server = GetQuickActionServer() ?? _activeSession?.Server;
                    using var audit = AuditOperationScope.BeginRdp(AuditActions.CtrlAltDel, server, _activeSession?.Id);
                    try
                    {
                        if (!ConfirmSendCtrlAltDel())
                        {
                            audit.CompleteCancelled("User declined confirmation");
                            return;
                        }

                        GetActiveCleanSession()?.SendCtrlAltDel();
                        audit.CompleteSuccess();
                    }
                    catch (Exception ex)
                    {
                        audit.CompleteFailed(ex.Message, ex);
                    }
                });
            };
            _rdpToolbarVm.RequestClipboardCopy += OnToolbarClipboardCopy;
            _rdpToolbarVm.RequestClipboardPaste += OnToolbarClipboardPaste;
            _rdpToolbarVm.RequestClipboardFileTransfer += OnToolbarClipboardFileTransfer;
            _rdpToolbarVm.RequestRiskAnalysis += OnToolbarRiskAnalysis;
            _rdpToolbarVm.RequestToggleFavorite += OnToolbarToggleFavorite;
            _rdpToolbarVm.RequestPerformanceLevel += OnToolbarPerformanceLevel;
            _rdpToolbarVm.RequestClipboardSync += OnToolbarClipboardSync;
            _rdpToolbarVm.RequestCaptureKeyboard += OnToolbarCaptureKeyboard;
            _rdpToolbarVm.RequestCaptureMouse += OnToolbarCaptureMouse;
            _rdpToolbarVm.RequestEventSearch += SearchToolbarEventsAsync;

            UpdateRdpToolbarState();
        }

        private RdpCleanSession? GetActiveCleanSession() => _activeSession?.CleanSession;

        private static string GetFavoriteServerKey(ServerInfo server)
        {
            var port = server.Port <= 0 ? 3389 : server.Port;
            return $"{server.Ip}:{port}";
        }

        private void UpdateRdpToolbarState()
        {
            var server = GetQuickActionServer();
            var favoriteTarget = GetToolbarFavoriteTarget();
            var connected = _activeSession?.CleanSession?.IsConnected == true;
            var snap = server != null ? (_lastSnapshot ?? GetLatestCachedSnapshot(server)) : _lastSnapshot;
            var hasCache = snap != null && (snap.Timeline?.Count > 0 || snap.ListenPorts?.Count > 0 || snap.CpuPct.HasValue);

            _rdpToolbarVm.IsConnected = connected;
            _rdpToolbarVm.HasServerContext = server != null;
            _rdpToolbarVm.HasCachedData = hasCache;
            _rdpToolbarVm.IsFocusModeActive = _rdpViewportVm.IsRdpFocusMode;
            _rdpToolbarVm.ServerDisplayName = favoriteTarget == null
                ? (server == null ? "Sunucu seçili değil" : GetServerDisplayName(server))
                : GetServerDisplayName(favoriteTarget);

            var canFavorite = favoriteTarget != null && !favoriteTarget.IsEphemeral;
            _rdpToolbarVm.IsFavoritesEnabled = canFavorite;
            _rdpToolbarVm.FavoriteDisabledTooltip = favoriteTarget == null
                ? (IsDashboardVisible()
                    ? "Klasör seçili veya sunucu yok"
                    : "Analiz ekranında sunucu seçin")
                : favoriteTarget.IsEphemeral
                    ? "Geçici bağlantılar favorilere eklenemez."
                    : "Analiz ekranında sunucu seçin";
            _rdpToolbarVm.IsFavorite = favoriteTarget != null
                && _favoriteServerKeys.Contains(GetFavoriteServerKey(favoriteTarget));

            if (server != null)
                _rdpToolbarVm.ResolutionScale = 125;
        }

        private bool IsDashboardVisible()
        {
            try
            {
                return SelectedServerPanel?.Visibility == Visibility.Visible;
            }
            catch
            {
                return false;
            }
        }

        private ServerInfo? GetToolbarFavoriteTarget()
        {
            if (!IsDashboardVisible() || _activeDashboardServer == null)
                return null;
            return _activeDashboardServer;
        }

        private static string GetServerDisplayName(ServerInfo server) =>
            string.IsNullOrWhiteSpace(server.Name) ? server.Ip : server.Name;

        private static bool IsSameServer(ServerInfo? a, ServerInfo? b)
        {
            if (a == null || b == null) return false;
            var portA = a.Port <= 0 ? 3389 : a.Port;
            var portB = b.Port <= 0 ? 3389 : b.Port;
            return string.Equals(a.Ip?.Trim(), b.Ip?.Trim(), StringComparison.OrdinalIgnoreCase) && portA == portB;
        }

        private ConnectionTreeNode? FindTreeNodeForServer(ServerInfo server)
        {
            foreach (var folder in _connectionTree)
            {
                foreach (var child in folder.Children)
                {
                    if (child.Server != null && IsSameServer(child.Server, server))
                        return child;
                }
            }
            return null;
        }

        private void SyncTreeSelectionToServer(ServerInfo server)
        {
            var node = FindTreeNodeForServer(server);
            if (node == null)
                return;

            if (ReferenceEquals(_selectedNode, node) && ReferenceEquals(ConnectionsTree.SelectedItem, node))
                return;

            _syncingTreeSelection = true;
            try
            {
                _selectedNode = node;
                ConnectionsTree.UpdateLayout();
                if (!SelectTreeNodeItem(node))
                    ConnectionsTree.Focus();
            }
            finally
            {
                _syncingTreeSelection = false;
            }
        }

        private bool SelectTreeNodeItem(ConnectionTreeNode target)
        {
            foreach (var folder in _connectionTree)
            {
                if (!folder.Children.Contains(target))
                    continue;

                if (ConnectionsTree.ItemContainerGenerator.ContainerFromItem(folder) is not TreeViewItem folderItem)
                    return false;

                folderItem.IsExpanded = true;
                folderItem.UpdateLayout();

                if (folderItem.ItemContainerGenerator.ContainerFromItem(target) is not TreeViewItem childItem)
                    return false;

                childItem.IsSelected = true;
                childItem.BringIntoView();
                return true;
            }

            return false;
        }

        private void ApplyActiveDashboardBadges()
        {
            foreach (var folder in _connectionTree)
            {
                foreach (var child in folder.Children)
                {
                    child.IsActiveDashboard = child.Server != null
                        && _activeDashboardServer != null
                        && IsSameServer(child.Server, _activeDashboardServer);
                }
            }
        }

        private void EvaluateAlerts(ServerInfo server, WmiSnapshot? snapshot, long? latencyMs)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var evaluation = await _alertEngine.EvaluateAsync(server, snapshot, latencyMs).ConfigureAwait(false);
                    var alerts = GetDistinctActiveAlerts();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateNotificationIndicator(alerts);
                        if (NotificationsPopup?.IsOpen == true)
                            RefreshNotificationsPopup();
                        NotifyNewAlerts(server, evaluation.Created);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Alerts] Evaluation failed: {ex.Message}");
                }
            });
        }

        private void ClearActiveDashboardServer()
        {
            _activeDashboardServer = null;
            _selectedServer = null;
            ApplyActiveDashboardBadges();
            UpdateRdpToolbarState();
        }

        private void OnToolbarClipboardCopy()
        {
            var mgr = GetActiveCleanSession();
            if (mgr == null || !mgr.IsConnected)
            {
                ShowToast("Pano için aktif RDP oturumu gerekli.", "📋");
                return;
            }

            mgr.SetClipboardRedirect(true);
            _rdpToolbarVm.ClipboardSyncEnabled = true;
            _rdpToolbarVm.CloseAllPopups();
            ShowToast("Uzak oturumda Ctrl+C ile kopyalayın — pano senkronize.", "📋");
        }

        private void OnToolbarClipboardPaste()
        {
            var mgr = GetActiveCleanSession();
            if (mgr == null || !mgr.IsConnected)
            {
                ShowToast("Yapıştırma için aktif RDP oturumu gerekli.", "📋");
                return;
            }

            mgr.SetClipboardRedirect(true);
            _rdpToolbarVm.ClipboardSyncEnabled = true;
            _rdpToolbarVm.CloseAllPopups();

            if (System.Windows.Clipboard.ContainsText())
                ShowToast("Yerel pano hazır — uzak oturumda Ctrl+V ile yapıştırın.", "📋");
            else
                ShowToast("Yerel pano boş.", "📋");
        }

        private void OnToolbarClipboardFileTransfer()
        {
            _rdpToolbarVm.CloseAllPopups();
            ShowToast("Dosya aktarımı: dosyayı RDP penceresine sürükleyin veya \\\\tsclient\\ ile paylaşılan klasörü kullanın.", "📁", 5000);
        }


        private void OnToolbarPerformanceLevel(RdpPerformanceLevel level)
        {
            var mgr = GetActiveCleanSession();
            if (mgr == null || !mgr.IsConnected) return;

            var quality = level switch
            {
                RdpPerformanceLevel.Low => RdpConnectionQuality.Wan,
                RdpPerformanceLevel.High => RdpConnectionQuality.Lan,
                _ => RdpConnectionQuality.Auto
            };
            mgr.ApplyConnectionQuality(quality);
            mgr.ApplyPerformanceLevel(level.ToString());
            _rdpToolbarVm.PerformanceLevel = level;
            _rdpToolbarVm.CloseAllPopups();
            ShowToast($"Performans: {level}", "⚡");
        }


        private void OnToolbarClipboardSync(bool enabled)
        {
            var server = GetQuickActionServer() ?? _activeSession?.Server;
            var action = enabled ? AuditActions.ClipboardEnable : AuditActions.ClipboardDisable;
            using var audit = AuditOperationScope.BeginRdp(action, server, _activeSession?.Id);
            try
            {
                GetActiveCleanSession()?.SetClipboardRedirect(enabled);
                audit.CompleteSuccess();
                ShowToast(enabled ? "Pano senkronizasyonu açık." : "Pano senkronizasyonu kapalı.", "📋");
            }
            catch (Exception ex)
            {
                audit.CompleteFailed(ex.Message, ex);
                ShowToast("Pano ayarı değiştirilemedi.", "⚠️");
            }
        }

        private void OnToolbarCaptureKeyboard(bool enabled)
        {
            GetActiveCleanSession()?.SetKeyboardCapture(enabled);
            ShowToast(enabled ? "Klavye yakalama açık." : "Klavye yakalama kapalı.", "⌨");
        }

        private void OnToolbarCaptureMouse(bool enabled)
        {
            GetActiveCleanSession()?.SetMouseCapture(enabled);
            ShowToast(enabled ? "Fare yakalama açık." : "Fare yakalama kapalı.", "🖱");
        }

        private bool ConfirmSendCtrlAltDel()
        {
            var server = GetQuickActionServer() ?? _activeSession?.Server ?? _selectedServer ?? _selectedNode?.Server;
            var name = server?.Name ?? "Sunucu";
            var ip = server?.Ip ?? "—";
            return ConfirmActionDialog.Show(this, ConfirmActionKind.CtrlAltDel, name, ip);
        }

        private void OnToolbarToggleFavorite() => ToggleFavorite(GetToolbarFavoriteTarget());

        private void ToggleFavorite(ServerInfo? server)
        {
            if (server == null)
            {
                ShowToast("Favorilere eklemek için analiz ekranında bir sunucu seçin.", "⭐");
                return;
            }

            if (server.IsEphemeral)
            {
                ShowToast("Geçici bağlantılar favorilere eklenemez.", "⭐");
                return;
            }

            var displayName = GetServerDisplayName(server);
            var key = GetFavoriteServerKey(server);
            if (_favoriteServerKeys.Contains(key))
            {
                _favoriteServerKeys.Remove(key);
                ShowToast($"{displayName} favorilerden çıkarıldı", "☆");
            }
            else
            {
                _favoriteServerKeys.Add(key);
                ShowToast($"{displayName} favorilere eklendi", "⭐");
            }

            RefreshFavoritesUi();
            SaveConnectionsToDisk();
        }

        private void MigrateFavoriteKey(string oldKey, string newKey)
        {
            if (string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
                return;

            if (_favoriteServerKeys.Remove(oldKey))
                _favoriteServerKeys.Add(newKey);
        }

        private void RefreshFavoritesUi()
        {
            RefreshFavoritesList();
            ApplyFavoriteBadgesToTree();
            ApplyActiveDashboardBadges();
            UpdateRdpToolbarState();
        }

        private void RefreshFavoritesList()
        {
            _favoriteNodes.Clear();
            foreach (var key in _favoriteServerKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var server = FindServerByFavoriteKey(key);
                if (server == null)
                    continue;

                _favoriteNodes.Add(new ConnectionTreeNode(server.Name, server.Ip, server) { IsFavorite = true });
            }

            FavoritesList.ItemsSource = _favoriteNodes;
            FavoritesSection.Visibility = _favoriteNodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private ServerInfo? FindServerByFavoriteKey(string key) =>
            _servers.FirstOrDefault(s => string.Equals(GetFavoriteServerKey(s), key, StringComparison.OrdinalIgnoreCase));

        private void ApplyFavoriteBadgesToTree()
        {
            foreach (var folder in _connectionTree)
            {
                foreach (var child in folder.Children)
                {
                    if (child.Server != null)
                        child.IsFavorite = _favoriteServerKeys.Contains(GetFavoriteServerKey(child.Server));
                }
            }
        }

        private void OnToolbarRiskAnalysis()
        {
            if (!IsAiGuideEntitled)
            {
                ShowPremiumPlusRequiredPrompt();
                return;
            }

            var server = GetQuickActionServer();
            var snap = _lastSnapshot ?? (server != null ? GetLatestCachedSnapshot(server) : null);
            if (snap == null)
            {
                ShowToast("Risk analizi için sunucu verisi yok. Önce tarama yapın veya bağlanın.", "⚠️");
                return;
            }

            using var audit = AuditOperationScope.BeginAnalysis(AuditActions.RiskAnalysis, server);
            try
            {
                if (!_isAiPanelOpen)
                    ToggleAiPanel();

            var sb = new StringBuilder();
            sb.AppendLine("⚠ Oturum Risk Analizi");
            sb.AppendLine($"Sunucu: {server?.Name ?? snap.Ip}");
            sb.AppendLine();

            if (snap.ListenPorts?.Count > 0)
                sb.AppendLine($"🔌 Açık portlar ({snap.ListenPorts.Count}): {string.Join(", ", snap.ListenPorts.Take(12))}{(snap.ListenPorts.Count > 12 ? "…" : "")}");
            else
                sb.AppendLine("🔌 Açık portlar: veri yok");

            var recentEvents = snap.Timeline?.OrderByDescending(e => e.Timestamp).Take(5).ToList() ?? new List<TimelineEvent>();
            sb.AppendLine(recentEvents.Count > 0
                ? $"📋 Son eventler:\n{string.Join("\n", recentEvents.Select(e => $"  • [{e.Timestamp:HH:mm}] {e.Level}: {e.Message}"))}"
                : "📋 Son eventler: kayıt yok");

            sb.AppendLine($"🧠 CPU: {(snap.CpuPct.HasValue ? $"{snap.CpuPct}%" : "—")} | RAM: {(snap.RamUsedPct.HasValue ? $"{snap.RamUsedPct:0}%" : "—")} | Disk: {(snap.DiskUsedPct.HasValue ? $"{snap.DiskUsedPct:0}%" : "—")}");

            var riskScore = EvaluateSecurityRisk(snap);
            sb.AppendLine($"🛡 Güvenlik riski: {riskScore.Label} ({riskScore.Score}/100)");
            if (!string.IsNullOrWhiteSpace(riskScore.Detail))
                sb.AppendLine($"   {riskScore.Detail}");

            if (DetectAdManagementContext(snap))
                sb.AppendLine("💡 Kullanıcı yönetimi algılandı — Active Directory araçları için rehberlik edebilirim.");

            if (DetectSqlServerContext(snap))
                sb.AppendLine("💡 SQL Server Management Studio algılandı — sorgu ve bakım önerileri sunabilirim.");

            AppendAiChatMessage(sb.ToString().TrimEnd());
            _rdpToolbarVm.CloseAllPopups();
            audit.CompleteSuccess();
            }
            catch (Exception ex)
            {
                audit.CompleteFailed(ex.Message, ex);
                ShowToast("Risk analizi tamamlanamadı.", "⚠️");
            }
        }

        private static (int Score, string Label, string Detail) EvaluateSecurityRisk(WmiSnapshot snap)
        {
            var score = 35;
            var details = new List<string>();

            if (snap.ListenPorts != null)
            {
                if (snap.ListenPorts.Contains(445)) { score += 15; details.Add("SMB (445) açık"); }
                if (snap.ListenPorts.Contains(3389)) { score += 5; details.Add("RDP (3389) açık"); }
                if (snap.ListenPorts.Contains(1433)) { score += 10; details.Add("SQL (1433) açık"); }
                if (snap.ListenPorts.Count > 25) { score += 10; details.Add("yüksek port sayısı"); }
            }

            var criticalStopped = snap.Services?.Count(s => s.IsCritical && s.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase)) ?? 0;
            if (criticalStopped > 0)
            {
                score += Math.Min(20, criticalStopped * 8);
                details.Add($"{criticalStopped} kritik servis durmuş");
            }

            if (snap.CpuPct > 90) { score += 8; details.Add("CPU yüksek"); }
            if (snap.RamUsedPct > 90) { score += 8; details.Add("RAM yüksek"); }
            if (snap.DiskUsedPct > 90) { score += 10; details.Add("disk dolu"); }

            score = Math.Clamp(score, 0, 100);
            var label = score >= 75 ? "Yüksek" : score >= 50 ? "Orta" : "Düşük";
            return (score, label, details.Count > 0 ? string.Join("; ", details) : "Belirgin risk sinyali yok");
        }

        private static bool DetectAdManagementContext(WmiSnapshot snap)
        {
            if (!string.IsNullOrWhiteSpace(snap.DomainName) &&
                snap.Services.Any(s => s.Name is "NTDS" or "ADWS" or "Kdc" or "DNS"))
                return true;

            return snap.TopProcesses.Any(p =>
                p.Name.Contains("dsa", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("ldp", StringComparison.OrdinalIgnoreCase) ||
                (p.Name.Contains("mmc", StringComparison.OrdinalIgnoreCase) &&
                 snap.Services.Any(s => s.Name is "NTDS" or "ADWS")));
        }

        private static bool DetectSqlServerContext(WmiSnapshot snap)
        {
            if (snap.Services.Any(s => s.Name.StartsWith("MSSQL", StringComparison.OrdinalIgnoreCase)))
                return true;

            return snap.TopProcesses.Any(p =>
                p.Name.Contains("ssms", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("sqlservr", StringComparison.OrdinalIgnoreCase));
        }

        private Task<IEnumerable<TimelineEvent>> SearchToolbarEventsAsync(string query)
        {
            var server = GetQuickActionServer();
            var snap = _lastSnapshot ?? (server != null ? GetLatestCachedSnapshot(server) : null);
            var source = snap?.Timeline ?? _currentTimelinePageItems.ToList();

            IEnumerable<TimelineEvent> results;
            if (string.IsNullOrWhiteSpace(query))
                results = source.OrderByDescending(e => e.Timestamp).Take(50);
            else
                results = source
                    .Where(e =>
                        (e.Message?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (e.Level?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (e.Type?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                    .OrderByDescending(e => e.Timestamp)
                    .Take(80);

            return Task.FromResult(results);
        }

        private void AppendAiChatMessage(string message, string role = "assistant")
        {
            _aiChatMessages.Add(new AIGuideMessage
            {
                Role = role,
                Content = message
            });
            ScrollAiGuideToBottom();
        }

        private void AppendAiGuideUserMessage(string message)
        {
            _aiChatMessages.Add(new AIGuideMessage
            {
                Role = "user",
                Content = message
            });
            ScrollAiGuideToBottom();
        }

        private void AppendAiGuideAssistantMessage(GuidanceResponse response)
        {
            var confidence = response.ConfidenceDetail?.OverallLabel ?? response.Confidence;
            var showDiagnostics = SettingsRuntime.Current.EnableAIGuideCaptureDiagnostics;
            _aiChatMessages.Add(new AIGuideMessage
            {
                Role = "assistant",
                Content = string.IsNullOrWhiteSpace(response.FormattedAnswer)
                    ? response.ErrorMessage ?? "Guidance unavailable."
                    : response.FormattedAnswer,
                Confidence = showDiagnostics ? confidence : null,
                Evidence = showDiagnostics ? response.VisibleEvidence : new List<GuidanceEvidence>()
            });
            ScrollAiGuideToBottom();
        }

        private void ScrollAiGuideToBottom()
        {
            try
            {
                AiGuideScrollViewer?.ScrollToEnd();
            }
            catch
            {
                // ignore scroll failures during layout
            }
        }

        private void SetAiGuideInputEnabled(bool enabled, string hintText)
        {
            if (AiGuideInputBox != null)
                AiGuideInputBox.IsEnabled = enabled;
            if (AiGuideSendButton != null)
                AiGuideSendButton.IsEnabled = enabled;
            if (AiGuideInputHintText != null)
                AiGuideInputHintText.Text = hintText;
        }

        private void SetupWelcomeScreen()
        {
            try
            {
                if (_welcomeDismissedThisSession || !SettingsService.Instance.Current.ShowWelcomeOverlayOnStartup)
                {
                    _welcomeOverlayVm.IsWelcomeOverlayVisible = false;
                    WelcomeOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                string rawUser = Environment.UserName;
                string firstName = rawUser.Contains(".") ? rawUser.Split('.')[0] : rawUser;
                if (!string.IsNullOrEmpty(firstName))
                    firstName = char.ToUpper(firstName[0]) + firstName.Substring(1);

                string greeting = "Merhaba";
                int hour = DateTime.Now.Hour;
                if (hour < 12) greeting = "Günaydın";
                else if (hour < 18) greeting = "İyi Günler";
                else greeting = "İyi Akşamlar";

                _welcomeOverlayVm.ConfigureGreeting($"{greeting} {firstName}", _servers.Count);
                _welcomeOverlayVm.ResetToWelcome();
                _welcomeOverlayVm.IsWelcomeOverlayVisible = true;
                WelcomeOverlay.Visibility = Visibility.Visible;
            }
            catch
            {
                _welcomeOverlayVm.IsWelcomeOverlayVisible = false;
                WelcomeOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseWelcomeOverlay()
        {
            _welcomeDismissedThisSession = true;

            var fadeOut = CloneStoryboard("FadeOutWelcome");
            if (fadeOut != null)
            {
                fadeOut.Completed += (_, _) =>
                {
                    _welcomeOverlayVm.IsWelcomeOverlayVisible = false;
                    WelcomeOverlay.Visibility = Visibility.Collapsed;
                    ActivateDashboardAfterWelcomeClose();
                };
                WelcomeOverlay.BeginStoryboard(fadeOut);
            }
            else
            {
                _welcomeOverlayVm.IsWelcomeOverlayVisible = false;
                WelcomeOverlay.Visibility = Visibility.Collapsed;
                ActivateDashboardAfterWelcomeClose();
            }
        }

        private void ActivateDashboardAfterWelcomeClose()
        {
            if (HasOpenRdpTab())
                return;

            var server = GetSelectedServer();
            if (server == null)
                return;

            _ = Dispatcher.BeginInvoke(() => ActivateDashboardForServer(server), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async Task RunWelcomeDailyAnalysisAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var servers = _servers.ToList();
            var batchStats = new AnalysisSnapshotPersistence.AnalysisBatchStats();

            var inputs = servers.Select(s => new DailyServerAnalysisService.ServerProbeInput
            {
                Server = s,
                CachedSnapshot = GetLatestCachedSnapshot(s)
            }).ToList();

            DailyAnalysisReport report;
            try
            {
                report = await _dailyAnalysisService.AnalyzeAsync(inputs, new DailyServerAnalysisService.AnalysisCallbacks
                {
                    OnStage = (message, progress) =>
                        _ = Dispatcher.InvokeAsync(() => _welcomeOverlayVm.SetLoadingStage(message, progress)),
                    PingAsync = TryPingMsAsync,
                    TcpOpenAsync = TryTcpOpenAsync,
                    TryRefreshSnapshot = async (server, token) =>
                    {
                        var credentials = SettingsApplicator.ResolveCredentials(server, SettingsRuntime.Current);
                        if (string.IsNullOrWhiteSpace(credentials.Username))
                            return null;

                        using var wmiCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        wmiCts.CancelAfter(TimeSpan.FromSeconds(6));

                        try
                        {
                            return await WmiCollector.GetSnapshotAsync(
                                server.Ip,
                                credentials.Username,
                                credentials.Password,
                                wmiCts.Token,
                                force: false);
                        }
                        catch (OperationCanceledException)
                        {
                            return null;
                        }
                        catch
                        {
                            return null;
                        }
                    },
                    OnServerAnalyzed = async (server, badge, snap, pingMs, rdpOpen, reason) =>
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            UpdateNodeStatus(server, badge);
                            if (snap?.Connected == true)
                            {
                                server.LastCpu = snap.CpuPct ?? server.LastCpu;
                                server.LastRam = snap.RamUsedPct.HasValue && snap.TotalRamGb.HasValue
                                    ? snap.RamUsedPct.Value / 100.0 * snap.TotalRamGb.Value
                                    : server.LastRam;
                                server.LastDisk = snap.DiskUsedPct ?? server.LastDisk;
                                server.Latency = pingMs.HasValue ? $"{pingMs.Value}ms" : server.Latency;
                                server.RdpSessions = snap.ActiveUsers?.ToString() ?? server.RdpSessions;
                                server.LastEvent = snap.Timeline.FirstOrDefault()?.Message ?? server.LastEvent;
                            }
                            else if (pingMs.HasValue)
                            {
                                server.Latency = $"{pingMs.Value}ms";
                            }
                        });

                        var key = GetServerKey(server);
                        var incoming = AnalysisSnapshotMapper.FromAnalysis(server, badge, snap, pingMs, rdpOpen, reason);
                        var outcome = await AnalysisSnapshotPersistence.SaveMergedAsync(key, incoming).ConfigureAwait(false);
                        var ramCached = outcome.Snapshot != null
                            && SnapshotMergeHelper.IsFullWmiMetrics(outcome.Snapshot.Metrics)
                            && WarmServerHistoryCache(key, outcome.Snapshot.Metrics);
                        batchStats.Record(outcome, ramCached);
                    }
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                report = BuildQuickAnalysisFromCurrentState(servers);
                await SaveAnalysisSnapshotsFromCachedStateAsync(servers, batchStats).ConfigureAwait(false);
            }

            AppLogger.Debug(
                $"Start Analysis complete: analyzed={batchStats.Analyzed}, saved={batchStats.Saved}, merged={batchStats.Merged}, failed={batchStats.Failed}, ramCached={batchStats.RamCached}",
                category: "Snapshot",
                action: "AnalysisComplete");

            await Dispatcher.InvokeAsync(() =>
            {
                UpdateOverviewDashboard();
                _welcomeOverlayVm.ApplyReport(report);
            });
        }

        private async Task SaveAnalysisSnapshotsFromCachedStateAsync(
            List<ServerInfo> servers,
            AnalysisSnapshotPersistence.AnalysisBatchStats stats)
        {
            foreach (var server in servers)
            {
                var snap = GetLatestCachedSnapshot(server);
                var badge = _serverStatuses.TryGetValue(server.Ip, out var b) ? b : server.LastStatus;
                var key = GetServerKey(server);
                var incoming = AnalysisSnapshotMapper.FromAnalysis(
                    server, badge, snap, snap?.PingMs >= 0 ? snap.PingMs : null, false, server.AlertMessage);
                var outcome = await AnalysisSnapshotPersistence.SaveMergedAsync(key, incoming).ConfigureAwait(false);
                var ramCached = outcome.Snapshot?.Metrics != null
                    && WarmServerHistoryCache(key, outcome.Snapshot.Metrics);
                stats.Record(outcome, ramCached);
            }
        }

        private bool WarmServerHistoryCache(string localKey, WmiSnapshot metrics)
        {
            if (!SnapshotMergeHelper.IsFullWmiMetrics(metrics) && !SnapshotMergeHelper.HasDisplayableData(metrics))
                return false;

            var history = _serverHistory.GetOrAdd(localKey, _ => new List<WmiSnapshot>());
            lock (history)
            {
                if (history.Count > 0
                    && IsLiveTelemetryAuthoritative(localKey)
                    && SnapshotMergeHelper.IsStaleDataSource(metrics)
                    && !SnapshotMergeHelper.ShouldAcceptIncomingVolatiles(metrics, history[^1]))
                    return false;

                var toStore = history.Count > 0
                    ? SnapshotMergeHelper.MergeWmiPreferNewer(history[^1], metrics)
                    : metrics;

                if (!SnapshotMergeHelper.IsFullWmiMetrics(toStore) && history.Count > 0
                    && SnapshotMergeHelper.IsFullWmiMetrics(history[^1]))
                    return false;

                if (history.Count > 0)
                    history[^1] = toStore;
                else
                    history.Add(toStore);

                SnapshotSecurityService.TrimHistoryInPlace(history);
            }

            return true;
        }

        private static void LogSnapshotLoaded(string serverKey, string source, DateTime? lastUpdatedUtc)
        {
            var hash = PersistentSnapshotStore.HashKey(serverKey)[..12];
            var age = lastUpdatedUtc.HasValue
                ? $"{(int)Math.Max(0, (DateTime.UtcNow - lastUpdatedUtc.Value.ToUniversalTime()).TotalMinutes)}m"
                : "?";
            AppLogger.Debug(
                $"Snapshot loaded for dashboard: key=#{hash}, source={source}, age={age}",
                category: "Snapshot",
                action: "DashboardLoad");
        }

        private static void LogSnapshotApplied(string serverKey, IEnumerable<string> sections)
        {
            var hash = PersistentSnapshotStore.HashKey(serverKey)[..12];
            AppLogger.Debug(
                $"Snapshot applied to dashboard: key=#{hash}, sections={string.Join(",", sections)}",
                category: "Snapshot",
                action: "DashboardApply");
        }

        /// <summary>
        /// Zaman aşımında mevcut dashboard durumundan özet üretir (sahte sayı üretmez).
        /// </summary>
        private DailyAnalysisReport BuildQuickAnalysisFromCurrentState(List<ServerInfo> servers)
        {
            var statuses = servers
                .Select(s => _serverStatuses.TryGetValue(s.Ip, out var b) ? b : s.LastStatus)
                .ToList();
            var (healthy, warning, critical, offline) = ServerHealthEvaluator.CountStatuses(statuses);

            var criticalFindings = new List<DailyServerFinding>();
            var warningFindings = new List<DailyServerFinding>();

            foreach (var server in servers)
            {
                var badge = _serverStatuses.TryGetValue(server.Ip, out var b) ? b : server.LastStatus;
                var snap = GetLatestCachedSnapshot(server);
                var finding = new DailyServerFinding
                {
                    ServerName = server.Name,
                    Ip = server.Ip,
                    Status = badge,
                    Summary = string.IsNullOrWhiteSpace(server.AlertMessage)
                        ? (badge == HealthBadge.Healthy ? "Normal" : badge.ToString())
                        : server.AlertMessage,
                    HasLiveData = snap?.Connected == true || server.Latency != "N/A"
                };

                if (badge is HealthBadge.Critical or HealthBadge.Offline)
                    criticalFindings.Add(finding);
                else if (badge == HealthBadge.Warning)
                    warningFindings.Add(finding);
            }

            return new DailyAnalysisReport
            {
                TotalServers = servers.Count,
                HealthyCount = healthy,
                WarningCount = warning,
                CriticalCount = critical,
                OfflineCount = offline,
                HasAnyLiveData = servers.Count > 0,
                CriticalFindings = criticalFindings,
                WarningFindings = warningFindings,
                Recommendations = new List<string> { "Tarama zaman aşımına uğradı; özet mevcut dashboard verilerinden oluşturuldu." }
            };
        }

        private WmiSnapshot? GetLatestCachedSnapshot(ServerInfo server)
        {
            var key = GetServerKey(server);
            if (_serverHistory.TryGetValue(key, out var history) && history.Count > 0)
                return history[^1];
            return null;
        }

        private void ShowTimelinePage(int page)
        {
            if (_currentTimelinePageItems == null || !_currentTimelinePageItems.Any())
            {
                SetTimelineEmptyState("No Windows Event Log entries are available for the current server yet.");
                return;
            }

            TimelineEmptyText.Visibility = Visibility.Collapsed;
            int totalPages = (int)Math.Ceiling(_currentTimelinePageItems.Count / (double)TimelinePageSize);
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            _currentTimelinePage = page;
            TimelinePageText.Text = $"Sayfa {page} / {totalPages}";

            // Slice the data
            var pageData = _currentTimelinePageItems
                .Skip((page - 1) * TimelinePageSize)
                .Take(TimelinePageSize)
                .ToList();

            TimelineItemsControl.ItemsSource = pageData;
            
            PrevTimelineBtn.IsEnabled = page > 1;
            NextTimelineBtn.IsEnabled = page < totalPages;

            UpdateTimelinePagination(totalPages);
        }

        private void SetTimelineEmptyState(string message)
        {
            TimelineItemsControl.ItemsSource = null;
            TimelineEmptyText.Text = message;
            TimelineEmptyText.Visibility = Visibility.Visible;
            TimelinePaginationPanel.Visibility = Visibility.Collapsed;
            TimelinePageText.Text = "Sayfa 0 / 0";
            PrevTimelineBtn.IsEnabled = false;
            NextTimelineBtn.IsEnabled = false;
        }

        private void OnPrevTimelineClick(object sender, RoutedEventArgs e)
        {
            if (_currentTimelinePage > 1)
            {
                _currentTimelinePage--;
                ShowTimelinePage(_currentTimelinePage);
            }
        }

        private void OnNextTimelineClick(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling(_currentTimelinePageItems.Count / (double)TimelinePageSize);
            if (_currentTimelinePage < totalPages)
            {
                _currentTimelinePage++;
                ShowTimelinePage(_currentTimelinePage);
            }
        }

        private void UpdateTimelinePagination(int totalPages)
        {
            TimelinePagesContainer.Children.Clear();
            
            if (totalPages <= 1)
            {
                TimelinePaginationPanel.Visibility = Visibility.Collapsed;
                return;
            }
            
            TimelinePaginationPanel.Visibility = Visibility.Visible;

            for (int i = 1; i <= totalPages; i++)
            {
                int pageNum = i;
                
                // Create button for unified view
                TimelinePagesContainer.Children.Add(CreatePaginationButton(i, pageNum));
            }
        }

        private Border CreatePaginationButton(int i, int pageNum)
        {
            var btn = new Border
            {
                Background = (i == _currentTimelinePage) ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6366F1")) : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2F394D")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = pageNum
            };

            var txt = new TextBlock
            {
                Text = i.ToString(),
                Foreground = (i == _currentTimelinePage) ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#94A3B8")),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };

            btn.Child = txt;
            btn.MouseDown += (s, e) => ShowTimelinePage(pageNum);
            return btn;
        }

        private async Task<StartupAnalysisResult> RunStartupAnalysisAsync(CancellationToken ct)
        {
            var result = new StartupAnalysisResult { Total = _servers.Count };
            var lockObj = new object();
            WmiSnapshot? firstSuccessfulSnapshot = null;
            
            var semaphore = new SemaphoreSlim(10);
            var tasks = _servers.Select(async s =>
            {
                await semaphore.WaitAsync();
                try
                {
                    bool isUp = await TryTcpOpenAsync(s.Ip, s.Port <= 0 ? 3389 : s.Port, 1500);
                    if (isUp) 
                    {
                        Interlocked.Increment(ref result.Healthy);
                        
                        // Quick check for WMI/RPC port (135) before trying full WMI connect
                        bool isWmiReady = await TryTcpOpenAsync(s.Ip, 135, 800);
                        if (!isWmiReady)
                        {
                            lock(lockObj)
                            {
                                result.Issues.Add(new ServerIssueInfo {
                                    ServerName = s.Name,
                                    Message = "WMI/RPC Erişimi Kapalı",
                                    Type = "Warning",
                                    Icon = "⚠️",
                                    Color = "#FBBF24"
                                });
                            }
                        }
                        else 
                        {
                            var snap = await WmiCollector.GetSnapshotAsync(s.Ip, s.Username, s.Password, ct);
                            
                            if (snap != null && snap.Connected)
                            {
                                lock(lockObj)
                                {
                                    if (firstSuccessfulSnapshot == null) firstSuccessfulSnapshot = snap;
                                }

                                var stoppedServices = snap.Services.Where(svc => svc.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase)).ToList();
                                if (stoppedServices.Any())
                                {
                                    Interlocked.Add(ref result.StoppedServices, stoppedServices.Count);
                                    lock(lockObj)
                                    {
                                        result.Issues.Add(new ServerIssueInfo {
                                            ServerName = s.Name,
                                            Message = $"{stoppedServices.Count} Kritik Servis Durmuş",
                                            Type = "Warning",
                                            Icon = "⚙️",
                                            Color = "#FBBF24"
                                        });
                                    }
                                }
                                
                                if (snap.DiskUsedPct >= 90)
                                {
                                    Interlocked.Increment(ref result.DiskWarnings);
                                    lock(lockObj)
                                    {
                                        result.Issues.Add(new ServerIssueInfo {
                                            ServerName = s.Name,
                                            Message = $"Disk Alanı Kritik (%{snap.DiskUsedPct})",
                                            Type = "Error",
                                            Icon = "⚠️",
                                            Color = "#F87171"
                                        });
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref result.Critical);
                        lock(lockObj)
                        {
                            result.Issues.Add(new ServerIssueInfo {
                                ServerName = s.Name,
                                Message = "Sunucuya Erişilemiyor (RDP Kapalı)",
                                Type = "Error",
                                Icon = "❌",
                                Color = "#EF4444"
                            });
                        }
                    }
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            
            // Assign the first successful snapshot to the result for timeline display
            result.AnalysisSnapshot = firstSuccessfulSnapshot;

            return result;
        }

        public class StartupAnalysisResult
        {
            public int Total;
            public int Healthy;
            public int Warning;
            public int Critical;
            public int StoppedServices;
            public int DiskWarnings;
            public List<ServerIssueInfo> Issues = new();
            public WmiSnapshot? AnalysisSnapshot;
        }

        public class ServerIssueInfo
        {
            public string ServerName { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty; // Error, Warning, Info
            public string Icon { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
        }

        private readonly Dictionary<string, HealthBadge> _serverStatuses = new();
        private readonly SemaphoreSlim _scanSemaphore = new(5); // Simultaneous scans limit

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AllowSetForegroundWindow(uint dwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZE = 0x20000000;
        private const int WS_MAXIMIZE = 0x01000000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_BORDER = 0x00800000;

        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;
        private const int SW_SHOW = 5;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_RESTORE = 9;

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_UPDATENOW = 0x0100;
        private const uint RDW_ALLCHILDREN = 0x0080;

        private const uint BM_CLICK = 0x00F5;
        private const int IDOK = 1;

        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;

        private const int WM_DPICHANGED = 0x02E0;


        private void InitializeHeaderIdentity()
        {
            try
            {
                var account = _accountService?.CurrentSession?.User;
                var isSignedIn = account is not null;
                var identity = account?.DisplayName ?? "Misafir";
                var planStatus = account is null
                    ? "Free plan"
                    : account.LicenseExpiresAtUtc is { } expiresAt && !string.Equals(account.PlanCode, "FREE", StringComparison.OrdinalIgnoreCase)
                        ? $"{account.PlanDisplayName} · {Math.Max(0, Math.Ceiling((expiresAt - DateTime.UtcNow).TotalDays))} gün kaldı"
                        : account.PlanDisplayName;
                if (HeaderUserNameText != null)
                    HeaderUserNameText.Text = identity;
                if (HeaderUserRoleText != null)
                    HeaderUserRoleText.Text = planStatus;
                if (HeaderUserInitials != null)
                {
                    var initials = string.Concat(identity.Where(char.IsLetterOrDigit).Take(2)).ToUpperInvariant();
                    HeaderUserInitials.Text = string.IsNullOrWhiteSpace(initials) ? "NV" : initials;
                }
                if (HeaderAccountButton != null)
                    HeaderAccountButton.Visibility = isSignedIn ? Visibility.Collapsed : Visibility.Visible;
                if (HeaderLogoutButton != null)
                    HeaderLogoutButton.Visibility = isSignedIn ? Visibility.Visible : Visibility.Collapsed;
                if (AdminHeaderButton != null)
                    AdminHeaderButton.Visibility = account?.IsAdmin == true ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Header identity init failed: {ex.Message}", category: "UI", action: "Init");
            }
        }

        private async void OnAccountLogoutClick(object sender, RoutedEventArgs e)
        {
            if (_accountService is null || _accountLogoutInProgress)
                return;
            _accountLogoutInProgress = true;
            if (HeaderLogoutButton != null)
                HeaderLogoutButton.IsEnabled = false;
            try
            {
                await _accountService.LogoutAsync();
            }
            catch
            {
                // AccountService always clears local session in its finally block.
            }
            _accountLogoutInProgress = false;
            if (HeaderLogoutButton != null)
                HeaderLogoutButton.IsEnabled = true;
            SetAccountService(_accountService);
            ShowToast("Misafir moda geçildi.", "\uE72B");
        }

        private void OnAccountAccessClick(object sender, RoutedEventArgs e)
        {
            if (System.Windows.Application.Current is App app &&
                app.EnsureSignedInForAccountAction(this))
            {
                SetAccountService(_accountService);
                ShowToast("Hesabınızla giriş yapıldı.", "\uE73E");
            }
        }

        public void SetAccountService(IAccountService? accountService)
        {
            _accountService = accountService;
            var session = _accountService?.CurrentSession;
            FeatureEntitlementService.Instance.SetAccountAuthenticated(
                session is not null, session?.User.PlanCode);
            InitializeHeaderIdentity();
            UpdateLicensingUi();
            EnforceAiGuideLicenseState();
            _rdpToolbarVm.IsPremium = IsPremiumUser;
        }

        private string GetActiveConnectionSearchQuery()
        {
            if (!string.IsNullOrWhiteSpace(_globalSearchQuery))
                return _globalSearchQuery.Trim();
            return _sidebarSearchQuery.Trim();
        }

        private void OnGlobalSearchChanged(object sender, TextChangedEventArgs e)
        {
            _globalSearchQuery = GlobalSearchBox?.Text ?? string.Empty;
            BuildConnectionTree(selectServer: _selectedServer ?? _selectedNode?.Server);
        }

        private void OnSidebarSearchChanged(object sender, TextChangedEventArgs e)
        {
            _sidebarSearchQuery = SidebarSearchBox?.Text ?? string.Empty;
            BuildConnectionTree(selectServer: _selectedServer ?? _selectedNode?.Server);
        }

        private void OnNotificationsClick(object sender, RoutedEventArgs e)
        {
            if (NotificationsPopup.IsOpen)
            {
                NotificationsPopup.IsOpen = false;
                return;
            }

            RefreshNotificationsPopup();
            NotificationsPopup.IsOpen = true;
        }

        private IReadOnlyList<AlertState> GetDistinctActiveAlerts()
        {
            return _alertEngine.GetActiveAlerts()
                .GroupBy(a => $"{a.ServerIp}|{a.RuleId}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(a => a.LastSeen).First())
                .OrderBy(a => string.Equals(a.Severity, AlertSeverities.Critical, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(a => a.LastSeen)
                .ToList();
        }

        private void UpdateNotificationIndicator(IReadOnlyList<AlertState>? alerts = null)
        {
            alerts ??= GetDistinctActiveAlerts();
            var count = alerts.Count;
            NotificationBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotificationBadgeText.Text = count > 99 ? "99+" : count.ToString();
            NotificationsButton.ToolTip = count > 0
                ? UiLocalizationService.T($"{count} active alert(s)", $"{count} aktif uyar\u0131")
                : UiLocalizationService.T("Notifications", "Bildirimler");
        }

        private void RefreshNotificationsPopup()
        {
            var alerts = GetDistinctActiveAlerts();
            var settings = SettingsService.Instance.Current;
            var deliveryEnabled = settings.EnableNotifications
                                  && (settings.CriticalAlerts || settings.WarningAlerts || settings.InformationAlerts);

            UpdateNotificationIndicator(alerts);
            NotificationPanelTitle.Text = UiLocalizationService.T("Notification Center", "Bildirim Merkezi");
            NotificationPanelSubtitle.Text = UiLocalizationService.T("Live alert feed", "Canl\u0131 sistem uyar\u0131lar\u0131");
            NotificationCountText.Text = UiLocalizationService.T($"{alerts.Count} active", $"{alerts.Count} aktif");
            NotificationStatusText.Text = deliveryEnabled
                ? UiLocalizationService.T("Alert notifications are enabled", "Uyar\u0131 bildirimleri a\u00e7\u0131k")
                : UiLocalizationService.T("Alert notifications are disabled", "Uyar\u0131 bildirimleri kapal\u0131");
            NotificationStatusDot.Fill = new SolidColorBrush(deliveryEnabled
                ? System.Windows.Media.Color.FromRgb(34, 197, 94)
                : System.Windows.Media.Color.FromRgb(100, 116, 139));
            NotificationEmptyTitle.Text = UiLocalizationService.T("No active alerts", "Aktif uyar\u0131 yok");
            NotificationEmptyDescription.Text = UiLocalizationService.T(
                "Your monitored servers look healthy.",
                "\u0130zlenen sunucularda aktif sorun g\u00f6r\u00fcnm\u00fcyor.");
            NotificationSettingsButtonText.Text = UiLocalizationService.T("Notification settings", "Bildirim ayarlar\u0131");

            NotificationItemsPanel.Children.Clear();
            NotificationEmptyState.Visibility = alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var alert in alerts.Take(12))
                NotificationItemsPanel.Children.Add(CreateNotificationCard(alert));

            if (alerts.Count > 12)
            {
                NotificationItemsPanel.Children.Add(new TextBlock
                {
                    Text = UiLocalizationService.T(
                        $"+{alerts.Count - 12} more active alerts",
                        $"+{alerts.Count - 12} aktif uyar\u0131 daha"),
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 141, 163)),
                    FontSize = 11,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 10)
                });
            }
        }

        private Border CreateNotificationCard(AlertState alert)
        {
            var isCritical = string.Equals(alert.Severity, AlertSeverities.Critical, StringComparison.OrdinalIgnoreCase);
            var accent = isCritical ? System.Windows.Media.Color.FromRgb(239, 68, 68) : System.Windows.Media.Color.FromRgb(245, 158, 11);
            var card = new Border
            {
                Background = new SolidColorBrush(isCritical ? System.Windows.Media.Color.FromRgb(31, 20, 29) : System.Windows.Media.Color.FromRgb(31, 27, 20)),
                BorderBrush = new SolidColorBrush(isCritical ? System.Windows.Media.Color.FromRgb(73, 35, 45) : System.Windows.Media.Color.FromRgb(74, 57, 27)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(4, 0, 4, 7),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = alert.ServerIp,
                ToolTip = alert.Message
            };
            card.MouseLeftButtonUp += OnNotificationCardClick;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bar = new Border
            {
                Background = new SolidColorBrush(accent),
                CornerRadius = new CornerRadius(9, 0, 0, 9)
            };
            Grid.SetColumn(bar, 0);
            root.Children.Add(bar);

            var icon = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(isCritical ? System.Windows.Media.Color.FromRgb(75, 29, 40) : System.Windows.Media.Color.FromRgb(76, 54, 20)),
                Margin = new Thickness(11, 11, 9, 11),
                Child = new TextBlock
                {
                    Text = isCritical ? "\uE814" : "\uE7BA",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(accent),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(icon, 1);
            root.Children.Add(icon);

            var serverLabel = string.Equals(alert.ServerName, alert.ServerIp, StringComparison.OrdinalIgnoreCase)
                ? alert.ServerIp
                : $"{alert.ServerName}  \u2022  {alert.ServerIp}";
            var details = string.IsNullOrWhiteSpace(alert.CurrentValue)
                ? serverLabel
                : $"{serverLabel}  \u2022  {alert.CurrentValue}";
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 9, 8, 9) };
            text.Children.Add(new TextBlock
            {
                Text = GetLocalizedAlertTitle(alert),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 237, 245)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = details,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 141, 163)),
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(text, 2);
            root.Children.Add(text);

            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 11, 8) };
            meta.Children.Add(new Border
            {
                Background = new SolidColorBrush(isCritical ? System.Windows.Media.Color.FromRgb(75, 29, 40) : System.Windows.Media.Color.FromRgb(76, 54, 20)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 2, 7, 2),
                Child = new TextBlock
                {
                    Text = isCritical
                        ? UiLocalizationService.T("Critical", "Kritik")
                        : UiLocalizationService.T("Warning", "Uyar\u0131"),
                    Foreground = new SolidColorBrush(accent),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold
                }
            });
            meta.Children.Add(new TextBlock
            {
                Text = alert.LastSeen.ToLocalTime().ToString("HH:mm"),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                FontSize = 9,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 0, 0)
            });
            Grid.SetColumn(meta, 3);
            root.Children.Add(meta);

            card.Child = root;
            return card;
        }

        private static string GetLocalizedAlertTitle(AlertState alert)
        {
            var turkish = alert.RuleId switch
            {
                "cpu-usage-high" => "CPU kullan\u0131m\u0131 kritik seviyede",
                "ram-usage-high" => "RAM kullan\u0131m\u0131 kritik seviyede",
                "disk-usage-high" => "Disk kullan\u0131m\u0131 kritik seviyede",
                "firewall-disabled" => "G\u00fcvenlik duvar\u0131 devre d\u0131\u015f\u0131",
                "defender-disabled" => "Windows Defender devre d\u0131\u015f\u0131",
                "bitlocker-disabled" => "BitLocker devre d\u0131\u015f\u0131",
                "critical-service-stopped" => "Kritik servis durduruldu",
                "server-unreachable" => "Sunucuya eri\u015filemiyor",
                "risky-port-open" => "Riskli a\u00e7\u0131k port alg\u0131land\u0131",
                "windows-update-reboot-required" => "Windows Update yeniden ba\u015flatma bekliyor",
                _ => alert.Title
            };
            return UiLocalizationService.T(alert.Title, turkish);
        }

        private void NotifyNewAlerts(ServerInfo server, IReadOnlyList<AlertState> created)
        {
            var fresh = created
                .GroupBy(a => a.RuleId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (fresh.Count == 0)
                return;

            var critical = fresh.Any(a => string.Equals(a.Severity, AlertSeverities.Critical, StringComparison.OrdinalIgnoreCase));
            var level = critical ? NotificationService.AlertLevel.Critical : NotificationService.AlertLevel.Warning;
            var message = fresh.Count == 1
                ? $"{server.Name}: {GetLocalizedAlertTitle(fresh[0])}"
                : UiLocalizationService.T(
                    $"{server.Name}: {fresh.Count} new active alerts",
                    $"{server.Name}: {fresh.Count} yeni aktif uyar\u0131");
            ShowToast(message, critical ? "\u26D4" : "\u26A0", level: level);
        }

        private void OnNotificationCardClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { Tag: string serverIp })
                return;

            var server = _servers.FirstOrDefault(s => string.Equals(s.Ip, serverIp, StringComparison.OrdinalIgnoreCase));
            if (server == null)
                return;

            NotificationsPopup.IsOpen = false;
            ActivateDashboardForServer(server);
        }

        private void OnNotificationSettingsClick(object sender, RoutedEventArgs e)
        {
            NotificationsPopup.IsOpen = false;
            OpenSettingsWindow(SettingsCategory.Notifications);
        }

        private void OnAiPanelRiskAnalysisClick(object sender, RoutedEventArgs e) => OnToolbarRiskAnalysis();

        private void LaunchEmbeddedMstsc(ServerInfo server)
        {
            _ = LaunchEmbeddedMstscAsync(server);
        }

        private async Task LaunchEmbeddedMstscAsync(ServerInfo server)
        {
            if (server == null) return;

            PauseBackgroundScans();
            PauseAiGuideForRdpConnection(server.Ip);

            var existingSession = _rdpSessions.FirstOrDefault(s =>
                s.Server.Ip == server.Ip && s.CleanSession != null);
            if (existingSession != null)
            {
                var state = existingSession.CleanSession!.ConnectionState;
                if (state == RdpConnectionState.Connecting || state == RdpConnectionState.Connected || state == RdpConnectionState.LoginComplete)
                {
                    _pendingConnectAudit?.CompleteCancelled("Already connecting");
                    _pendingConnectAudit = null;
                    SwitchToTab(existingSession);
                    UpdateConnectButtonState();
                    return;
                }

                CloseTabSession(existingSession);
            }

            var maxConn = SettingsService.Instance.Current.MaxConcurrentConnections;
            if (_rdpSessions.Count >= maxConn)
            {
                _pendingConnectAudit?.CompleteCancelled($"Maximum concurrent connections ({maxConn}) reached");
                _pendingConnectAudit = null;
                ShowToast($"Maximum concurrent connections ({maxConn}) reached.", "\uE7BA");
                return;
            }

            var connectServer = ResolveServerForConnection(server);
            var connectLabel = string.IsNullOrWhiteSpace(connectServer.Name) ? connectServer.Ip : connectServer.Name;
            RdpCleanHostLogger.Log($"Launch clean RDP session for {connectLabel} ({connectServer.Ip})");

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            RdpTabSession? session = null;
            try
            {
                session = new RdpTabSession { Server = connectServer };
                session.ReadinessGate.MarkConnecting();
                session.ConnectionCts = new CancellationTokenSource();
                session.FillMode = connectServer.FillMode;
                session.ConnectAudit = _pendingConnectAudit;
                _pendingConnectAudit = null;
                session.IsQuickConnect = _connectAsQuickConnect;

                if (!_rdpHostInitialized)
                    await EnsureRdpHostInitializedAsync().ConfigureAwait(true);

                var contentVm = CreateSessionViewModel(session, connectServer);
                contentVm.ApplySidebarWidth(LeftSidebarColumn?.ActualWidth ?? 0);
                var tabView = EnsureTabSessionView(session, contentVm);
                var clean = contentVm.CleanSession;

                WireCleanSessionEvents(session, connectServer, clean);

                _rdpSessions.Add(session);
                _activeSession = session;

                ShowRdpSessionWorkspace();
                ApplyActiveWorkspaceContent();
                RefreshTabBar();

                ForceWorkspaceLayoutPass();
                await Dispatcher.InvokeAsync(() => ForceWorkspaceLayoutPass(), DispatcherPriority.Loaded);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                _ = FinalizeConnectAuditAsync(session, contentVm.ConnectAttemptTask);
            }
            catch (Exception ex)
            {
                session?.ConnectAudit?.CompleteFailed("RDP bağlantısı kurulamadı.");
                if (session != null) session.ConnectAudit = null;
                _pendingConnectAudit?.CompleteFailed("RDP bağlantısı kurulamadı.");
                _pendingConnectAudit = null;
                _pendingServer = null;
                RdpCleanHostLogger.Log($"Launch exception type={ex.GetType().Name} hresult={ex.HResult}");
                ShowRdpConnectionFailedState("Sunucuya bağlanılamadı. Sunucu adresini ve ağ bağlantısını kontrol edin.");
            }
        }

        private void WireCleanSessionEvents(RdpTabSession session, ServerInfo server, RdpCleanSession clean)
        {
            clean.StatusChanged += msg => Dispatcher.BeginInvoke(() =>
            {
                if (session != _activeSession || string.IsNullOrWhiteSpace(msg))
                    return;
                if (session.CleanSession?.IsConnecting == true)
                    ShowRdpConnectingState(msg);
            });

            clean.Connected += () => Dispatcher.BeginInvoke(() =>
            {
                if (session != _activeSession)
                    return;

                session.ReadinessGate.MarkSessionUsable(server.Ip);
                session.TabView?.HideConnecting();
                session.TabView?.SetConnectedStatus();
                session.TabView?.SetResolutionProfile(session.Server?.ResolutionProfile ?? RdpViewportSizing.DefaultProfile);
                HideRdpFailureState();
                UpdateConnectionStatus("Bağlandı.", true);
                ApplyRdpViewportSizeNow();
                OnRdpTabSessionConnected(session, server);
            });

            clean.ConnectionFailed += failure => Dispatcher.BeginInvoke(() =>
            {
                if (session != _activeSession)
                    return;

                var title = failure.State switch
                {
                    RdpConnectionState.TimedOut => "Bağlantı zaman aşımına uğradı",
                    _ => "Bağlantı kurulamadı"
                };
                var diagnostics = failure.Diagnostics.ToReport();
                if (session.ContentViewModel != null)
                    diagnostics = session.ContentViewModel.BuildDiagnosticsReport()
                        + Environment.NewLine + Environment.NewLine
                        + failure.Diagnostics.ToReport();

                session.TabView?.ShowFailure(title, failure.Message, diagnostics);
                UpdateConnectionStatus(failure.Message, false);
                session.ConnectAudit?.CompleteFailed(failure.Message);
                session.ConnectAudit = null;
                ResumeBackgroundScans();
                RefreshTabBar();
            });

            clean.Disconnected += (reason, _) => Dispatcher.BeginInvoke(() =>
            {
                session.ReadinessCts?.Cancel();
                session.ReadinessGate.Reset();
                if (session == _activeSession)
                    _aiGuidePausedForRdpConnection = false;

                if (session == _activeSession)
                {
                    UpdateConnectionStatus("Bağlantı kesildi", false);
                    UpdateRdpToolbarState();
                    ResumeBackgroundScans();
                    RefreshTabBar();
                }
            });
        }

        private void CleanupFailedRdpSession(RdpTabSession session)
        {
            try
            {
                RemoveTabSessionView(session);
                _rdpSessions.Remove(session);
                if (_activeSession == session)
                    _activeSession = _rdpSessions.LastOrDefault();

                session.CleanSession?.Dispose();

                RefreshTabBar();
            }
            catch (Exception ex) { AppLogger.Error("Failed RDP session cleanup error.", ex); }
        }

        private void ApplyRdpFullViewportChrome(bool active)
        {
            try
            {
                if (RdpColumnBorder == null || RdpContentGrid == null) return;
                if (active && !_rdpViewportVm.IsRdpFocusMode)
                {
                    RdpColumnBorder.Padding = new Thickness(0);
                    RdpColumnBorder.Background = System.Windows.Media.Brushes.Black;
                    MainContentGrid.Background = System.Windows.Media.Brushes.Black;
                    RdpContentGrid.Margin = new Thickness(0);
                    ActiveConnectionHeader.Margin = new Thickness(0, 0, 0, 4);
                    ExtraIconToolbar.Margin = new Thickness(0, 0, 0, 2);
                }
                else if (!active && !_rdpViewportVm.IsRdpFocusMode)
                {
                    RdpColumnBorder.Padding = new Thickness(12, 8, 12, 8);
                    RdpColumnBorder.Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush");
                    MainContentGrid.Background = null;
                    RdpContentGrid.Margin = new Thickness(0);
                    ActiveConnectionHeader.Margin = new Thickness(0, 0, 0, 10);
                    ExtraIconToolbar.Margin = new Thickness(0, 0, 0, 4);
                }
            }
            catch (Exception ex) { AppLogger.Error("RDP viewport chrome apply failed.", ex); }
        }

        private void ShowRdpConnectingState(string message)
        {
            HideRdpFailureState();
            ShowRdpSessionWorkspace();
            GetActiveTabView()?.ShowConnecting(message);
            ConnectionStatusText.Text = message;
            UpdateConnectButtonState();
        }

        private void ShowRdpConnectionFailedState(string message, RdpConnectionFailureInfo? failure = null)
        {
            if (_activeSession?.CleanSession?.IsConnected == true)
            {
                AppLogger.Warning(
                    $"Ignoring failure overlay while RDP is connected: {message}",
                    category: "RDP",
                    action: "ConnectedFailureIgnored");
                RdpCleanHostLogger.Log($"Ignoring failure overlay while connected: {message}");
                GetActiveTabView()?.SetCaptureUnavailableStatus();
                return;
            }

            _pendingServer = null;

            var info = failure ?? new RdpConnectionFailureInfo
            {
                State = RdpConnectionState.Failed,
                Message = message,
                Diagnostics = new RdpConnectionAttemptDiagnostics { FailureReason = message }
            };

            _lastRdpFailureInfo = info;
            _lastRdpFailureServer = _activeSession?.Server ?? _lastRdpFailureServer;

            GetActiveTabView()?.ShowFailure(
                info.State switch
                {
                    RdpConnectionState.TimedOut => "Bağlantı zaman aşımına uğradı",
                    RdpConnectionState.Cancelled => "Connection cancelled",
                    _ => "Bağlantı kurulamadı"
                },
                info.Message,
                info.Diagnostics.ToReport());
            UpdateConnectionStatus(message, false);
            ShowToast(message, "\uE783", 5000);
            UpdateConnectButtonState();
            RefreshTabBar();
        }

        private void PopulateRdpFailureDiagnostics(RdpConnectionAttemptDiagnostics d)
        {
            // Diagnostics rendered inside RdpSessionView.ShowFailure
        }

        private void HideRdpFailureState() => GetActiveTabView()?.HideFailure();


        private static string BuildMstscCommand(string host, int port)
        {
            var target = port <= 0 || port == 3389 ? host : $"{host}:{port}";
            var mstscPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
            return $"\"{mstscPath}\" /v:{target}";
        }

        private void OnRdpFailureCopyMstscCommandClick(object sender, RoutedEventArgs e)
        {
            var server = _lastRdpFailureServer ?? _activeSession?.Server;
            if (server == null)
                return;

            var port = _lastRdpFailureInfo?.Diagnostics.Port ?? server.Port;
            if (port <= 0) port = 3389;

            try
            {
                System.Windows.Clipboard.SetText(BuildMstscCommand(server.Ip, port));
                ShowToast("mstsc command copied to clipboard.", "\uE8C8", 2500);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to copy mstsc command.", ex);
                ShowToast("Could not copy mstsc command.", "\uE783", 3000);
            }
        }

        private void OnRdpFailureOpenMstscClick(object sender, RoutedEventArgs e)
        {
            var server = _lastRdpFailureServer ?? _activeSession?.Server;
            if (server == null)
                return;

            var port = _lastRdpFailureInfo?.Diagnostics.Port ?? 3389;
            try
            {
                ExternalMstscFallback.LaunchDirect(server.Ip, port);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to launch mstsc.exe.", ex);
                ShowToast("Could not open Windows Remote Desktop.", "\uE783", 3000);
            }
        }

        private void UpdateConnectButtonState()
        {
            if (SidebarConnectButton == null)
                return;

            var selected = _selectedNode?.Server;
            if (selected == null)
            {
                SidebarConnectButton.IsEnabled = true;
                return;
            }

            var isConnecting = _rdpSessions.Any(s =>
                s.Server.Ip == selected.Ip
                && s.CleanSession?.ConnectionState is RdpConnectionState.Connecting or RdpConnectionState.Connected);
            SidebarConnectButton.IsEnabled = !isConnecting;
        }

        private void OnRdpFailureRetryClick(object sender, RoutedEventArgs e)
        {
            var server = _lastRdpFailureServer ?? _activeSession?.Server;
            if (server == null)
            {
                HideRdpFailureState();
                return;
            }

            HideRdpFailureState();
            ConnectToServer(server, quickConnect: _connectAsQuickConnect);
        }

        private void OnRdpFailureCancelClick(object sender, RoutedEventArgs e)
        {
            HideRdpFailureState();
            if (_activeSession != null)
            {
                CloseTabSession(_activeSession);
            }

            _pendingServer = null;
            UpdateConnectionStatus("Connection cancelled.", false);
            ResumeBackgroundScans();
            UpdateConnectButtonState();
        }

        private void OnRdpFailureCopyDiagnosticsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var report = _lastRdpFailureInfo?.Diagnostics.ToReport()
                    ?? _activeSession?.CleanSession?.LastDiagnostics.ToReport()
                    ?? "No RDP diagnostics available.";
                System.Windows.Clipboard.SetText(report);
                ShowToast("Diagnostics copied to clipboard.", "\uE8C8", 2500);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to copy RDP diagnostics.", ex);
                ShowToast("Could not copy diagnostics.", "\uE783", 3000);
            }
        }

        private void ShowRdpWaitingForScreenState()
        {
            ShowRdpConnectingState(RdpWaitingForScreenMessage);
        }

        private async Task EnsureRdpHostInitializedAsync(bool logFreeze = true)
        {
            if (_rdpHostInitialized && CentralWorkspaceHost != null)
                return;

            if (logFreeze)
                _connectUiWatch.Restart();

            await Dispatcher.InvokeAsync(() => InitializeRdpHost(), DispatcherPriority.Background);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            _rdpHostInitialized = CentralWorkspaceHost != null;

            if (logFreeze)
                LogConnectUiFreeze("EnsureRdpHostInitializedAsync");
        }

        private void LogConnectUiFreeze(string checkpoint)
        {
            var ms = (int)_connectUiWatch.ElapsedMilliseconds;
            if (ms >= 250)
                RdpConnectionLogger.UiFreezeDetected(checkpoint, ms);
        }

        private void PauseAiGuideForRdpConnection(string? host)
        {
            if (!IsAiGuideEntitled || _aiGuideService == null)
                return;

            RdpConnectionLogger.RdpConnectionStarted(host);

            var hadInFlight = _aiGuideCaptureCts != null
                || _aiGuideService.State is AIGuideSessionState.Capturing or AIGuideSessionState.Guiding;

            _aiGuidePausedForRdpConnection = true;
            Interlocked.Increment(ref _aiGuideCaptureRunId);

            try { _aiGuideCaptureCts?.Cancel(); } catch { /* ignore */ }
            try { _aiGuideCaptureCts?.Dispose(); } catch { /* ignore */ }
            _aiGuideCaptureCts = null;

            CancelAiGuideGuidanceInFlight();
            _aiGuideService?.CancelInFlightAnalysis();

            if (hadInFlight)
            {
                AIGuideAnalysisLogger.ExistingAiGuideTaskCancelled();
                RdpConnectionLogger.ExistingAiGuideTaskCancelled(host);
            }

            RdpConnectionLogger.AiGuidePausedForConnection(host);

            if (_isAiPanelOpen)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    SetAiGuideInputEnabled(false, AIGuideLanguage.RdpLoadingMessage);
                    UpdateAiGuideStatus(AIGuideLanguage.AiGuidePausedForConnection, "#FBBF24", string.Empty);
                }, DispatcherPriority.Background);
            }
        }

        private void ResumeAiGuideAfterStableReady(string? host)
        {
            _aiGuidePausedForRdpConnection = false;

            if (!_isAiPanelOpen || !IsAiGuideEntitled || _aiGuideService == null)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                if (_aiGuideService.LastAnalysis?.Success == true && _aiGuideService.LastMentorDashboard != null)
                {
                    ApplyVisualMentorDashboard(_aiGuideService.LastMentorDashboard);
                    UpdateAiGuideStatus(AIGuideLanguage.Ready, "#22C55E", string.Empty);
                    SetAiGuideInputEnabled(true, AIGuideLanguage.InputHintReady);
                }
                else
                {
                    UpdateAiGuideStatus(AIGuideLanguage.OpenHint, "#64748B", string.Empty);
                    SetAiGuideInputEnabled(true, AIGuideLanguage.InputHintReady);
                }
            }, DispatcherPriority.Background);
        }

        private void OnRdpTabSessionConnected(RdpTabSession session, ServerInfo server)
        {
            _pendingServer = null;

            if (session.IsQuickConnect)
            {
                session.CleanSession?.SetClipboardRedirect(false);
                if (session == _activeSession)
                    _rdpToolbarVm.ClipboardSyncEnabled = false;
            }

            if (session == _activeSession)
            {
                LoggingContext.SessionId = session.Id;
                session.ConnectAudit?.CompleteSuccess();
                session.ConnectAudit = null;
                CompleteRdpSessionReady(session, server);
                _ = RunOptionalRdpCaptureProbeAsync(session, server);
            }
            else
            {
                session.ReadinessGate.MarkSessionUsable(server.Ip);
                session.ConnectAudit?.CompleteSuccess();
                session.ConnectAudit = null;
            }
        }

        private void OnLegacyRdpManagerConnected()
        {
            _pendingServer = null;
            _legacyReadinessGate.MarkSessionUsable(_activeSession?.Server?.Ip);
            UpdateConnectionStatus("Bağlandı.", true);
            GetActiveTabView()?.SetConnectedStatus();
            HideRdpFailureState();
            if (RdpLoadingOverlay != null) RdpLoadingOverlay.Visibility = Visibility.Collapsed;
            _ = RunLegacyOptionalCaptureProbeAsync();
        }

        private Task<RdpWarmupSnapshot> ReadRdpWarmupSnapshotAsync(RdpTabSession session) =>
            Dispatcher.InvokeAsync(() => ReadRdpWarmupSnapshot(session), DispatcherPriority.Background).Task;

        private Task<RdpWarmupSnapshot> ReadLegacyRdpWarmupSnapshotAsync() =>
            Dispatcher.InvokeAsync(ReadLegacyRdpWarmupSnapshot, DispatcherPriority.Background).Task;

        private async Task RunOptionalRdpCaptureProbeAsync(RdpTabSession session, ServerInfo server)
        {
            try { session.ReadinessCts?.Cancel(); } catch { /* ignore */ }
            try { session.ReadinessCts?.Dispose(); } catch { /* ignore */ }
            session.ReadinessCts = new CancellationTokenSource();
            var ct = session.ReadinessCts.Token;

            try
            {
                var captureReady = await session.ReadinessGate.TryCaptureProbeAsync(
                    server.Ip,
                    () => ReadRdpWarmupSnapshotAsync(session),
                    snap => RdpFrameProbe.IsNonBlankFrameAsync(snap, ct),
                    ct).ConfigureAwait(false);

                if (captureReady && session == _activeSession)
                {
                    await Dispatcher.InvokeAsync(
                        () => ResumeAiGuideAfterStableReady(server.Ip),
                        DispatcherPriority.Background);
                }
                else if (!captureReady && session == _activeSession)
                {
                    AppLogger.Warning(
                        "Screen capture probe failed — RDP session remains active.",
                        category: "RDP",
                        action: "CaptureProbe");
                    RdpCleanHostLogger.Log("WARNING Screen capture probe failed — RDP session remains active.");

                    await Dispatcher.InvokeAsync(
                        () =>
                        {
                            if (session.CleanSession?.IsConnected != true)
                                return;

                            session.TabView?.SetCaptureUnavailableStatus();
                        },
                        DispatcherPriority.Background);
                }
            }
            catch (OperationCanceledException)
            {
                session.ReadinessGate.MarkCancelled();
            }
        }

        private async Task RunLegacyOptionalCaptureProbeAsync()
        {
            try { _legacyReadinessCts?.Cancel(); } catch { /* ignore */ }
            try { _legacyReadinessCts?.Dispose(); } catch { /* ignore */ }
            _legacyReadinessCts = new CancellationTokenSource();
            var ct = _legacyReadinessCts.Token;

            try
            {
                var captureReady = await _legacyReadinessGate.TryCaptureProbeAsync(
                    _activeSession?.Server?.Ip,
                    ReadLegacyRdpWarmupSnapshotAsync,
                    snap => RdpFrameProbe.IsNonBlankFrameAsync(snap, ct),
                    ct).ConfigureAwait(false);

                if (captureReady)
                {
                    await Dispatcher.InvokeAsync(
                        () => ResumeAiGuideAfterStableReady(_activeSession?.Server?.Ip),
                        DispatcherPriority.Background);
                }
                else
                {
                    AppLogger.Warning(
                        "Screen capture probe failed — RDP session remains active.",
                        category: "RDP",
                        action: "CaptureProbe");
                    RdpCleanHostLogger.Log("WARNING Screen capture probe failed — RDP session remains active.");

                    await Dispatcher.InvokeAsync(
                        () => GetActiveTabView()?.SetCaptureUnavailableStatus(),
                        DispatcherPriority.Background);
                }
            }
            catch (OperationCanceledException)
            {
                _legacyReadinessGate.MarkCancelled();
            }
        }

        private void CompleteRdpSessionReady(RdpTabSession session, ServerInfo server)
        {
            ApplySidebarWidthToActiveSession();
            ApplyRdpViewportSizeNow();
            UpdateConnectionStatus("Bağlandı.", true);
            GetActiveTabView()?.HideConnecting();
            GetActiveTabView()?.SetConnectedStatus();
            HideRdpFailureState();

            session.CleanSession?.EnsureHostFillsParent();
            SyncRdpHostChildBounds();
            GetActiveTabView()?.LogSizingDiagnostics();

            UpdateRdpToolbarState();
        }

        private RdpWarmupSnapshot ReadRdpWarmupSnapshot(RdpTabSession session)
        {
            var mgr = session.CleanSession;
            var (width, height) = GetRdpHostPanelPixelSizeFallback();
            if (width < 200 || height < 200)
            {
                width = (int)Math.Max(200, RdpContentGrid?.ActualWidth ?? 1024);
                height = (int)Math.Max(200, RdpContentGrid?.ActualHeight ?? 768);
            }

            var hasFocus = false;
            try { hasFocus = GetActiveRdpHostControl()?.ContainsFocus == true || RdpHost?.IsKeyboardFocused == true; } catch { /* ignore */ }

            nint hostHandle = 0;
            try { hostHandle = CaptureTargetResolver.SafeGetWinFormsHandle(GetActiveRdpHostControl()); } catch { /* ignore */ }

            var isVisible = RdpHost?.Visibility == Visibility.Visible
                && RdpContentGrid?.IsVisible == true
                && width >= 200
                && height >= 200;

            return new RdpWarmupSnapshot
            {
                IsConnected = mgr?.IsConnected == true,
                State = mgr?.SessionState ?? RdpState.Disconnected,
                ClientState = mgr?.TryGetClientState(),
                Width = width,
                Height = height,
                IsVisible = isVisible,
                HasHostFocus = hasFocus,
                HostHandle = hostHandle
            };
        }

        private RdpWarmupSnapshot ReadLegacyRdpWarmupSnapshot()
        {
            var width = (int)Math.Max(0, RdpContentGrid?.ActualWidth ?? 0);
            var height = (int)Math.Max(0, RdpContentGrid?.ActualHeight ?? 0);
            nint hostHandle = 0;
            try { hostHandle = CaptureTargetResolver.SafeGetWinFormsHandle(GetActiveRdpHostControl()); } catch { /* ignore */ }

            return new RdpWarmupSnapshot
            {
                IsConnected = _activeSession?.CleanSession?.IsConnected == true,
                State = _activeSession?.CleanSession?.SessionState ?? RdpState.Disconnected,
                ClientState = _activeSession?.CleanSession?.TryGetClientState(),
                Width = width,
                Height = height,
                IsVisible = RdpHost?.Visibility == Visibility.Visible && width >= 200 && height >= 200,
                HasHostFocus = RdpHost?.IsKeyboardFocused == true,
                HostHandle = hostHandle
            };
        }

        private bool IsRdpStableReady() =>
            _activeSession?.ReadinessGate.IsStableReady ?? false;

        private bool ShouldBlockAiGuideForRdp()
        {
            if (!HasRdpViewportForCapture())
                return false;

            if (_aiGuidePausedForRdpConnection || IsAnyRdpConnecting())
                return true;

            var gate = _activeSession?.ReadinessGate ?? _legacyReadinessGate;
            var connected = _activeSession?.CleanSession?.IsConnected ?? false;

            if (!connected)
                return gate.Phase == RdpReadinessPhase.Connecting;

            return !gate.IsReadyForCapture;
        }

        private bool IsRdpWarmingUp() =>
            (_activeSession?.ReadinessGate.IsWarmingUp ?? false)
            || _legacyReadinessGate.IsWarmingUp
            || _aiGuidePausedForRdpConnection;

        private bool TryBeginAiGuideCaptureOrNotify()
        {
            if (!IsAiGuideEntitled)
            {
                ShowPremiumPlusRequiredPrompt();
                return false;
            }

            if (ShouldBlockAiGuideForRdp())
            {
                if (_aiGuidePausedForRdpConnection || IsAnyRdpConnecting())
                {
                    AIGuideAnalysisLogger.CaptureSkippedBecauseRdpNotStable("paused-or-connecting");
                    if (_isAiPanelOpen)
                        UpdateAiGuideStatus(AIGuideLanguage.AiGuidePausedForConnection, "#FBBF24", string.Empty);
                    else
                        ShowToast(AIGuideLanguage.AiGuidePausedForConnection, "\uE946", 4000);
                }
                else
                {
                    AIGuideAnalysisLogger.CaptureSkippedBecauseRdpNotStable("not-stable");
                    ShowToast(AIGuideLanguage.RdpLoadingMessage, "\uE946", 4000);
                }

                return false;
            }

            _ = BeginAiGuideCaptureAsync();
            return true;
        }

        private bool IsAnyRdpConnecting() =>
            _rdpSessions.Any(s => s.CleanSession?.ConnectionState.IsInProgress() == true);

        private bool IsRdpReadyForCapture()
        {
            if (HasRdpViewportForCapture() && _activeSession != null)
            {
                return _activeSession.CleanSession?.IsReadyForCapture == true
                    && _activeSession.ReadinessGate.IsStableReady;
            }

            return false;
        }

        private (bool IsConnected, bool IsReady, Rdp.RdpState State) GetRdpCaptureReadiness()
        {
            var mgr = _activeSession?.CleanSession;
            if (mgr == null)
                return (false, false, Rdp.RdpState.Disconnected);

            var gateReady = _activeSession?.ReadinessGate.IsStableReady
                ?? _legacyReadinessGate.IsStableReady;
            return (mgr.IsConnected, mgr.IsReadyForCapture && gateReady, mgr.SessionState);
        }

        private nint GetRdpHostHandleSafe()
        {
            var clientHandle = CaptureTargetResolver.SafeGetWinFormsHandle(GetActiveRdpHostControl());
            if (clientHandle != 0) return clientHandle;

            if (RdpHost?.IsLoaded == true
                && PresentationSource.FromVisual(RdpHost) is HwndSource hostSrc)
                return hostSrc.Handle;

            return RdpHost?.Handle ?? nint.Zero;
        }

        private bool HasRdpViewportForCapture() =>
            (_activeSession != null || IsAnyRdpConnecting())
            && RdpHost != null
            && RdpHost.Visibility == Visibility.Visible;

        private ResolvedCaptureTarget ResolveAiGuideCaptureTargetSnapshot()
        {
            var server = _activeSession?.Server ?? _selectedServer ?? _selectedNode?.Server;
            var serverKey = server != null ? GetServerKey(server) : null;

            return CaptureTargetResolver.Resolve(
                HasRdpViewportForCapture,
                IsAnyRdpConnecting,
                GetRdpCaptureReadiness,
                GetRdpHostHandleSafe,
                RdpContentGrid,
                RdpContentGrid,
                SelectedServerPanel,
                RdpColumnBorder,
                RdpHost,
                this,
                server,
                serverKey);
        }

        private async Task FinalizeConnectAuditAsync(RdpTabSession session, Task connectTask)
        {
            try
            {
                await connectTask.ConfigureAwait(true);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (session.ConnectAudit == null)
                        return;

                    if (session.CleanSession?.IsConnected == true)
                        return;

                    if (session.CleanSession?.IsConnecting == true)
                        return;

                    ShowRdpConnectionFailedState("Bağlantı kurulamadı.");
                    session.ConnectAudit.CompleteFailed("Connection attempt completed without success");
                    session.ConnectAudit = null;

                    if (session.IsQuickConnect)
                    {
                        System.Windows.MessageBox.Show(
                            "Bağlantı kurulamadı. Host/IP, kullanıcı adı ve şifreyi kontrol edin.",
                            "Quick Connect Başarısız",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                });
            }
            catch (Exception)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowRdpConnectionFailedState("Sunucuya bağlanılamadı. Sunucu adresini ve ağ bağlantısını kontrol edin.");
                    session.ConnectAudit?.CompleteFailed("RDP bağlantısı kurulamadı.");
                    session.ConnectAudit = null;

                    if (session.IsQuickConnect)
                    {
                        System.Windows.MessageBox.Show(
                            "Sunucuya bağlanılamadı. Sunucu adresini ve ağ bağlantısını kontrol edin.",
                            "Quick Connect Başarısız",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                });
            }
        }







        // --- Tab bar management ---

        private void RefreshTabBar()
        {
            try
            {
                TabBarPanel.Children.Clear();

                if (_rdpSessions.Count == 0)
                {
                    TabBarBorder.Visibility = Visibility.Collapsed;
                    return;
                }

                TabBarBorder.Visibility = Visibility.Visible;

                foreach (var session in _rdpSessions)
                {
                    var isActive = session == _activeSession;
                    var tabLabel = string.IsNullOrWhiteSpace(session.Server.Name) ? session.Server.Ip : session.Server.Name;

                    // Tab container
                    var tabBorder = new Border
                    {
                        Background = isActive
                            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B))
                            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0E, 0x14, 0x20)),
                        BorderBrush = isActive
                            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6C, 0x8F, 0xF0))
                            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x26, 0x34)),
                        BorderThickness = new Thickness(1, 0, 1, isActive ? 2 : 0),
                        CornerRadius = new CornerRadius(6, 6, 0, 0),
                        Padding = new Thickness(10, 6, 8, 6),
                        Margin = new Thickness(2, 4, 2, 0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = tabLabel
                    };
                    session.TabElement = tabBorder;

                    var tabStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

                    // Status dot
                    var connState = session.CleanSession?.ConnectionState ?? RdpConnectionState.Idle;
                    var dotColor = connState switch
                    {
                        RdpConnectionState.LoginComplete => System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81),
                        RdpConnectionState.Connecting or RdpConnectionState.Connected => System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B),
                        RdpConnectionState.Failed or RdpConnectionState.TimedOut => System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44),
                        _ => System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)
                    };
                    var statusDot = new System.Windows.Shapes.Ellipse
                    {
                        Width = 7, Height = 7,
                        Fill = new SolidColorBrush(dotColor),
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    tabStack.Children.Add(statusDot);

                    // Server name
                    var nameText = new TextBlock
                    {
                        Text = tabLabel,
                        Foreground = isActive
                            ? System.Windows.Media.Brushes.White
                            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA7)),
                        FontSize = 11,
                        FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center,
                        MaxWidth = 120,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    tabStack.Children.Add(nameText);

                    // Close button
                    var closeBtn = new System.Windows.Controls.Button
                    {
                        Content = "✕",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA7)),
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(4, 0, 2, 0),
                        Margin = new Thickness(6, 0, 0, 0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center,
                        Tag = session
                    };
                    closeBtn.Click += OnTabCloseClick;
                    tabStack.Children.Add(closeBtn);

                    tabBorder.Child = tabStack;

                    // Click to switch
                    var capturedSession = session;
                    tabBorder.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.OriginalSource is System.Windows.Controls.Button) return;
                        SwitchToTab(capturedSession);
                    };

                    TabBarPanel.Children.Add(tabBorder);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void OnTabCloseClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.Tag is RdpTabSession session)
                {
                    CloseTabSession(session);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void SwitchToTab(RdpTabSession session)
        {
            try
            {
                if (!_rdpSessions.Contains(session)) return;

                PauseBackgroundScans();
                CancelAiGuideAnalysisInFlight();
                _aiGuideService?.InvalidateScreenContext("rdp-tab-switch");

                _activeSession = session;
                _pendingServer = session.Server;

                ShowActiveTabSessionView();
                ShowRdpSessionWorkspace();
                if (SelectedServerPanel != null) SelectedServerPanel.Visibility = Visibility.Collapsed;
                if (RdpPlaceholder != null) RdpPlaceholder.Visibility = Visibility.Collapsed;

                RefreshTabBar();

                // Preserve framebuffer — only show panel, no reconnect
                if (session.CleanSession?.IsConnected == true)
                {
                    Dispatcher.BeginInvoke(ApplyRdpViewportSizeNow, DispatcherPriority.Render);
                }

                // Update status display
                if (session.CleanSession != null)
                {
                    if (session.CleanSession.IsConnecting)
                        ShowRdpConnectingState("Bağlanıyor...");
                    else if (session.CleanSession.IsConnected)
                        UpdateConnectionStatus("Bağlandı.", true);
                    else
                        UpdateConnectionStatus("Bağlanıyor...", false);
                }

                UpdateRdpToolbarState();

                if (_isAiPanelOpen && ShouldBlockAiGuideForRdp())
                {
                    SetAiGuideInputEnabled(false, AIGuideLanguage.RdpLoadingMessage);
                    UpdateAiGuideStatus(AIGuideLanguage.AiGuidePausedForConnection, "#FBBF24", string.Empty);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void CloseTabSession(RdpTabSession session)
        {
            CancelAiGuideAnalysisInFlight();
            try { session.ReadinessCts?.Cancel(); } catch { /* ignore */ }
            try { session.ReadinessCts?.Dispose(); } catch { /* ignore */ }
            session.ReadinessCts = null;
            try { (session.ContentViewModel as RdpSessionViewModel)?.CancelAutomaticRetry(); } catch { /* ignore */ }
            try { session.ConnectionCts?.Cancel(); } catch { /* ignore */ }
            try { session.ConnectionCts?.Dispose(); } catch { /* ignore */ }
            session.ConnectionCts = null;
            session.ReadinessGate.Reset();
            using var audit = AuditOperationScope.BeginRdp(AuditActions.Disconnect, session.Server, session.Id);
            try
            {
                session.ConnectAudit?.CompleteCancelled("Session closed before connect completed");
                session.ConnectAudit = null;

                if (session.ContentViewModel is RdpSessionViewModel sessionVm)
                    sessionVm.Dispose();
                else
                    session.CleanSession?.Dispose();

                session.TabView?.DisposeHostResources();

                _rdpSessions.Remove(session);
                RemoveTabSessionView(session);

                if (_activeSession == session)
                {
                    _activeSession = _rdpSessions.LastOrDefault();
                    if (_activeSession != null)
                    {
                        SwitchToTab(_activeSession);
                    }
                    else
                    {
                        _pendingServer = null;
                        HideRdpSessionWorkspace();
                        ApplyActiveWorkspaceContent();
                        UpdateConnectionStatus("RDP bağlantısı yok.", false);
                        if (_rdpSessions.Count == 0)
                            ApplyRdpFullViewportChrome(active: false);
                    }
                }
                else
                {
                    RefreshTabBar();
                }

                audit.CompleteSuccess();
                if (_activeSession == null)
                {
                    LoggingContext.ClearSession();
                    ResumeBackgroundScans();
                    var server = _selectedNode?.Server ?? _activeDashboardServer;
                    if (server != null)
                        ActivateDashboardForServer(server);
                    else
                        UpdateConnectionStatus("RDP bağlantısı yok.", false);
                }

                if (session.Server?.IsEphemeral == true)
                {
                    _ephemeralServers.RemoveAll(s =>
                        ReferenceEquals(s, session.Server) ||
                        (s.IsEphemeral &&
                         string.Equals(s.Ip, session.Server.Ip, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(s.Username, session.Server.Username, StringComparison.OrdinalIgnoreCase)));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to close RDP tab session.", ex, category: "RDP", action: "Disconnect");
                audit.CompleteFailed(ex.Message, ex);
            }
        }


        private System.Windows.Forms.Control GetRdpUiAnchor()
        {
            if (_rdpUiAnchor == null)
            {
                _rdpUiAnchor = new System.Windows.Forms.Control();
                if (!_rdpUiAnchor.IsHandleCreated)
                    _ = _rdpUiAnchor.Handle;
            }

            return _rdpUiAnchor;
        }

        private RdpSessionHostControl? GetActiveRdpHostControl() => GetActiveTabView()?.HostControl;

        private bool TryGetRdpHostPixelSize(out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                RdpContentGrid?.UpdateLayout();
                RdpHost?.UpdateLayout();

                if (RdpHost == null || !RdpHost.IsLoaded)
                    return false;

                if (RdpHost.ActualWidth < 1 || RdpHost.ActualHeight < 1)
                    return false;

                if (PresentationSource.FromVisual(RdpHost) is HwndSource hostSrc
                    && GetClientRect(hostSrc.Handle, out var hostRc))
                {
                    width = Math.Max(0, hostRc.Right - hostRc.Left);
                    height = Math.Max(0, hostRc.Bottom - hostRc.Top);
                    if (width > 0 && height > 0)
                        return true;
                }

                width = (int)Math.Max(RdpHostLayoutSnapshot.MinHostWidth, RdpHost.ActualWidth);
                height = (int)Math.Max(RdpHostLayoutSnapshot.MinHostHeight, RdpHost.ActualHeight);
                return width > 0 && height > 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("RDP host pixel size read failed.", ex);
            }

            return false;
        }

        private async Task<(int W, int H)?> WaitForRdpHostPixelSizeAsync(int maxAttempts = 100)
        {
            var view = GetActiveTabView();
            if (view == null)
                return null;

            await view.PrepareHostForConnectAsync(Math.Max(5000, maxAttempts * 50)).ConfigureAwait(true);
            var host = view.HostControl;
            if (host.Width > 0 && host.Height > 0)
                return (host.Width, host.Height);

            return null;
        }

        private (int W, int H) GetRdpConnectSize()
        {
            if (TryGetRdpHostPixelSize(out var w, out var h))
                return (w, h);

            var (wDip, hDip) = GetRdpViewportDipSize();
            return (
                (int)Math.Max(RdpHostLayoutSnapshot.MinHostWidth, wDip > 50 ? wDip : RdpContentGrid?.ActualWidth ?? 1024),
                (int)Math.Max(RdpHostLayoutSnapshot.MinHostHeight, hDip > 50 ? hDip : RdpContentGrid?.ActualHeight ?? 640));
        }

        private (int W, int H) GetRdpHostPanelPixelSizeFallback(int fallbackW = 0, int fallbackH = 0)
        {
            var (w, h) = GetRdpConnectSize();
            if (w >= 200 && h >= 200)
                return (w, h);

            if (fallbackW >= 200 && fallbackH >= 200)
                return (fallbackW, fallbackH);

            return (1024, 768);
        }

        public MainWindow() : this(AccountComposition.Current)
        {
        }

        public MainWindow(IAccountService? accountService)
        {
            _accountService = accountService;
            _shutdownPersistence = new ShutdownPersistenceCoordinator(
                FlushShutdownPersistenceAsync,
                TimeSpan.FromSeconds(15));
            var session = _accountService?.CurrentSession;
            FeatureEntitlementService.Instance.SetAccountAuthenticated(
                session is not null, session?.User.PlanCode);
            // RDP bileşeni için default donanım hızlandırmayı kullan. SoftwareOnly bazı modellerde beyaz ekrana neden olabiliyor.

            InitializeComponent();
            UpdateNotificationIndicator();
            InitializeHeaderIdentity();
            _liveRefreshIndicator = new LiveRefreshIndicatorController(
                LiveRefreshIndicatorRoot,
                LiveRefreshStatusText,
                LiveRefreshProgressFill,
                LiveRefreshProgressTrack);
            _refreshLifecycle = new DashboardRefreshLifecycle(_liveRefreshIndicator);
            try
            {
                PerformanceCanvas.SizeChanged += (_, _) => UpdatePerformanceChart();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            SettingsService.Instance.SettingsChanged += OnAppSettingsChanged;
            StateChanged += MainWindow_StateChanged;
            PreviewMouseMove += (_, _) => RecordUserActivity();
            PreviewKeyDown += (_, _) => RecordUserActivity();
            InitializeWelcomeOverlay();
            InitializeRdpToolbar();
            AiPanelBorder.Visibility = Visibility.Collapsed;
            
            this.SourceInitialized += OnSourceInitialized;
            this.Loaded += MainWindow_Loaded;

            if (AiWidgetLockOverlay != null)
                AiWidgetLockOverlay.ViewPlansRequested += (_, _) => OpenPlansWindow();
            if (AiExpandedAnalysisConsole != null)
                AiExpandedAnalysisConsole.ViewPlansRequested += (_, _) => OpenPlansWindow();
            
            this.SourceInitialized += (s, e) => {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null) {
                    // SoftwareOnly RDP ActiveX ile beyaz ekran sorununa yol açabiliyor.
                    // Sadece gerekirse (örn. GPU glitch) açılmalı.
                    source.CompositionTarget.RenderMode = RenderMode.Default;
                }
            };

            try
            {
                SessionsGrid.ItemsSource = _sessionRows;
                ServicesGrid.ItemsSource = _serviceRows;
                ProcessesGrid.ItemsSource = _processRows;
                OpenPortsGrid.ItemsSource = _openPortRows;
                TimelineItemsControl.ItemsSource = _currentTimelinePageItems;
                RefreshServicesFilter();
                RefreshOpenPortsFilter();
                SetTimelineEmptyState("Select a server to load the Windows Event Log timeline.");
                IpAddressesList.ItemsSource = _ipAddresses;
                SmartInsightList.ItemsSource = _smartInsights;
                ConnectionsTree.ItemsSource = _connectionTree;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            GlobalExceptionHandler.Observe(
                LoadConnectionsAsync(),
                "Storage.LoadConnections",
                notifyUser: true,
                userMessage: "Kay\u0131tl\u0131 ba\u011flant\u0131lar y\u00fcklenemedi. Ayr\u0131nt\u0131lar logland\u0131.");
            UpdateConnectionStatus("RDP bağlantısı yok.", false);
            if (RdpContentGrid != null)
            {
                RdpContentGrid.SizeChanged += (_, _) => ScheduleRdpResize();
            }
            SourceInitialized += OnSourceInitialized;
            RestartSidebarStatusTimer();

            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var src = HwndSource.FromHwnd(hwnd);
                    src?.AddHook(WndProc);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            // Adaptive visibility tracking removed (field was unused)

            Activated += (s, e) =>
            {
                if (_activeSession?.CleanSession != null)
                {
                    // Focus logic for ActiveX if needed
                }
            };
        }

        public void RequestApplicationShutdown()
        {
            if (Interlocked.Exchange(ref _applicationShutdownRequested, 1) != 0)
                return;

            _forceApplicationShutdown = true;
            if (Dispatcher.CheckAccess())
                Close();
            else
                _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Normal);
        }

        private void PrepareForApplicationShutdown()
        {
            try { StopConnectionMonitor(); } catch { }
            try { _connectionTimer?.Stop(); } catch { }
            try { _sidebarStatusTimer?.Stop(); } catch { }
            try { _statusBarTimer?.Stop(); } catch { }
            try { _sessionIdleTimer?.Stop(); } catch { }
            try { _insightsTimer?.Stop(); } catch { }
            try { _rdpSizingDebounce?.Stop(); } catch { }
            try { _floatingToolbarHideTimer?.Stop(); } catch { }
            try { _chromeAnimateTimer?.Stop(); } catch { }
            try { _insightsCts?.Cancel(); } catch { }
            try { _backgroundScanCts.Cancel(); } catch { }
            try { _aiGuideCaptureCts?.Cancel(); } catch { }
            try { _aiGuideGuidanceCts?.Cancel(); } catch { }
            try { _deferredDeepCts?.Cancel(); } catch { }

            foreach (var session in _rdpSessions.ToList())
            {
                try { session.ContentViewModel?.CancelAutomaticRetry(); } catch { }
                try { session.ConnectionCts?.Cancel(); } catch { }
                try { session.ReadinessCts?.Cancel(); } catch { }
            }
        }
        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_shutdownCloseApproved)
                return;

            var settings = SettingsService.Instance.Current;
            if (!_forceApplicationShutdown && !_accountLogoutInProgress && settings.CloseToTray && _trayService != null && e.Cancel == false)
            {
                e.Cancel = true;
                _trayService.HideToTray();
                return;
            }

            // WPF does not await async event handlers. Cancel before yielding, then
            // issue one final Close after the bounded persistence operation completes.
            Interlocked.Exchange(ref _applicationShutdownRequested, 1);
            PrepareForApplicationShutdown();
            e.Cancel = true;
            if (_shutdownCloseInProgress)
                return;

            _shutdownCloseInProgress = true;
            try
            {
                var result = await _shutdownPersistence.RunOnceAsync();
                if (result.Status == ShutdownPersistenceStatus.TimedOut)
                {
                    AppLogger.Warning(
                        "Kapanış verileri 15 saniye içinde kaydedilemedi; uygulama kapatılmaya devam edecek.",
                        result.Exception,
                        category: "Storage",
                        action: "Shutdown");
                }
                else if (result.Status == ShutdownPersistenceStatus.Failed)
                {
                    AppLogger.Error(
                        "Kapanış verileri kaydedilemedi; uygulama kapatılmaya devam edecek.",
                        result.Exception,
                        category: "Storage",
                        action: "Shutdown");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Kapanış verileri kaydedilirken beklenmeyen bir hata oluştu.", ex, category: "Storage", action: "Shutdown");
            }
            finally
            {
                _shutdownCloseApproved = true;
                _shutdownCloseInProgress = false;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { Close(); } catch { }
                    if (Interlocked.Exchange(ref _applicationShutdownIssued, 1) == 0)
                    {
                        try
                        {
                            if (global::System.Windows.Application.Current is App app)
                                app.RequestApplicationShutdown();
                            else
                                global::System.Windows.Application.Current.Shutdown();
                        }
                        catch { }
                    }
                }), DispatcherPriority.Normal);
            }
        }

        private async Task FlushShutdownPersistenceAsync()
        {
            // Layout collection reads UI-owned state, so start it before the first
            // asynchronous storage operation can move this pipeline off the UI thread.
            if (_dashboardBuilder != null)
                await _dashboardBuilder.FlushLayoutAsync().ConfigureAwait(false);

            await StorageService.FlushConnectionsOnShutdownAsync(
                    _servers,
                    _explicitFolders,
                    _serverHistory,
                    _favoriteServerKeys)
                .ConfigureAwait(false);
            await SettingsService.Instance.SaveAsync().ConfigureAwait(false);
            await PersistentSnapshotStore.FlushAsync().ConfigureAwait(false);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            if (WindowState == WindowState.Minimized && settings.MinimizeToTray && _trayService != null)
                _trayService.HideToTray();

            if (WindowState != WindowState.Minimized)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    GetActiveTabView()?.HandleWindowStateChanged();
                }, DispatcherPriority.Render);
            }
        }

        private void RecordUserActivity() => _lastUserActivityUtc = DateTime.UtcNow;

        private void OnAppSettingsChanged(object? sender, AppSettings settings)
        {
            AuditLogService.Log(
                AuditActions.SettingsChange,
                AuditCategories.Configuration,
                AuditResults.Success,
                LogLevels.Information,
                message: "Application settings changed.");
            void ApplySettings()
            {
                ApplyAppSettings(settings);
                SaveConnectionsToDisk();
            }

            if (Dispatcher.CheckAccess())
                ApplySettings();
            else
                Dispatcher.BeginInvoke((Action)ApplySettings, DispatcherPriority.Normal);
        }

        public void ApplyAppSettings(AppSettings settings)
        {
            try
            {
                SettingsApplicator.ApplyAll(this, settings);
                Title = settings.ApplicationName;
                RestartSidebarStatusTimer();
                RestartSessionIdleTimer();
                SettingsApplicator.EnforceCacheSizeLimit(settings);
                _rdpToolbarVm.IsPremium = IsPremiumUser;
                ApplyStatusAnimations(settings.ShowStatusAnimations && settings.AnimationsEnabled);
                ApplyMonitoringVisibility(settings);
                ApplyAiGuideLanguageUi(refreshContent: true);

                if (_insightsTimer != null)
                    _insightsTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, settings.DefaultRefreshIntervalSeconds));

                if (!settings.EnableLiveMonitoring || !settings.EnableBackgroundTasks)
                    StopInsightsRefresh();
                else if (_selectedNode?.Server != null && !HasAnyActiveSession())
                    StartInsightsRefresh(_selectedNode.Server);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to apply settings: {ex.Message}");
            }
        }

        public void OpenSettingsWindow(SettingsCategory? initialCategory = null)
        {
            var vm = new SettingsViewModel(
                msg => ShowToast(msg, "\uE73E"),
                msg => System.Windows.MessageBox.Show(msg, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes,
                ApplyAppSettings,
                ExportConnectionsFromSettingsAsync,
                ImportConnectionsFromSettingsAsync);
            if (initialCategory.HasValue)
                vm.SelectedCategory = initialCategory.Value;

            var dialog = new SettingsWindow(vm) { Owner = this };
            dialog.ShowDialog();
            ApplyAppSettings(SettingsService.Instance.Current);
        }

        public void OpenPlansWindow()
        {
            if (System.Windows.Application.Current is App app &&
                !app.EnsureSignedInForAccountAction(this))
                return;

            var dialog = new PlansWindow { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                UpdateLicensingUi();
                EnforceAiGuideLicenseState();
            }
        }

        public async Task<(bool Succeeded, string Message)> ActivateAccountPlanAsync(LicensingPlanTier plan)
        {
            if (_accountService is null || _accountService.CurrentSession is null)
                return (false, "Planı etkinleştirmek için önce giriş yapın.");

            var result = await _accountService.ActivatePlanAsync(plan);
            if (!result.Succeeded)
                return (false, result.ErrorMessage ?? "Plan SQL veritabanında etkinleştirilemedi.");

            SetAccountService(_accountService);
            return (true, $"Planınız {result.Value!.PlanDisplayName} olarak güncellendi.");
        }

        public async Task<(bool Succeeded, string Message)> RedeemActivationCodeAsync(string activationCode)
        {
            if (_accountService is null || _accountService.CurrentSession is null)
                return (false, "Aktivasyon kodu için önce giriş yapın.");

            var result = await _accountService.RedeemActivationTokenAsync(activationCode);
            if (!result.Succeeded)
                return (false, result.ErrorMessage ?? "Aktivasyon kodu kullanılamadı.");

            SetAccountService(_accountService);
            return (true, $"Planınız {result.Value!.PlanDisplayName} olarak etkinleştirildi.");
        }

        private void OnOpenPlansClick(object sender, RoutedEventArgs e) => OpenPlansWindow();

        private void OnOpenAdminConsoleClick(object sender, RoutedEventArgs e)
        {
            if (_accountService?.CurrentSession?.User.IsAdmin != true)
            {
                ShowToast("Yönetici yetkisi gerekli.", "\uE72E");
                return;
            }

            new AdminWindow(_accountService) { Owner = this }.ShowDialog();
        }

        private void UpdateLicensingUi()
        {
            var locked = !FeatureEntitlementService.Instance.IsPremiumEnabled;
            if (AiWidgetLockOverlay != null)
                AiWidgetLockOverlay.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            if (AiWidgetContentHost != null)
                AiWidgetContentHost.Effect = locked ? new BlurEffect { Radius = 8, RenderingBias = RenderingBias.Quality } : null;
            AiExpandedAnalysisConsole?.RefreshLicensingUi();
        }

        private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => OpenSettingsWindow();

        private async void OnQuickExportConfigClick()
        {
            using var audit = AuditOperationScope.BeginDataTransfer(AuditActions.Export);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "netvium-settings.json"
            };
            if (dlg.ShowDialog() != true)
            {
                audit.CompleteCancelled("Export dialog cancelled");
                return;
            }

            try
            {
                await SettingsService.Instance.ExportAsync(dlg.FileName);
                audit.CompleteSuccess();
                ShowToast("Configuration exported.", "\uE74D");
            }
            catch (Exception ex)
            {
                audit.CompleteFailed(ex.Message, ex);
                ShowToast("Export failed.", "⚠️");
            }
        }

        private async void OnQuickImportConfigClick()
        {
            using var audit = AuditOperationScope.BeginDataTransfer(AuditActions.Import);
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
            if (dlg.ShowDialog() != true)
            {
                audit.CompleteCancelled("Import dialog cancelled");
                return;
            }

            try
            {
                var merge = System.Windows.MessageBox.Show(
                    "Merge with existing settings?",
                    "Import",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;
                await SettingsService.Instance.ImportAsync(dlg.FileName, merge);
                ApplyAppSettings(SettingsService.Instance.Current);
                audit.CompleteSuccess();
                ShowToast("Configuration imported.", "\uE8B7");
            }
            catch (Exception ex)
            {
                audit.CompleteFailed(ex.Message, ex);
                ShowToast("Import failed.", "⚠️");
            }
        }

        private async Task PersistLastConnectedServerAsync(ServerInfo server)
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.RememberLastConnectedServer)
                return;

            var port = server.Port <= 0 ? 3389 : server.Port;
            settings.LastConnectedServerKey = $"{server.Ip}:{port}";
            await SettingsService.Instance.SaveAsync(settings);
        }

        private void StartSidebarStatusTimer() => RestartSidebarStatusTimer();

        private void RestartSidebarStatusTimer()
        {
            try
            {
                _sidebarStatusTimer?.Stop();
                var interval = Math.Max(5, SettingsService.Instance.Current.PingIntervalSeconds);
                _sidebarStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
                _sidebarStatusTimer.Tick += async (_, _) => await RefreshAllSidebarStatusesAsync();
                _sidebarStatusTimer.Start();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void RestartSessionIdleTimer()
        {
            try
            {
                _sessionIdleTimer?.Stop();
                _sessionIdleTimer = null;
                var settings = SettingsService.Instance.Current;
                if (!settings.AutoLogout || settings.SessionTimeoutMinutes <= 0)
                    return;

                _sessionIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                _sessionIdleTimer.Tick += (_, _) =>
                {
                    var idle = DateTime.UtcNow - _lastUserActivityUtc;
                    if (idle.TotalMinutes < settings.SessionTimeoutMinutes)
                        return;

                    AppLogger.Audit("Session idle timeout — closing RDP sessions.");
                    foreach (var session in _rdpSessions.ToList())
                    {
                        try { CloseTabSession(session); } catch (Exception ex) { AppLogger.Error("Idle timeout tab close failed.", ex); }
                    }
                    _sessionIdleTimer?.Stop();
                };
                _sessionIdleTimer.Start();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private string GetServerKey(ServerInfo server) => ServerSnapshotKey.FromServer(server);

        private (double W, double H) GetRdpViewportDipSize()
        {
            try
            {
                RdpContentGrid?.UpdateLayout();
                RdpHost?.UpdateLayout();

                var w = RdpContentGrid?.ActualWidth ?? 0;
                var h = RdpContentGrid?.ActualHeight ?? 0;
                if (w >= 50 && h >= 50)
                    return (w, h);

                w = RdpHost?.ActualWidth ?? 0;
                h = RdpHost?.ActualHeight ?? 0;
                return (w, h);
            }
            catch (Exception ex)
            {
                AppLogger.Error("RDP viewport size read failed.", ex);
                return (RdpHost?.ActualWidth ?? 0, RdpHost?.ActualHeight ?? 0);
            }
        }


        private void ScheduleRdpResize()
        {
            if (_activeSession?.CleanSession == null) return;
            if (_suspendRdpSizing && !_isResizingRdpChrome) return;

            if (_isResizingRdpChrome)
            {
                ApplyRdpViewportSizeNow();
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastRdpResizeUtc).TotalMilliseconds < RdpResizeThrottleMs)
            {
                _rdpSizingDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RdpResizeThrottleMs) };
                _rdpSizingDebounce.Stop();
                _rdpSizingDebounce.Tick -= OnRdpSizingDebounceTick;
                _rdpSizingDebounce.Tick += OnRdpSizingDebounceTick;
                _rdpSizingDebounce.Start();
                return;
            }

            ApplyRdpViewportSizeNow();
        }

        private void ApplyRdpViewportSizeNow()
        {
            if (_activeSession?.CleanSession == null) return;
            if (_suspendRdpSizing && !_isResizingRdpChrome) return;

            try
            {
                var manager = _activeSession?.CleanSession;
                if (manager == null) return;
                if (manager.IsConnecting) return;

                _lastRdpResizeUtc = DateTime.UtcNow;
                ApplySidebarWidthToActiveSession();
                SyncRdpHostChildBounds();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void ApplySidebarWidthToActiveSession()
        {
            if (_activeSession?.ContentViewModel is RdpSessionViewModel vm)
                vm.ApplySidebarWidth(LeftSidebarColumn?.ActualWidth ?? 0);
        }

        private void SyncRdpHostChildBounds()
        {
            try
            {
                var updateRemote = _activeSession?.CleanSession?.IsConnected == true;
                GetActiveTabView()?.ApplyLayoutResize(updateSessionDisplay: updateRemote);
            }
            catch (Exception ex) { AppLogger.Error("RDP host bounds sync failed.", ex); }
        }


        private void OnRdpSizingDebounceTick(object? sender, EventArgs e)
    {
        if (_rdpSizingDebounce == null) return;
        _rdpSizingDebounce.Stop();
        if (_suspendRdpSizing) return;
        ApplyRdpViewportSizeNow();
    }    

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                if (PresentationSource.FromVisual(this) is HwndSource src)
                {
                    src.AddHook(WndProc);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_ENTERSIZEMOVE = 0x0231;
            const int WM_EXITSIZEMOVE = 0x0232;
            const int WM_DPICHANGED = 0x02E0;
            const int WM_DISPLAYCHANGE = 0x007E;

            try
            {
                if (msg == WM_ENTERSIZEMOVE)
                {
                    _suspendRdpSizing = true;
                    GetActiveTabView()?.BeginInteractiveWindowMove();
                }
                else if (msg == WM_EXITSIZEMOVE)
                {
                    _suspendRdpSizing = false;
                    var tabView = GetActiveTabView();
                    if (tabView != null)
                        tabView.EndInteractiveWindowMove();
                    else
                        ApplyRdpViewportSizeNow();
                }
                else if (msg == WM_DPICHANGED || msg == WM_DISPLAYCHANGE)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        var tabView = GetActiveTabView();
                        if (tabView != null)
                            tabView.HandleDisplayEnvironmentChanged();
                        else
                            ScheduleRdpResize();
                    }, DispatcherPriority.Render);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            return IntPtr.Zero;
        }

        private void InitializeRdpHost()
        {
            _rdpHostInitialized = CentralWorkspaceHost != null;
        }

        private ServerInfo? SelectRememberedServer()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.RememberLastConnectedServer || string.IsNullOrWhiteSpace(settings.LastConnectedServerKey))
                return null;

            return _servers.FirstOrDefault(s =>
            {
                var port = s.Port <= 0 ? 3389 : s.Port;
                return string.Equals($"{s.Ip}:{port}", settings.LastConnectedServerKey, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void OnAddServerClick(object sender, RoutedEventArgs e)
        {
            var nameBox = GetServerNameBox();
            var name = nameBox?.Text?.Trim() ?? string.Empty;
            var ip = ServerIpBox.Text?.Trim() ?? string.Empty;
            var folder = AddFolderBox.Text?.Trim() ?? string.Empty;
            var username = UsernameBox.Text?.Trim() ?? string.Empty;
            var password = PasswordBox.Password ?? string.Empty;

            var defaults = SettingsService.Instance.Current;
            if (defaults.UseDefaultCredentials)
            {
                if (string.IsNullOrWhiteSpace(username))
                    username = defaults.DefaultUsername;
                if (string.IsNullOrWhiteSpace(password))
                    password = CredentialProtector.Unprotect(defaults.DefaultPasswordProtected);
            }

            if (string.IsNullOrWhiteSpace(folder)) folder = "Sahsi";

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                System.Windows.MessageBox.Show("Sunucu adı, IP, kullanıcı adı ve şifre zorunludur.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var server = new ServerInfo(name, ip, username, password, folder: folder);
            _servers.Add(server);
            SaveConnectionsToDisk();
            BuildConnectionTree(selectServer: server);
            ConnectToServer(server);

            if (nameBox != null)
            {
                nameBox.Text = string.Empty;
            }
            ServerIpBox.Text = string.Empty;
            AddFolderBox.Text = string.Empty;
            UsernameBox.Text = string.Empty;
            PasswordBox.Password = string.Empty;
            AddServerPanel.Visibility = Visibility.Collapsed;
            OpenAddPanelButton.Visibility = Visibility.Visible;
        }

        private void OnConnectSelectedClick(object sender, RoutedEventArgs e)
        {
            var server = GetSelectedServer();
            if (server != null)
            {
                ConnectToServer(server);
                return;
            }

            System.Windows.MessageBox.Show("Önce listeden bir sunucu seçin veya Quick Connect kullanın.", "Seçim Yok", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool _syncingQuickConnect;

        private void ClearQuickConnectFields()
        {
            _syncingQuickConnect = true;
            try
            {
                QuickHostHistoryCombo.SelectedIndex = -1;
                QuickHostHistoryCombo.Text = string.Empty;
                QuickUserBox.Text = string.Empty;
                QuickPassBox.Password = string.Empty;
                QuickSaveToListCheckBox.IsChecked = false;
            }
            finally
            {
                _syncingQuickConnect = false;
            }
        }

        private void OnQuickConnectClick(object sender, RoutedEventArgs e)
        {
            var host = GetQuickConnectHost();
            var user = QuickUserBox.Text?.Trim();
            var pass = QuickPassBox.Password;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                System.Windows.MessageBox.Show("Host/IP, kullanıcı adı ve şifre zorunludur.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _ = QuickConnectHistoryService.RecordHostAsync(host);
            _ = LoadQuickConnectHistoryAsync();

            var saveToList = QuickSaveToListCheckBox.IsChecked == true;
            ServerInfo server;

            if (saveToList)
            {
                var existing = _servers.FirstOrDefault(s => string.Equals(s.Ip, host, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    server = new ServerInfo(host, host, user, pass, "Sahsi");
                    _servers.Add(server);
                    BuildConnectionTree(selectServer: server);
                }
                else
                {
                    server = existing;
                    server.Username = user;
                    server.Password = pass;
                }

                SaveConnectionsToDisk();
            }
            else
            {
                server = new ServerInfo(host, host, user, pass, "Sahsi") { IsEphemeral = true };
                _ephemeralServers.Add(server);
            }

            ConnectToServer(server, quickConnect: true);
            ClearQuickConnectFields();
        }

        private string? GetQuickConnectHost()
        {
            var text = QuickHostHistoryCombo.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text) && QuickHostHistoryCombo.SelectedItem is string selected)
                text = selected.Trim();
            return text;
        }

        private async Task LoadQuickConnectHistoryAsync()
        {
            try
            {
                var items = await QuickConnectHistoryService.LoadAsync();
                await Dispatcher.InvokeAsync(() =>
                {
                    _syncingQuickConnect = true;
                    try
                    {
                        QuickHostHistoryCombo.ItemsSource = items;
                        QuickHostHistoryCombo.SelectedIndex = -1;
                        QuickHostHistoryCombo.Text = string.Empty;
                    }
                    finally
                    {
                        _syncingQuickConnect = false;
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load quick connect history.", ex, category: "Storage", action: "QuickConnectHistory");
            }
        }

        private void OnQuickHostHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingQuickConnect)
                return;

            if (QuickHostHistoryCombo.SelectedItem is string host && !string.IsNullOrWhiteSpace(host))
                QuickHostHistoryCombo.Text = host;
        }

        private void OnQuickHostHistoryLostFocus(object sender, RoutedEventArgs e)
        {
            // Editable combo: keep typed host in the text box.
        }

        private void OnFavoriteItemClick(object sender, MouseButtonEventArgs e)
        {
            if (HasOpenRdpTab())
                return;

            if (sender is FrameworkElement fe && fe.DataContext is ConnectionTreeNode node && node.Server != null)
                ActivateDashboardForServer(node.Server);
        }

        private void OnToggleFavoriteFromMenuClick(object sender, RoutedEventArgs e)
        {
            ConnectionTreeNode? node = null;
            if (sender is MenuItem mi)
            {
                var cm = mi.Parent as ContextMenu;
                if (cm?.PlacementTarget is FrameworkElement fe)
                    node = fe.DataContext as ConnectionTreeNode;
            }

            if (node?.Server != null)
                ToggleFavorite(node.Server);
        }

        private async void OnRefreshInsightsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var server = GetSelectedServer();
                if (server != null && _insightsCts != null)
                {
                    AuditLogService.Log(
                        AuditActions.WidgetRefreshed,
                        AuditCategories.Widget,
                        AuditResults.Started,
                        LogLevels.Information,
                        server.Name,
                        server.Ip,
                        message: $"Dashboard widgets refresh requested for {server.Name} ({server.Ip}).");
                    _refreshGeneration = _refreshLifecycle!.Begin(GetServerKey(server));
                    _dashboardState.ActivateServer(GetServerKey(server), _refreshGeneration);
                    ResetLiveChartSession(GetServerKey(server));
                    await RefreshInsightsTick(server, _insightsCts.Token, force: true);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private async void OnMorningCheckClick(object sender, RoutedEventArgs e)
        {
            var server = GetSelectedServer();
            if (server == null) return;

            var localKey = GetServerKey(server);
            var scanCt = _insightsCts?.Token ?? CancellationToken.None;
            var freshSnapshotTask = CollectFreshQuickScanSnapshotAsync(server, scanCt);
            var items  = new List<ScanItem>();
            int checks = 0, risks = 0;

            // 1. Ping Check
            checks++;
            var ping = await TryPingMsAsync(server.Ip, 2000);
            bool pingOk = ping.HasValue;
            items.Add(new ScanItem
            {
                Icon   = pingOk ? "✅" : "⚠️",
                Label  = "PING",
                Detail = pingOk ? $"{ping!.Value}ms — Erişilebilir" : "Başarısız — Sunucu erişilemiyor!",
                IsRisk = !pingOk
            });
            if (!pingOk) risks++;

            // 2. RDP Port Check
            checks++;
            var rdp = await TryTcpOpenAsync(server.Ip, server.Port > 0 ? server.Port : 3389, 2000);
            items.Add(new ScanItem
            {
                Icon   = rdp ? "✅" : "⚠️",
                Label  = "RDP PORT",
                Detail = rdp ? "Port açık" : "Port kapalı — Bağlantı mümkün değil!",
                IsRisk = !rdp
            });
            if (!rdp) risks++;

            // 3. Disk Check
            checks++;
            var freshSnapshot = await freshSnapshotTask;
            if (freshSnapshot != null && freshSnapshot.Connected && ping.HasValue)
                freshSnapshot.PingMs = (int)Math.Min(ping.Value, int.MaxValue);

            if (SnapshotMergeHelper.HasDisplayableData(freshSnapshot))
            {
                WarmServerHistoryCache(localKey, freshSnapshot!);
                if (string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase))
                {
                    var effective = ApplyIncomingSnapshotForUi(freshSnapshot, localKey) ?? freshSnapshot;
                    UpdateUiWithSnapshot(effective, server, localKey, ping, rdp, DateTime.Now, scanCt, DashboardUpdateTier.Basic);
                }
            }

            var latestSnap = SnapshotMergeHelper.HasDisplayableData(freshSnapshot)
                ? freshSnapshot
                : (_serverHistory.TryGetValue(localKey, out var hist) && hist.Any() ? hist.Last() : null);
            if (latestSnap != null && latestSnap.DiskUsedPct.HasValue)
            {
                bool diskCrit = latestSnap.DiskUsedPct > 85;
                items.Add(new ScanItem
                {
                    Icon   = diskCrit ? "🔴" : "✅",
                    Label  = "DISK",
                    Detail = diskCrit
                        ? $"%{latestSnap.DiskUsedPct:0} dolu ({latestSnap.DiskFreeGb:0.0} GB free) — Kritik!"
                        : $"%{latestSnap.DiskUsedPct:0} dolu ({latestSnap.DiskFreeGb:0.0} GB free)",
                    IsRisk = diskCrit
                });
                if (diskCrit) risks++;
            }
            else
            {
                items.Add(new ScanItem { Icon = "❓", Label = "DISK", Detail = "Veri yok (WMI bağlantısı gerekli)", IsRisk = false });
            }

            // 4. Session Check
            checks++;
            if (latestSnap != null)
            {
                var sessCnt = latestSnap.Sessions.Count(s => !string.IsNullOrEmpty(s.User) && s.User != "-");
                var dupes   = latestSnap.Sessions.GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
                items.Add(new ScanItem
                {
                    Icon   = dupes ? "⚠️" : "✅",
                    Label  = "OTURUMLAR",
                    Detail = dupes ? $"{sessCnt} aktif — Duplicate oturum tespit edildi!" : $"{sessCnt} aktif, risk yok",
                    IsRisk = dupes
                });
                if (dupes) risks++;
            }

            // 5. Service Check
            if (latestSnap != null && latestSnap.Services.Any())
            {
                var stopped = latestSnap.Services.Where(s => !s.State.Equals("Running", StringComparison.OrdinalIgnoreCase)).ToList();
                checks++;
                bool svcRisk = stopped.Any();
                items.Add(new ScanItem
                {
                    Icon   = svcRisk ? "🔴" : "✅",
                    Label  = "KRİTİK SERVİSLER",
                    Detail = svcRisk
                        ? $"{stopped.Count} servis durdurulmuş: {string.Join(", ", stopped.Select(s => s.Name))}"
                        : $"{latestSnap.Services.Count} servis çalışıyor",
                    IsRisk = svcRisk
                });
                if (svcRisk) risks++;
            }

            // Show modern dialog
            var dlg = new QuickScanDialog(server.Name ?? server.Ip, server.Ip, items, checks, risks)
            {
                Owner = this
            };
            dlg.ShowDialog();
        }


        private async Task<WmiSnapshot?> CollectFreshQuickScanSnapshotAsync(ServerInfo server, CancellationToken ct)
        {
            var credentials = SettingsApplicator.ResolveCredentials(server, SettingsRuntime.Current);
            var snapshot = await TryFetchSnapshotViaAgentAsync(server, ct);
            if (SnapshotMergeHelper.HasDisplayableData(snapshot) || IsAgentAuthenticationFailure(snapshot) || ct.IsCancellationRequested)
                return snapshot;

            return await WmiCollector.GetSnapshotAsync(
                server.Ip,
                credentials.Username,
                credentials.Password,
                ct,
                onProgress: null,
                force: true,
                includeEventLogs: false);
        }

        private static bool TryParseIdleMinutes(string idle, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrEmpty(idle) || idle == "." || idle.Equals("none", StringComparison.OrdinalIgnoreCase))
                return false;
            // Format: "H:MM" or ">H:MM" or "MM"
            var clean = idle.TrimStart('>');
            if (TimeSpan.TryParseExact(clean, new[] { @"h\:mm", @"m\:ss", @"h\:mm\:ss" }, null, out var ts))
            {
                minutes = (int)ts.TotalMinutes;
                return true;
            }
            if (int.TryParse(clean, out minutes)) return true;
            return false;
        }

        private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
        {
            if (_selectedNode != null)
            {
                RemoveNodeInternal(_selectedNode);
            }
        }

        private void RemoveNodeInternal(ConnectionTreeNode node)
        {
            if (node == null) return;

            if (node.Server != null)
            {
                var result = System.Windows.MessageBox.Show(this, $"'{node.Server.Name}' ({node.Server.Ip}) sunucusunu silmek istediğinize emin misiniz?", "Sunucuyu Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _favoriteServerKeys.Remove(GetFavoriteServerKey(node.Server));
                    _servers.Remove(node.Server);
                    BuildConnectionTree();
                    RefreshFavoritesUi();
                    SaveConnectionsToDisk();
                    ClearConfigUi();
                }
            }
            else
            {
                var result = System.Windows.MessageBox.Show(this, $"'{node.Title}' klasörünü ve içindeki tüm sunucuları silmek istediğinize emin misiniz?", "Klasörü Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var folderName = node.Title;
                    if (folderName == "Sahsi")
                    {
                         System.Windows.MessageBox.Show(this, "Sistem klasörü silinemez.", "Bilgi");
                         return;
                    }
                    var serversToRemove = _servers.Where(s => (string.IsNullOrWhiteSpace(s.Folder) ? "Sahsi" : s.Folder) == folderName).ToList();
                    foreach (var s in serversToRemove)
                    {
                        _favoriteServerKeys.Remove(GetFavoriteServerKey(s));
                        _servers.Remove(s);
                    }
                    
                    bool removed = _explicitFolders.Remove(folderName);
                    
                    BuildConnectionTree();
                    RefreshFavoritesUi();
                    SaveConnectionsToDisk();
                    
                    if (serversToRemove.Count == 0 && !removed)
                    {
                        System.Windows.MessageBox.Show(this, $"'{folderName}' klasörü silinemedi (Eşleşme bulunamadı).", "Bilgi");
                    }
                }
            }
        }

        private void OnRemoveNodeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var mi = sender as MenuItem;
                var node = mi?.DataContext as ConnectionTreeNode;
                
                if (node == null)
                {
                    var cm = (mi?.Parent as ContextMenu) ?? (mi?.CommandParameter as ContextMenu);
                    if (cm?.PlacementTarget is FrameworkElement fe)
                    {
                        node = fe.DataContext as ConnectionTreeNode;
                    }
                }
                
                if (node == null) node = _selectedNode;

                if (node != null)
                {
                    RemoveNodeInternal(node);
                }
                else
                {
                    System.Windows.MessageBox.Show(this, "Silinecek öğe belirlenemedi.", "Bilgi");
                }
            }
            catch (Exception ex)
            {
                _ = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.AsyncCommand,
                    "Connections.Delete",
                    notifyUser: true,
                    userMessage: "\u00d6\u011fe silinemedi. L\u00fctfen yeniden deneyin.");
            }
        }

        private void OnConnectionSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_syncingTreeSelection)
            {
                _selectedNode = e.NewValue as ConnectionTreeNode;
                return;
            }

            _selectedNode = e.NewValue as ConnectionTreeNode;
            var server = _selectedNode?.Server;
            if (server != null)
            {
                if (HasOpenRdpTab())
                {
                    _selectedServer = server;
                    FillConfigUi(server);
                    UpdateRdpToolbarState();
                    return;
                }

                UpdateConnectionStatus($"{server.Ip} seçildi.", false);
                FillConfigUi(server);

                if (_activeDashboardServer != null
                    && IsSameServer(_activeDashboardServer, server)
                    && string.Equals(_insightsKey, GetServerKey(server), StringComparison.OrdinalIgnoreCase))
                {
                    _selectedServer = server;
                    UpdateRdpToolbarState();
                    return;
                }

                ActivateDashboardForServer(server);
            }
            else
            {
                ClearConfigUi();
                ClearActiveDashboardServer();
                HideSelectedServerOverview();
            }
        }

        private void OnConnectionItemClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem { DataContext: ConnectionTreeNode { Server: not null } node })
                return;

            _selectedNode = node;
            FillConfigUi(node.Server);

            if (HasOpenRdpTab())
            {
                _selectedServer = node.Server;
                UpdateRdpToolbarState();
                return;
            }

            UpdateConnectionStatus($"{node.Server.Ip} seçildi.", false);
            ActivateDashboardForServer(node.Server);
        }

        private void OnConnectionDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Find the actual TreeViewItem double-clicked
            if (sender is TreeViewItem item)
            {
                // Check if the node has a server
                if (item.DataContext is ConnectionTreeNode node && node.Server != null)
                {
                    e.Handled = true; // Prevents expanding/collapsing on double click
                    ConnectToServer(node.Server);
                }
            }
        }

        private void CancelDeferredDeepWork()
        {
            try { _deferredDeepCts?.Cancel(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { _deferredDeepCts?.Dispose(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            _deferredDeepCts = null;
        }

        private void EnsureDashboardPanelVisible()
        {
            try
            {
                if (HasOpenRdpTab())
                    return;

                if (IsRdpActive()) return;
                RestoreRdpChromeAfterSession();
                if (SelectedServerPanel != null) SelectedServerPanel.Visibility = Visibility.Visible;
                if (RdpPlaceholder != null) RdpPlaceholder.Visibility = Visibility.Collapsed;
                if (RdpHost != null) RdpHost.Visibility = Visibility.Collapsed;
                ApplyActiveWorkspaceContent();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private bool ShouldShowRdpBottomChrome()
        {
            // An active RDP session owns the full workspace. BottomPanels has Height=0,
            // but the old path still reserved its measured/default 130 DIP row, producing
            // the large black strip visible below the remote desktop.
            return false;
        }

        private void RestoreRdpChromeAfterSession()
        {
            if (IsRdpActive())
                return;

            if (_rdpViewportVm.IsRdpFocusMode)
                ExitRdpFocusMode();

            EnsureChromeDefaultsMeasured();
            _isResizingRdpChrome = false;
            _chromeResizeVm.CurrentTopChromeHeight = _chromeResizeVm.DefaultTopChromeHeight;
            ApplyTopChromeLayout(false);

            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    MainContentGrid?.UpdateLayout();
                    RdpContentGrid?.UpdateLayout();
                    SelectedServerPanel?.UpdateLayout();
                    DashboardWorkspaceHost?.RelayoutAll();
                }
                catch (Exception ex) { AppLogger.Error("Dashboard relayout after RDP failed.", ex); }
            }, DispatcherPriority.Loaded);
        }

        private void ApplyDashboardPhase0Ui(ServerInfo server, WmiSnapshot? cachedSnap, string? localKey = null)
        {
            if (localKey != null && IsLiveMetricsActive(localKey))
                return;

            if (cachedSnap != null)
            {
                SetTextIfChanged(SelectedOsText, SnapshotMergeHelper.IsMeaningfulOs(cachedSnap.OSName)
                    ? cachedSnap.OSName!
                    : (string.IsNullOrWhiteSpace(cachedSnap.OSName) ? "Henüz bilinmiyor" : cachedSnap.OSName));
                SetTextIfChanged(SelectedCpuCountText, cachedSnap.CPUCoreCount?.ToString() ?? "—");
                SetTextIfChanged(SelectedRamSizeText, cachedSnap.TotalRamGb.HasValue ? $"{cachedSnap.TotalRamGb.Value:0} GB" : "—");
                SelectedLastUserText.Text = cachedSnap.LastUser ?? "Yok";
                SelectedLastLogonText.Text = cachedSnap.LastLogon ?? "-";
                SelectedLastSessionText.Text = cachedSnap.LastSession ?? "-";

                var activeAccountList = cachedSnap.Sessions
                    .Where(s => !string.IsNullOrEmpty(s.User) && s.User != "-")
                    .Select(s => s.User.Contains("\\") ? s.User.Split('\\').Last() : s.User)
                    .Distinct()
                    .ToList();
                ActiveAccountsValueText.Text = activeAccountList.Any() ? string.Join(", ", activeAccountList) : "Yok";

                UpdateStaleDataBanner(cachedSnap.CollectedAt.ToUniversalTime(), refreshing: true);
                SelectedStatusText.Text = "KAYITLI";
                HealthBadgeDot.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));

                var newSessions = new List<SessionRow>();
                foreach (var s in cachedSnap.Sessions)
                    newSessions.Add(new SessionRow(s.User, s.State, s.Idle, s.Logon, "Normal"));
                _sessionRows.SyncWithDiff(newSessions, (a, b) => a.User == b.User && a.State == b.State);
                UpdateSessionsWidget(cachedSnap);
                _serviceRows.SyncWithDiff(cachedSnap.Services, (a, b) => a.Display == b.Display && a.State == b.State);
                RefreshServicesFilter();
                UpdateServicesWidgetState(cachedSnap, SettingsRuntime.Current);

                _currentTimelinePageItems.Clear();
                _currentTimelinePageItems.AddRange(cachedSnap.Timeline);
                if (!_currentTimelinePageItems.Any())
                {
                    SetTimelineEmptyState("Event Log data is loading in the background.");
                }
                else
                {
                    ShowTimelinePage(1);
                }
                ActiveUsersBadgeText.Text = cachedSnap.ActiveUsers.HasValue
                    ? $"Aktif Kullanıcı: {cachedSnap.ActiveUsers.Value}"
                    : "Aktif Kullanıcı: -";
            }
            else
            {
                ApplyMinimumDashboardFromServer(server);
            }

            SyncInfoCompactContent();
        }

        private void UpdateStaleDataBanner(DateTime? lastUpdatedUtc, bool refreshing)
        {
            if (lastUpdatedUtc.HasValue)
            {
                var local = lastUpdatedUtc.Value.ToLocalTime();
                if (refreshing)
                    HealthSummaryText.Text = $"Son kayıtlı veri gösteriliyor, arka planda güncelleniyor... ({local:HH:mm})";
                else
                    HealthSummaryText.Text = $"Son veri: {local:HH:mm}";
            }
            else if (refreshing)
            {
                HealthSummaryText.Text = "Canlı analiz başlatılıyor...";
            }
        }

        private void ApplyMinimumDashboardFromServer(ServerInfo server)
        {
            var displayName = string.IsNullOrWhiteSpace(server.Name) ? "SUNUCU" : server.Name.ToUpper();
            SelectedTitleText.Text = displayName;
            SelectedDnsNameText.Text = server.Ip;
            IntelligenceServerNameText.Text = server.Name ?? "SUNUCU";
            IntelligenceRoleText.Text = "Bilinmiyor";
            IntelligenceConfidenceText.Text = "—";
            IntelligenceRoleIcon.Text = "❓";
            IntelligenceCpuVal.Text = "—";
            IntelligenceRamVal.Text = "—";
            IntelligenceDiskVal.Text = "—";
            IntelligencePingVal.Text = "—";
            SelectedOsText.Text = "Henüz bilinmiyor";
            SelectedCpuCountText.Text = "—";
            SelectedRamSizeText.Text = "—";
            SelectedLastUserText.Text = string.IsNullOrWhiteSpace(server.Username) ? "—" : server.Username;
            SelectedLastLogonText.Text = "—";
            SelectedLastSessionText.Text = "—";
            ActiveAccountsValueText.Text = "—";
            SelectedStatusText.Text = "BİLİNMİYOR";
            HealthBadgeDot.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
            HealthSummaryText.Text = "Bu sunucu için henüz analiz kaydı yok. Canlı analiz başlatıldı.";
            ActiveUsersBadgeText.Text = "Aktif Kullanıcı: -";
            _sessionRows.SyncWithDiff(new List<SessionRow>(), (a, b) => a.User == b.User);
            UpdateSessionsWidget(null);
            _serviceRows.SyncWithDiff(new List<ServiceRow>(), (a, b) => a.Display == b.Display);
            RefreshServicesFilter();
            UpdateServicesWidgetState(null, SettingsRuntime.Current);
            _currentTimelinePageItems.Clear();
            SetTimelineEmptyState("Live analysis started. Event Log data will appear after the server snapshot is collected.");
            UpdateAiCacheStatusLabel(null);
            ApplyServerBootstrapMetrics(server, GetServerKey(server));
            UpdateHealthStatusState();
            UpdateGpuWidget(null);
            UpdateProcessesWidget(null);
            UpdateWindowsUpdatesWidget(null);
            UpdateFirewallWidget(null);
            UpdateBitLockerWidget(null);
            UpdateDefenderWidget(null);
            UpdateSecurityScoreWidget(null);
            UpdateOpenPortsWidget(null);
            SyncInfoCompactContent();
        }

        private void ApplyDashboardFromCachedMetrics(ServerInfo server, WmiSnapshot cachedSnap, string localKey, bool refreshing)
        {
            if (!_dashboardState.TryApplyBootstrapSnapshot(localKey, cachedSnap))
                return;

            ApplyDashboardPhase0Ui(server, cachedSnap, localKey);
            UpdateIntelligenceUi(cachedSnap, server.Name ?? "SUNUCU", forceRegenerate: false);
            ApplySnapshotMetricsToUi(cachedSnap, server, localKey, cachedSnap.PingMs >= 0 ? cachedSnap.PingMs : null);
            RestoreCachedAiInsights(localKey);
            UpdateStaleDataBanner(cachedSnap.CollectedAt.ToUniversalTime(), refreshing);
        }

        private void ApplyAuthoritativeDashboardState(ServerInfo server, WmiSnapshot snap, string localKey)
        {
            if (!_dashboardState.TryRestoreLiveOwnedSnapshot(localKey, snap))
                return;

            ApplyDashboardPhase0Ui(server, snap, localKey);
            UpdateIntelligenceUi(snap, server.Name ?? "SUNUCU", forceRegenerate: false);
            RestoreCachedAiInsights(localKey);
            UpdateLiveMetrics(server, localKey, snap, snap.PingMs >= 0 ? snap.PingMs : null, addChartSample: false, recordNetwork: false);
            UpdateStaleDataBanner(DateTime.UtcNow, refreshing: true);
        }

        private void ApplyServerBootstrapMetrics(ServerInfo server, string localKey)
        {
            var metrics = LiveDashboardMetricsBuilder.FromServer(server, localKey);
            _dashboardState.SetCurrentLiveMetrics(metrics);
            ApplyLiveDashboardMetricsUi(metrics, addChartSample: false);
        }

        private void ApplySnapshotMetricsToUi(WmiSnapshot? snap, ServerInfo server, string localKey, long? latencyMs)
        {
            if (IsLiveMetricsActive(localKey))
                return;

            var metrics = LiveDashboardMetricsBuilder.FromSnapshot(snap, server, localKey, latencyMs, isLive: false);
            _dashboardState.SetCurrentLiveMetrics(metrics);
            ApplyLiveDashboardMetricsUi(metrics, addChartSample: false);
        }

        private void UpdateLiveMetrics(ServerInfo server, string localKey, WmiSnapshot snap, long? latencyMs, bool addChartSample = true, bool recordNetwork = true)
        {
            if (!string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase))
                return;

            if (snap.PingMs < 0 && latencyMs.HasValue)
                snap.PingMs = (int)Math.Min(latencyMs.Value, int.MaxValue);

            _dashboardState.SetActiveLiveRefresh(localKey);
            _dashboardState.TryApplyLiveSnapshot(localKey, _refreshGeneration, snap);

            var metrics = LiveDashboardMetricsBuilder.FromSnapshot(
                _dashboardState.CurrentSnapshot ?? snap, server, localKey, latencyMs, isLive: true, networkTracker: _networkTracker, recordNetwork: recordNetwork);
            _dashboardState.SetCurrentLiveMetrics(metrics);
            ApplyLiveDashboardMetricsUi(metrics, addChartSample);
        }

        private void StartLiveMetricSampling(ServerInfo server, string localKey, long? latencyMs)
        {
            var credentials = SettingsApplicator.ResolveCredentials(server, SettingsRuntime.Current);
            var telemetryServer = new ServerInfo(
                server.Name, server.Ip, credentials.Username, credentials.Password, server.Folder, server.Port);
            _liveMetricSampler.Start(new LiveMetricSamplingContext
            {
                Server = telemetryServer,
                ServerKey = localKey,
                LatencyMs = latencyMs,
                IsStillActive = () => string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase),
                OnSampleAsync = async (snap, ping) =>
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase))
                            return;
                        UpdateLiveMetrics(server, localKey, snap, ping, addChartSample: true);
                    });
                }
            });
        }

        private void ApplyLiveDashboardMetricsUi(LiveDashboardMetrics metrics, bool addChartSample)
        {
            try
            {
                if (!metrics.IsLive && IsLiveTelemetryAuthoritative(metrics.ServerKey))
                    return;

                var settings = SettingsRuntime.Current;

                if (settings.MonitorCpu)
                {
                    if (metrics.CpuPercent.HasValue)
                    {
                        var cpuText = $"{metrics.CpuPercent.Value}%";
                        SetTextIfChanged(IntelligenceCpuVal, cpuText);
                        SetTextIfChanged(HeaderCpuValue, cpuText);
                        SetTextIfChanged(LiveCpuText, cpuText);
                        if (Math.Abs(IntelligenceCpuBar.Value - metrics.CpuPercent.Value) > 0.1)
                            IntelligenceCpuBar.Value = metrics.CpuPercent.Value;
                        UpdateHeaderKpiBar(HeaderCpuBarFill, metrics.CpuPercent.Value);
                        UpdateHeaderKpiBar(ServerOverviewCpuBarFill, metrics.CpuPercent.Value, 170);
                    }
                    else if (metrics.IsLive)
                    {
                        SetTextIfChanged(IntelligenceCpuVal, "—");
                        SetTextIfChanged(HeaderCpuValue, "—");
                        SetTextIfChanged(LiveCpuText, "—");
                    }
                }

                if (settings.MonitorRam)
                {
                    if (metrics.RamPercent.HasValue)
                    {
                        var ramText = $"{metrics.RamPercent.Value:0}%";
                        SetTextIfChanged(IntelligenceRamVal, ramText);
                        SetTextIfChanged(HeaderRamValue, ramText);
                        SetTextIfChanged(LiveRamText, ramText);
                        if (Math.Abs(IntelligenceRamBar.Value - metrics.RamPercent.Value) > 0.1)
                            IntelligenceRamBar.Value = metrics.RamPercent.Value;
                        UpdateHeaderKpiBar(HeaderRamBarFill, metrics.RamPercent.Value);
                        UpdateHeaderKpiBar(ServerOverviewRamBarFill, metrics.RamPercent.Value, 170);
                    }
                    else if (metrics.IsLive)
                    {
                        SetTextIfChanged(IntelligenceRamVal, "—");
                        SetTextIfChanged(HeaderRamValue, "—");
                        SetTextIfChanged(LiveRamText, "—");
                    }
                }

                if (settings.MonitorDisk)
                {
                    if (metrics.DiskPercent.HasValue)
                    {
                        var diskText = $"{metrics.DiskPercent.Value:0}%";
                        SetTextIfChanged(IntelligenceDiskVal, diskText);
                        SetTextIfChanged(HeaderDiskValue, diskText);
                        SetTextIfChanged(LiveDiskText, diskText);
                        if (Math.Abs(IntelligenceDiskBar.Value - metrics.DiskPercent.Value) > 0.1)
                            IntelligenceDiskBar.Value = metrics.DiskPercent.Value;
                        UpdateHeaderKpiBar(HeaderDiskBarFill, metrics.DiskPercent.Value);
                        UpdateHeaderKpiBar(ServerOverviewDiskBarFill, metrics.DiskPercent.Value, 170);

                        HeaderDiskValue.Foreground = new SolidColorBrush(metrics.DiskPercent.Value >= 90
                            ? System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71)
                            : metrics.DiskPercent.Value >= 70
                                ? System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24)
                                : System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
                    }
                    else if (metrics.IsLive)
                    {
                        SetTextIfChanged(IntelligenceDiskVal, "—");
                        SetTextIfChanged(HeaderDiskValue, "—");
                        SetTextIfChanged(LiveDiskText, "—");
                    }
                }

                if (settings.MonitorNetwork)
                {
                    if (metrics.LatencyMs is >= 0)
                    {
                        SetTextIfChanged(IntelligencePingVal, $"{metrics.LatencyMs.Value}ms");
                        var statusText = metrics.LatencyMs < 50 ? "Ultra Stable" : (metrics.LatencyMs < 150 ? "Good" : "Lags Detected");
                        SetTextIfChanged(IntelligencePingStatus, statusText);
                        IntelligencePingStatus.Foreground = new System.Windows.Media.SolidColorBrush(metrics.LatencyMs < 100
                            ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)
                            : System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
                    }
                    else if (metrics.IsLive)
                    {
                        SetTextIfChanged(IntelligencePingVal, "N/A");
                        SetTextIfChanged(IntelligencePingStatus, "N/A");
                    }

                    var upText = "—";
                    var downText = "—";
                    switch (metrics.NetworkState)
                    {
                        case NetworkDisplayState.Collecting:
                            upText = "Toplanıyor...";
                            downText = "Toplanıyor...";
                            break;
                        case NetworkDisplayState.Ready:
                            upText = metrics.NetworkOutBps.HasValue
                                ? $"↑ {LiveDashboardMetricsBuilder.FormatThroughput(metrics.NetworkOutBps)}"
                                : "Alınamadı";
                            downText = metrics.NetworkInBps.HasValue
                                ? $"↓ {LiveDashboardMetricsBuilder.FormatThroughput(metrics.NetworkInBps)}"
                                : "Alınamadı";
                            break;
                        case NetworkDisplayState.Unavailable:
                            upText = "Alınamadı";
                            downText = metrics.LatencyMs is >= 0
                                ? $"Ping {metrics.LatencyMs.Value}ms"
                                : "Alınamadı";
                            break;
                        case NetworkDisplayState.Idle:
                        default:
                            upText = metrics.IsLive ? "Toplanıyor..." : "—";
                            downText = metrics.IsLive ? "Toplanıyor..." : "—";
                            break;
                    }
                    SetTextIfChanged(LiveNetworkUpText, upText);
                    SetTextIfChanged(LiveNetworkDownText, downText);
                }
                else
                {
                    SetTextIfChanged(LiveNetworkUpText, "—");
                    SetTextIfChanged(LiveNetworkDownText, "—");
                }

                var uptimeText = metrics.UptimeText ?? "—";
                var uptimeDetail = metrics.UptimeDetailText ?? "—";
                SetTextIfChanged(DashUptimeText, uptimeText);
                SetTextIfChanged(LiveUptimeText, uptimeText);
                SetTextIfChanged(LiveUptimeDetailText, uptimeDetail);

                var ip = string.IsNullOrWhiteSpace(metrics.IpAddress) ? "—" : metrics.IpAddress;
                SetTextIfChanged(LiveIpText, ip);
                SetTextIfChanged(SelectedDnsNameText, ip);
                UpdateServerOverviewDetails(_lastSnapshot);

                if (_activeSession != null && _selectedServer != null)
                    StatusConnectionText.Text = $"Bağlantı: {_selectedServer.Name} ({_selectedServer.Ip})";
                else if (_selectedServer != null)
                    StatusConnectionText.Text = $"Bağlantı: {_selectedServer.Name} ({_selectedServer.Ip})";

                var networkSource = metrics.NetworkState switch
                {
                    NetworkDisplayState.Ready => "live",
                    NetworkDisplayState.Collecting => "collecting",
                    NetworkDisplayState.Unavailable => "unavailable",
                    _ => metrics.IsLive ? "collecting" : "snapshot"
                };
                var uptimeSource = metrics.HasUptime
                    ? (metrics.IsLive ? "live" : "snapshot")
                    : (metrics.IsLive ? "na" : "server");
                var ipSource = metrics.HasIp
                    ? (metrics.Source == LiveDashboardMetricSources.Server ? "server" : metrics.Source)
                    : "na";

                RefreshDebugLog.BottomCardsApplied(metrics.ServerKey, networkSource, uptimeSource, ipSource);

                var chartSamples = _liveChartSession.SampleCount;
                if (addChartSample && metrics.IsLive)
                {
                    var chartKey = _liveChartServerKey ?? metrics.ServerKey;
                    if (_liveChartSession.TryAddLiveSample(metrics, metrics.ServerKey, chartKey))
                    {
                        _chartUpdateThrottle.Reset();
                        _lastChartCpuCount = 0;
                        _lastChartRamCount = 0;
                        UpdatePerformanceChart();
                    }
                    chartSamples = _liveChartSession.SampleCount;
                }

                RefreshDebugLog.LiveMetricsApplied(
                    metrics.ServerKey,
                    metrics.HasCpu,
                    metrics.HasRam,
                    metrics.HasDisk,
                    metrics.HasNetwork,
                    metrics.HasUptime,
                    metrics.HasIp,
                    chartSamples);

                RefreshDebugLog.ChartUpdate(
                    metrics.ServerKey,
                    chartSamples,
                    draw: chartSamples > 0);

                UpdateFooterSparklines();
                UpdateStatusBar(metrics);
                SyncInfoCompactContent();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private static void UpdateHeaderKpiBar(System.Windows.Controls.Border? fill, double percent, double trackWidth = 72)
        {
            if (fill == null)
                return;
            fill.Width = Math.Max(0, trackWidth * Math.Clamp(percent, 0, 100) / 100.0);
        }

        private void UpdateFooterSparklines()
        {
            try
            {
                UpdateSparkline(LiveCpuSparkline, _liveChartSession.GetCpuSeries(), 100);
                UpdateSparkline(LiveRamSparkline, _liveChartSession.GetRamSeries(), 100);
                UpdateSparkline(LiveDiskSparkline, _liveChartSession.GetDiskSeries(), 100);
                UpdateSparkline(DashCpuSparkline, _liveChartSession.GetCpuSeries(), 100);
                UpdateSparkline(DashRamSparkline, _liveChartSession.GetRamSeries(), 100);
                UpdateSparkline(DashDiskSparkline, _liveChartSession.GetDiskSeries(), 100);
                UpdateSparkline(DashNetworkSparkline, _liveChartSession.GetNetworkSeries(), 100);
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private static void UpdateSparkline(System.Windows.Shapes.Polyline? polyline, IReadOnlyList<double> values, double maxValue)
        {
            if (polyline == null)
                return;

            if (values.Count < 2)
            {
                polyline.Points = new PointCollection { new System.Windows.Point(0, 20), new System.Windows.Point(48, 20) };
                return;
            }

            var tail = values.TakeLast(10).ToList();
            var max = Math.Max(1, tail.Max());
            if (maxValue > 0)
                max = maxValue;

            var points = new PointCollection(tail.Count);
            double step = tail.Count > 1 ? 48.0 / (tail.Count - 1) : 0;
            for (int i = 0; i < tail.Count; i++)
            {
                double x = i * step;
                double y = 24 - (Math.Clamp(tail[i], 0, max) / max * 20);
                points.Add(new System.Windows.Point(x, y));
            }
            polyline.Points = points;
        }

        private void UpdateStatusBar(LiveDashboardMetrics metrics)
        {
            try
            {
                if (metrics.LatencyMs is >= 0)
                {
                    StatusLatencyPanel.Visibility = Visibility.Visible;
                    SetTextIfChanged(StatusLatencyText, $"Latency: {metrics.LatencyMs.Value}ms");
                }
                else
                {
                    StatusLatencyPanel.Visibility = Visibility.Collapsed;
                }

                var rdpSession = _activeSession;
                var sameServer = rdpSession?.Server != null && _selectedServer != null
                    && string.Equals(rdpSession.Server.Ip, _selectedServer.Ip, StringComparison.OrdinalIgnoreCase)
                    && rdpSession.CleanSession?.IsConnected == true;

                if (sameServer && rdpSession?.CleanSession != null)
                {
                    StatusProtocolPanel.Visibility = Visibility.Visible;
                    StatusEncryptionPanel.Visibility = Visibility.Visible;
                    StatusSessionPanel.Visibility = Visibility.Visible;

                    var profile = rdpSession.CleanSession.ActiveProfileLabel ?? "RDP";
                    SetTextIfChanged(StatusProtocolText, profile);

                    var encryption = profile.Contains("CredSSP", StringComparison.OrdinalIgnoreCase)
                        ? "CredSSP / TLS"
                        : "Standard RDP";
                    SetTextIfChanged(StatusEncryptionText, encryption);

                    if (rdpSession.CleanSession.ConnectedAtUtc is DateTime started)
                    {
                        var elapsed = DateTime.UtcNow - started;
                        SetTextIfChanged(StatusSessionText, $"Oturum: {elapsed:hh\\:mm\\:ss}");
                    }
                }
                else
                {
                    StatusProtocolPanel.Visibility = Visibility.Collapsed;
                    StatusEncryptionPanel.Visibility = Visibility.Collapsed;
                    StatusSessionPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void EnsureStatusBarTimer()
        {
            if (_statusBarTimer != null)
                return;

            _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusBarTimer.Tick += (_, _) =>
            {
                if (_currentLiveMetrics != null)
                    UpdateStatusBar(_currentLiveMetrics);
            };
            _statusBarTimer.Start();
        }

        private void ApplyPersistentEnrichment(ServerInfo server, PersistentServerSnapshot snapshot)
        {
            var localKey = GetServerKey(server);
            if (IsLiveMetricsActive(localKey))
                return;

            if (snapshot.Services?.Count > 0 && !_serviceRows.Any())
                _serviceRows.SyncWithDiff(snapshot.Services, (a, b) => a.Display == b.Display && a.State == b.State);

            if (snapshot.Timeline?.Count > 0 && !_currentTimelinePageItems.Any(e => e.Type != "System"))
            {
                _currentTimelinePageItems.Clear();
                _currentTimelinePageItems.AddRange(snapshot.Timeline);
                ShowTimelinePage(1);
            }

            if (snapshot.AiInsights?.Count > 0 && !_smartInsights.Any(x => x.IsAi))
            {
                foreach (var ins in snapshot.AiInsights)
                    _smartInsights.Add(ins);
                SmartInsightList.ItemsSource = _smartInsights;
            }

            if (snapshot.RuleInsights?.Count > 0)
            {
                foreach (var ins in snapshot.RuleInsights)
                {
                    if (!_smartInsights.Any(x => x.Text == ins.Text))
                        _smartInsights.Add(ins);
                }
            }

            if (snapshot.SystemStatusComponents?.Count > 0 && (IntelligenceComponentsList.ItemsSource == null
                || !_currentIntelligence.Components.Any()))
            {
                _currentIntelligence = new ServerIntelligence(
                    server.Name ?? "SUNUCU",
                    snapshot.DetectedRole ?? _currentIntelligence.DetectedRole,
                    snapshot.IntelligenceConfidence > 0 ? snapshot.IntelligenceConfidence : _currentIntelligence.Confidence,
                    snapshot.SystemStatusComponents,
                    _currentIntelligence.Insights,
                    _currentIntelligence.Recommendations);
                IntelligenceComponentsList.ItemsSource = snapshot.SystemStatusComponents;
                SetTextIfChanged(IntelligenceRoleText, snapshot.DetectedRole ?? "Unknown");
                UpdateHealthExpandedSummary();
            }

            UpdateAiCacheStatusLabel(snapshot.SectionTimestamps?.AiAnalysisUtc);
        }

        private void UpdateAiCacheStatusLabel(DateTime? aiUtc)
        {
            if (_smartInsights.Any(x => x.IsAi))
            {
                if (aiUtc.HasValue)
                {
                    AiAnalysisStatusText.Text = $"Son AI analizi: {aiUtc.Value.ToLocalTime():HH:mm}";
                    AiAnalysisStatusText.Visibility = Visibility.Visible;
                }
                SmartAnalysisNormalText.Visibility = Visibility.Collapsed;
            }
            else
            {
                AiAnalysisStatusText.Visibility = Visibility.Collapsed;
                SmartAnalysisNormalText.Text = "AI analizi henüz oluşturulmadı. Yenile ile başlatabilirsiniz.";
                SmartAnalysisNormalText.Visibility = Visibility.Visible;
            }
            UpdateAiWidgetState();
        }

        private void ApplyPersistentSnapshotToUi(ServerInfo server, PersistentServerSnapshot snapshot, bool refreshing)
        {
            var localKey = GetServerKey(server);
            if (IsLiveMetricsActive(localKey))
                return;

            SnapshotInspector.LogInspect(localKey, snapshot, "DashboardApply");

            if (!SnapshotMergeHelper.HasSnapshotUiContent(snapshot))
            {
                ApplyMinimumDashboardFromServer(server);
                return;
            }

            var snap = EnrichMetricsFromSnapshot(snapshot);
            if (!_dashboardState.TryApplyPersistentSnapshot(localKey, snap, snapshot.SectionTimestamps))
                return;

            SelectedTitleText.Text = string.IsNullOrWhiteSpace(server.Name) ? "SUNUCU" : server.Name.ToUpper();
            SelectedDnsNameText.Text = server.Ip;

            ApplyDashboardPhase0Ui(server, snap, localKey);
            UpdateIntelligenceUi(snap, server.Name ?? "SUNUCU", forceRegenerate: SnapshotMergeHelper.IsFullWmiMetrics(snap));
            ApplySnapshotMetricsToUi(snap, server, localKey, snap.PingMs >= 0 ? snap.PingMs : null);

            if (snapshot.Services?.Count > 0)
                _serviceRows.SyncWithDiff(snapshot.Services, (a, b) => a.Display == b.Display && a.State == b.State);

            if (snapshot.Timeline?.Count > 0)
            {
                _currentTimelinePageItems.Clear();
                _currentTimelinePageItems.AddRange(snapshot.Timeline);
                ShowTimelinePage(1);
            }

            _smartInsights.Clear();
            if (snapshot.AiInsights?.Count > 0)
            {
                foreach (var ins in snapshot.AiInsights.OrderBy(x => x.SortOrder))
                    _smartInsights.Add(ins);
                _cachedAiInsightsByServer[localKey] = snapshot.AiInsights.ToList();
            }
            else if (snapshot.RuleInsights != null)
            {
                foreach (var ins in snapshot.RuleInsights.OrderBy(x => x.SortOrder))
                    _smartInsights.Add(ins);
            }
            SmartInsightList.ItemsSource = _smartInsights;
            SmartAnalysisNormalText.Visibility = _smartInsights.Any(x => !x.IsSectionHeader)
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateAiCacheStatusLabel(snapshot.SectionTimestamps?.AiAnalysisUtc);

            if (snapshot.SystemStatusComponents?.Count > 0
                && SnapshotMergeHelper.IsFullSnapshot(snapshot))
            {
                _currentIntelligence = new ServerIntelligence(
                    server.Name ?? "SUNUCU",
                    snapshot.DetectedRole ?? _currentIntelligence.DetectedRole,
                    snapshot.IntelligenceConfidence > 0 ? snapshot.IntelligenceConfidence : _currentIntelligence.Confidence,
                    snapshot.SystemStatusComponents,
                    _currentIntelligence.Insights,
                    _currentIntelligence.Recommendations);
                IntelligenceComponentsList.ItemsSource = snapshot.SystemStatusComponents;
                SetTextIfChanged(IntelligenceRoleText, snapshot.DetectedRole ?? "Unknown");
                SetTextIfChanged(IntelligenceConfidenceText, $"{_currentIntelligence.Confidence}%");
                UpdateHealthExpandedSummary();
            }

            UpdateStaleDataBanner(snapshot.LastUpdatedUtc, refreshing);
            LogSnapshotApplied(GetServerKey(server), AnalysisSnapshotMapper.DescribeSections(snapshot));
        }

        private static WmiSnapshot EnrichMetricsFromSnapshot(PersistentServerSnapshot snapshot)
        {
            var m = snapshot.Metrics ?? new WmiSnapshot();
            if (snapshot.Services.Count > 0)
                m.Services = snapshot.Services.ToList();
            if (snapshot.Timeline.Count > 0 && (m.Timeline == null || m.Timeline.Count == 0))
                m.Timeline = snapshot.Timeline.ToList();
            if (SnapshotMergeHelper.IsFullWmiMetrics(m) || snapshot.Services.Count > 0)
                m.Connected = true;
            m.DataSource = SnapshotDataSources.Persistent;
            return m;
        }

        private PersistentServerSnapshot BuildPersistentSnapshot(string localKey, WmiSnapshot snap)
        {
            var isFull = SnapshotMergeHelper.IsFullWmiMetrics(snap);
            List<WmiSnapshot>? history = null;
            if (_serverHistory.TryGetValue(localKey, out var h))
                history = h;

            var healthResult = CalculateHealthScore(_dashboardState.CurrentLiveMetrics?.LatencyMs, snap, _lastDashboardRdpOpen);

            return new PersistentServerSnapshot
            {
                SnapshotKind = isFull ? SnapshotKinds.FullWmi : SnapshotKinds.BasicProbe,
                Metrics = snap,
                Services = snap.Services?.Count > 0 ? snap.Services.ToList() : _serviceRows.ToList(),
                Timeline = _currentTimelinePageItems.ToList(),
                AiInsights = _smartInsights.ToList(),
                RuleInsights = new List<SmartInsight>(),
                SystemStatusComponents = _currentIntelligence.Components?.ToList() ?? new List<DetectedComponent>(),
                DetectedRole = _currentIntelligence.DetectedRole,
                IntelligenceConfidence = _currentIntelligence.Confidence,
                SectionTimestamps = _dashboardState.SectionTimestamps,
                Trends = SnapshotTrendAnalyzer.Analyze(history, snap, healthResult.Score)
            };
        }

        private void SchedulePersistSnapshot(string localKey, WmiSnapshot snap)
        {
            if (!IsLiveTelemetryAuthoritative(localKey))
                return;

            var built = BuildPersistentSnapshot(localKey, snap);
            if (!SnapshotMergeHelper.IsFullSnapshot(built) && built.AiInsights.Count == 0)
                return;

            built.LastUpdatedUtc = DateTime.UtcNow;
            PersistentSnapshotStore.ScheduleSave(localKey, built);
        }

        private async Task PersistFullSnapshotAsync(string localKey, WmiSnapshot snap)
        {
            if (!IsLiveTelemetryAuthoritative(localKey))
                return;

            if (!SnapshotMergeHelper.IsFullWmiMetrics(snap))
                return;

            try
            {
                var built = BuildPersistentSnapshot(localKey, snap);
                built.SnapshotKind = SnapshotKinds.FullWmi;
                built.LastUpdatedUtc = DateTime.UtcNow;
                if (built.Metrics != null)
                    built.Metrics.CollectedAt = snap.CollectedAt == default ? DateTime.Now : snap.CollectedAt;

                var existing = await PersistentSnapshotStore.LoadAsync(localKey).ConfigureAwait(false);
                var merged = existing != null
                    ? SnapshotMergeHelper.MergePersistentForDiskSave(existing, built)
                    : built;

                merged.LastUpdatedUtc = DateTime.UtcNow;
                await PersistentSnapshotStore.SaveImmediatelyAsync(localKey, merged).ConfigureAwait(false);
                WarmServerHistoryCache(localKey, merged.Metrics);
                SnapshotInspector.LogInspect(localKey, merged, "PersistFull");
                await Dispatcher.InvokeAsync(() =>
                {
                    if (string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase))
                        _refreshLifecycle?.MarkSnapshotSaved(_refreshGeneration, localKey);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Full snapshot persist failed.", ex, category: "Snapshot", action: "PersistFull");
            }
        }

        private WmiSnapshot? ResolveEffectiveSnapshot(WmiSnapshot? live, string localKey) =>
            _dashboardState.ResolveForUi(localKey, live);

        private WmiSnapshot? ApplyIncomingSnapshotForUi(WmiSnapshot? incoming, string localKey) =>
            _dashboardState.ApplyIncomingSnapshotForUi(localKey, incoming);

        private async Task RunDeferredDeepPhaseAsync(
            ServerInfo server,
            WmiSnapshot snap,
            string localKey,
            long? pingMs,
            bool rdpOpen,
            DateTime now,
            CancellationToken ct,
            int refreshGen,
            bool skipDelay = false)
        {
            using var perf = PerformanceTrace.Start("DashboardDeepPhase");
            var eventLogReported = false;
            var servicesReported = false;
            var refreshFailed = false;
            try
            {
                if (!skipDelay)
                {
                    var elapsed = DateTime.UtcNow - _dashboardLoadStartedUtc;
                    var wait = TimeSpan.FromSeconds(DashboardDeepPhaseDelaySeconds) - elapsed;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, ct).ConfigureAwait(false);
                }

                if (ct.IsCancellationRequested || _insightsKey != localKey)
                    return;

                if (!_refreshCoordinator.TryBegin(localKey, ServerRefreshCoordinator.RefreshPhase.EventLog))
                    return;

                try
                {
                    var settings = SettingsService.Instance.Current;
                    if (settings.MonitorEventLogs)
                        await TryDeferredEventLogEnrichmentAsync(server, snap, localKey, pingMs, rdpOpen, now, ct).ConfigureAwait(false);
                }
                finally
                {
                    _refreshCoordinator.End(localKey, ServerRefreshCoordinator.RefreshPhase.EventLog);
                    eventLogReported = true;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_insightsKey == localKey && _refreshLifecycle?.IsCurrent(refreshGen, localKey) == true)
                            _refreshLifecycle.Report(refreshGen, localKey, LiveRefreshMilestone.EventLog);
                    });
                }

                if (ct.IsCancellationRequested || _insightsKey != localKey)
                    return;

                if (!_refreshCoordinator.TryBegin(localKey, ServerRefreshCoordinator.RefreshPhase.DeepServices))
                    return;

                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_insightsKey != localKey) return;
                        if (!_refreshLifecycle?.IsCurrent(refreshGen, localKey) == true) return;
                        var currentSnap = _lastSnapshot ?? snap;
                        if (IsLiveMetricsActive(localKey))
                            ApplyDeepDashboardEnrichment(server, localKey, currentSnap, pingMs, rdpOpen, now, ct);
                        else
                            UpdateUiWithSnapshot(currentSnap, server, localKey, pingMs, rdpOpen, now, ct, DashboardUpdateTier.Deep);
                        UpdateStaleDataBanner(DateTime.UtcNow, refreshing: false);

                        if (!SnapshotTtl.IsExpired(_sectionTimestamps.AiAnalysisUtc, SnapshotTtl.AiAnalysis))
                        {
                            _refreshLifecycle?.MarkAiSkipped(refreshGen, localKey, "cache-valid");
                            UpdateAiCacheStatusLabel(_sectionTimestamps.AiAnalysisUtc);
                        }
                        else
                            RequestSmartAnalysis(server, force: false);
                    });
                }
                finally
                {
                    _refreshCoordinator.End(localKey, ServerRefreshCoordinator.RefreshPhase.DeepServices);
                    servicesReported = true;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_insightsKey == localKey && _refreshLifecycle?.IsCurrent(refreshGen, localKey) == true)
                            _refreshLifecycle.Report(refreshGen, localKey, LiveRefreshMilestone.Services);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Server switched or refresh stopped.
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Deferred deep dashboard phase failed.", ex, category: "Dashboard", action: "DeepPhase");
                refreshFailed = true;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_refreshLifecycle?.IsCurrent(refreshGen, localKey) == true)
                        _refreshLifecycle.CompleteFailed(refreshGen, localKey);
                });
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_refreshLifecycle?.IsCurrent(refreshGen, localKey) != true)
                        return;
                    if (refreshFailed)
                        return;

                    if (!eventLogReported)
                        _refreshLifecycle.Report(refreshGen, localKey, LiveRefreshMilestone.EventLog);
                    if (!servicesReported)
                        _refreshLifecycle.Report(refreshGen, localKey, LiveRefreshMilestone.Services);
                    _refreshLifecycle.CompleteSuccess(refreshGen, localKey);
                });
            }
        }

        private async void ShowSelectedServerOverview(ServerInfo server)
        {
            using var perf = PerformanceTrace.Start("ShowSelectedServerOverview");
            CancelAiGuideAnalysisInFlight();
            try
            {
                if (HasOpenRdpTab() || IsRdpActive())
                    return;

                _activeDashboardServer = server;
                _selectedServer = server;
                SyncTreeSelectionToServer(server);

                var localKey = GetServerKey(server);
                var previousKey = _insightsKey;
                var switchingServer = !string.IsNullOrEmpty(previousKey)
                    && !string.Equals(previousKey, localKey, StringComparison.OrdinalIgnoreCase);
                var liveAuthoritative = IsLiveTelemetryAuthoritative(localKey);

                if (switchingServer)
                {
                    _refreshCoordinator.CancelServer(previousKey!);
                    _refreshLifecycle?.Cancel(previousKey);
                }
                else if (!liveAuthoritative)
                {
                    _dashboardState.ClearActiveLiveRefresh();
                }

                if (!_dashboardState.IsActiveServer(localKey))
                    _dashboardState.OnServerSwitch(localKey);

                _refreshCoordinator.SetActiveServer(localKey);
                _dashboardLoadStartedUtc = DateTime.UtcNow;
                CancelDeferredDeepWork();

                EnsureDashboardPanelVisible();
                EnsureDashboardBuilderInitialized(localKey);
                SelectedTitleText.Text = string.IsNullOrWhiteSpace(server.Name) ? "SUNUCU" : server.Name.ToUpper();
                SelectedDnsNameText.Text = server.Ip;

                WmiSnapshot? memSnap = null;
                if (ServerHistoryHelper.TryGetHistory(_serverHistory, server, out var memHistory) && memHistory.Any())
                    memSnap = memHistory.Last();

                _ipAddresses.Clear();
                _ipAddresses.Add(server.Ip);
                ResetLiveChartSession(localKey);
                _currentAgentHealth = null;
                if (!liveAuthoritative)
                    ApplyServerBootstrapMetrics(server, localKey);
                EnsureStatusBarTimer();
                _lastIntelligenceFingerprint = null;
                _lastIntelligenceServerKey = null;
                _lastServicesFingerprint = null;
                _lastTimelineFingerprint = null;

                var hadInstantData = false;
                var memFull = memSnap != null && SnapshotMergeHelper.IsFullWmiMetrics(memSnap);

                if (liveAuthoritative
                    && ServerHistoryHelper.TryGetHistory(_serverHistory, server, out var authoritativeHistory)
                    && authoritativeHistory.Count > 0)
                {
                    var authoritativeSnap = authoritativeHistory[^1];
                    var persistentMeta = await PersistentSnapshotStore.LoadAsync(localKey).ConfigureAwait(true);
                    _dashboardState.SetSectionTimestamps(persistentMeta?.SectionTimestamps ?? new SnapshotSectionTimestamps());
                    ApplyAuthoritativeDashboardState(server, authoritativeSnap, localKey);
                    hadInstantData = true;
                }
                else
                {
                // B/C) Persistent .nsnap — load before first paint so disk can beat stale memory cache
                var persistent = await PersistentSnapshotStore.LoadAsync(localKey).ConfigureAwait(true);
                var diskFull = persistent != null && SnapshotMergeHelper.IsFullSnapshot(persistent);
                WmiSnapshot? diskSnap = diskFull && persistent?.Metrics != null
                    ? EnrichMetricsFromSnapshot(persistent)
                    : null;
                var diskMetricsFull = diskSnap != null && SnapshotMergeHelper.IsFullWmiMetrics(diskSnap);

                WmiSnapshot? bestMetrics = null;
                string instantSource = "None";
                if (memFull && diskMetricsFull)
                {
                    bestMetrics = SnapshotMergeHelper.MergeWmiPreferNewer(memSnap, diskSnap);
                    instantSource = SnapshotMergeHelper.IsWmiSnapshotNewer(memSnap!, diskSnap!)
                        ? "Memory"
                        : "Disk";
                }
                else if (diskMetricsFull)
                {
                    bestMetrics = diskSnap;
                    instantSource = "Disk";
                }
                else if (memFull)
                {
                    bestMetrics = memSnap;
                    instantSource = "Memory";
                }

                if (bestMetrics != null && SnapshotMergeHelper.HasDisplayableData(bestMetrics))
                {
                    _dashboardState.SetSectionTimestamps(persistent?.SectionTimestamps ?? new SnapshotSectionTimestamps());
                    if (persistent == null)
                        _smartInsights.Clear();

                    ApplyDashboardFromCachedMetrics(server, bestMetrics, localKey, refreshing: true);
                    if (persistent != null && SnapshotMergeHelper.HasSnapshotUiContent(persistent))
                        ApplyPersistentEnrichment(server, persistent);

                    _serverHistory[localKey] = new List<WmiSnapshot> { bestMetrics };
                    LogSnapshotLoaded(localKey, instantSource, bestMetrics.CollectedAt.ToUniversalTime());
                    LogSnapshotApplied(localKey, AnalysisSnapshotMapper.DescribeSections(
                        new PersistentServerSnapshot { Metrics = bestMetrics, Services = bestMetrics.Services?.ToList() ?? new() }));
                    hadInstantData = true;
                }
                else if (persistent != null)
                {
                    _dashboardState.SetSectionTimestamps(persistent.SectionTimestamps ?? new SnapshotSectionTimestamps());

                    if (diskFull && !memFull)
                    {
                        ApplyPersistentSnapshotToUi(server, persistent, refreshing: true);
                        _serverHistory[localKey] = new List<WmiSnapshot> { _lastSnapshot ?? EnrichMetricsFromSnapshot(persistent) };
                        hadInstantData = true;
                    }
                    else if (!hadInstantData && SnapshotMergeHelper.HasSnapshotUiContent(persistent))
                    {
                        ApplyPersistentSnapshotToUi(server, persistent, refreshing: true);
                        hadInstantData = true;
                    }
                    else if (!hadInstantData)
                    {
                        ApplyPersistentSnapshotToUi(server, persistent, refreshing: true);
                        hadInstantData = SnapshotMergeHelper.HasSnapshotUiContent(persistent);
                    }

                    if (persistent.Metrics != null && diskFull)
                        _serverHistory[localKey] = new List<WmiSnapshot> { _lastSnapshot ?? EnrichMetricsFromSnapshot(persistent) };
                }
                else if (!hadInstantData)
                {
                    _dashboardState.SetSectionTimestamps(new SnapshotSectionTimestamps());
                    _smartInsights.Clear();
                    // D) Minimum from connections.json
                    ApplyMinimumDashboardFromServer(server);
                }
                }

                // E) Live refresh — only after snapshot/minimum UI is visible
                _refreshGeneration = _refreshLifecycle!.Begin(localKey);
                _dashboardState.ActivateServer(localKey, _refreshGeneration);
                StartInsightsRefresh(server);
                SyncTreeSelectionToServer(server);
                ApplyActiveDashboardBadges();
                UpdateRdpToolbarState();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void ActivateDashboardForServer(ServerInfo server)
        {
            if (server == null || HasOpenRdpTab())
                return;

            _activeDashboardServer = server;
            _selectedServer = server;
            EnsureDashboardPanelVisible();
            AuditLogService.Log(
                AuditActions.ServerSelected,
                AuditCategories.Dashboard,
                AuditResults.Success,
                LogLevels.Information,
                server.Name,
                server.Ip,
                message: $"Server selected: {server.Name} ({server.Ip})");
            AuditLogService.Log(
                AuditActions.DashboardOpened,
                AuditCategories.Dashboard,
                AuditResults.Success,
                LogLevels.Information,
                server.Name,
                server.Ip,
                message: $"Dashboard opened for {server.Name} ({server.Ip})");
            ShowSelectedServerOverview(server);
            ApplyActiveWorkspaceContent();
        }

        private void HideSelectedServerOverview()
        {
            _activeDashboardServer = null;
            _selectedServer = null;
            ApplyActiveDashboardBadges();
            RestoreDashPanelIfExpanded();
            try { SelectedServerPanel.Visibility = Visibility.Collapsed; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try
            {
                foreach (var session in _rdpSessions.ToList())
                {
                    try { session.CleanSession?.Disconnect(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            StopInsightsRefresh();
            CancelDeferredDeepWork();
            UpdateRdpToolbarState();
        }

        private void StartInsightsRefresh(ServerInfo server)
    {
        try
        {
            var key = GetServerKey(server);

            // Aynı sunucu için timer zaten varsa durdurulmuş olabilir (RDP sonrası); yeniden başlat.
            if (_insightsTimer != null && string.Equals(_insightsKey, key, StringComparison.OrdinalIgnoreCase))
            {
                if (_insightsCts == null || _insightsCts.IsCancellationRequested)
                {
                    _insightsCts?.Dispose();
                    _insightsCts = new CancellationTokenSource();
                }

                if (!_insightsTimer.IsEnabled)
                    _insightsTimer.Start();

                _ = RefreshInsightsTick(server, _insightsCts!.Token, force: false);
                return;
            }

            StopInsightsRefresh();
            _insightsKey = key;
            _insightsCts = new CancellationTokenSource();

            _insightsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(5, SettingsService.Instance.Current.DefaultRefreshIntervalSeconds))
            };
            _insightsTimer.Tick += async (_, _) =>
            {
                await RefreshInsightsTick(server, _insightsCts.Token, force: false);
            };
            _insightsTimer.Start();

            // İlk açılış: önbellek varsa onu kullan; arka planda hafif yenileme.
            _insightsUiThrottle.Reset();
            _ = RefreshInsightsTick(server, _insightsCts.Token, force: false);
        }
        catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
    }

        private void StopInsightsRefresh()
        {
            try
            {
                if (_insightsTimer != null)
                {
                    _insightsTimer.Stop();
                    _insightsTimer = null;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { _insightsCts?.Cancel(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { _insightsCts?.Dispose(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            _insightsCts = null;
            _insightsKey = null;
            _dashboardState.ClearActiveLiveRefresh();
            _refreshLifecycle?.Cancel();
            ResetLiveChartSession(null);
            CancelDeferredDeepWork();
            _ = PersistentSnapshotStore.FlushAsync();
        }



        /// <summary>
        /// RDP bağlantısı kurulurken tüm arka plan taramalarını durdurur.
        /// Timer durmaz, in-flight async task'lar da CancellationToken ile iptal edilir.
        /// </summary>
        private void PauseBackgroundScans()
        {
            _rdpActive = true; // volatile: tüm thread'ler hemen görür

            // 1. Timer'ları durdur (yeni tick başlatılmasın)
            try { _sidebarStatusTimer?.Stop(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { _insightsTimer?.Stop(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            // 2. In-flight insights task'ını iptal et
            try { _insightsCts?.Cancel(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            // 3. In-flight background scan task'ını iptal et
            try { _backgroundScanCts.Cancel(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { _backgroundScanCts.Dispose(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            _backgroundScanCts = new CancellationTokenSource();
        }

        /// <summary>
        /// RDP bağlantısı kapandıktan sonra arka plan taramalarını yeniden başlatır.
        /// </summary>
        private void ResumeBackgroundScans()
        {
            if (HasAnyActiveSession())
                return;

            _rdpActive = false; // volatile: tüm thread'ler hemen görür

            // Yeni CTS hazır olduğundan emin ol
            if (_backgroundScanCts.IsCancellationRequested)
            {
                try { _backgroundScanCts.Dispose(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                _backgroundScanCts = new CancellationTokenSource();
            }

            try { _sidebarStatusTimer?.Start(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            var server = _activeDashboardServer ?? _selectedNode?.Server;
            if (server != null)
                StartInsightsRefresh(server);
        }

        private async Task RefreshInsightsTick(ServerInfo server, CancellationToken ct, bool force = false)
        {
            if (ct.IsCancellationRequested) return;

            if (!force && HasAnyActiveSession()) return;

            if (Interlocked.CompareExchange(ref _insightsTickInFlight, 1, 0) != 0)
                return;

            try
            {
                await RefreshInsightsAsync(server, ct, force: force);
            }
            finally
            {
                Interlocked.Exchange(ref _insightsTickInFlight, 0);
            }
        }

        private bool TryBeginInsightsRefresh(ServerInfo server, out string localKey)
        {
            localKey = GetServerKey(server);
            return _refreshCoordinator.TryBegin(localKey, ServerRefreshCoordinator.RefreshPhase.FastMetrics);
        }

        private void EndInsightsRefresh(string localKey)
        {
            _refreshCoordinator.End(localKey, ServerRefreshCoordinator.RefreshPhase.FastMetrics);
        }

        private async Task RefreshInsightsAsync(ServerInfo server, CancellationToken ct, bool force = false)
        {
            if (!TryBeginInsightsRefresh(server, out var localKey))
                return;

            try
            {
                using var perf = PerformanceTrace.Start("RefreshInsightsAsync");
                await RefreshInsightsAsyncCore(server, localKey, ct, force);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Dashboard refresh failed.", ex, category: "Refresh", action: "Insights");
                await Dispatcher.InvokeAsync(() =>
                {
                    if (string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase))
                        _refreshLifecycle?.CompleteFailed(_refreshGeneration, localKey);
                });
            }
            finally
            {
                EndInsightsRefresh(localKey);
            }
        }

        private async Task RefreshInsightsAsyncCore(ServerInfo server, string localKey, CancellationToken ct, bool force = false)
        {
            if (!force && HasAnyActiveSession())
                return;

            if (ct.IsCancellationRequested) return;

            if (force)
            {
                _dashboardLoadStartedUtc = DateTime.UtcNow;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_insightsKey == localKey)
                        UpdateStaleDataBanner(_sectionTimestamps.MetricsUtc ?? DateTime.UtcNow, refreshing: true);
                });
            }

            var appSettings = SettingsService.Instance.Current;
            if (!appSettings.EnableLiveMonitoring && !force)
            {
                CompleteRefreshCycleIfCurrent(localKey);
                return;
            }
            if (!appSettings.EnableBackgroundTasks && !force)
            {
                CompleteRefreshCycleIfCurrent(localKey);
                return;
            }
            if (appSettings.LimitRefreshRate && !force)
            {
                var minGap = TimeSpan.FromSeconds(Math.Max(5, appSettings.DefaultRefreshIntervalSeconds));
                if (DateTime.UtcNow - _lastInsightsRefreshUtc < minGap)
                {
                    CompleteRefreshCycleIfCurrent(localKey);
                    return;
                }
            }
            _lastInsightsRefreshUtc = DateTime.UtcNow;

            _insightsUiThrottle.Reset();

            WmiSnapshot? snap = null;
            var now = DateTime.Now;
            long? pingMs = -1L;
            var rdpOpen = false;

            var host = server.Ip;
            var port = server.Port <= 0 ? 3389 : server.Port;

            // Start all tasks - COMPLETE BACKGROUND ISOLATION
            var pingTask = appSettings.MonitorNetwork
                ? TryPingMsAsync(host, timeoutMs: 1200)
                : Task.FromResult<long?>(-1L);
            var rdpTask = TryTcpOpenAsync(host, port, timeoutMs: 1200);
            var agentTask = TryFetchSnapshotViaAgentAsync(server, ct);
            var agentHealthTask = RefreshAgentHealthAsync(server, ct);

            // Wait for INITIAL fast checks
            await Task.WhenAll(pingTask, rdpTask);
            pingMs = await pingTask.ConfigureAwait(false);
            rdpOpen = await rdpTask.ConfigureAwait(false);

            // Phase 0 (~1 sn): ping/RDP status strip
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateUiWithSnapshot(null, server, localKey, pingMs, rdpOpen, now, ct, DashboardUpdateTier.Placeholder);
            });

            // Wait for Agent
            snap = await agentTask;
            _ = agentHealthTask;

            // Fallback to WMI only if agent is not available/failed.
            if ((snap == null || !snap.Connected) && !IsAgentAuthenticationFailure(snap))
            {
                if (ct.IsCancellationRequested) return;
                if (!force && _rdpActive) return;
                var credentials = SettingsApplicator.ResolveCredentials(server, SettingsRuntime.Current);
                snap = await WmiCollector.GetSnapshotAsync(server.Ip, credentials.Username, credentials.Password, ct,
                    onProgress: null,
                    force: force, includeEventLogs: false);
            }

            if (snap != null && snap.Connected)
                snap.PingMs = pingMs.HasValue ? (int)Math.Min(pingMs.Value, int.MaxValue) : -1;

            var mergedSnap = ApplyIncomingSnapshotForUi(snap, localKey);
            if (mergedSnap != null && SnapshotMergeHelper.HasDisplayableData(mergedSnap))
            {
                if (snap != null && snap.Connected && mergedSnap.PingMs < 0 && pingMs.HasValue)
                    mergedSnap.PingMs = (int)Math.Min(pingMs.Value, int.MaxValue);
            }
            else
            {
                mergedSnap = _lastSnapshot ?? snap;
            }

            // Phase 1 (1-5 sn): temel CPU/RAM/Disk/servisler
            var basicSnap = mergedSnap;
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateUiWithSnapshot(basicSnap, server, localKey, pingMs, rdpOpen, now, ct, DashboardUpdateTier.Basic);
            });

            // Phase 2 (5+ sn): event log, intelligence, health score — arka planda
            if (basicSnap != null && SnapshotMergeHelper.HasDisplayableData(basicSnap) && !ct.IsCancellationRequested)
            {
                CancelDeferredDeepWork();
                _deferredDeepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = RunDeferredDeepPhaseAsync(server, basicSnap, localKey, pingMs, rdpOpen, now, _deferredDeepCts.Token, _refreshGeneration, skipDelay: force);
            }
            else
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_insightsKey != localKey)
                        return;
                    _refreshLifecycle?.Report(_refreshGeneration, localKey, LiveRefreshMilestone.EventLog);
                    _refreshLifecycle?.MarkAiSkipped(_refreshGeneration, localKey, "no-deep-metrics");
                    _refreshLifecycle?.CompleteSuccess(_refreshGeneration, localKey);
                });
            }

            SettingsApplicator.OptimizeMemoryIfEnabled(appSettings);
        }

        private void CompleteRefreshCycleIfCurrent(string localKey)
        {
            if (string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase))
                _refreshLifecycle?.CompleteSuccess(_refreshGeneration, localKey);
        }

        private async Task TryDeferredEventLogEnrichmentAsync(
            ServerInfo server,
            WmiSnapshot snap,
            string localKey,
            long? pingMs,
            bool rdpOpen,
            DateTime now,
            CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested || _rdpActive)
                    return;

                var credentials = SettingsApplicator.ResolveCredentials(server, SettingsRuntime.Current);
                var enriched = await WmiCollector.TryEnrichEventLogsAsync(
                    server.Ip, credentials.Username, credentials.Password, snap, ct).ConfigureAwait(false);

                if (!enriched || ct.IsCancellationRequested)
                    return;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested || _insightsKey != localKey)
                        return;

                    UpdateTimeline(snap, SettingsService.Instance.Current, isFinal: true);
                    server.LastEvent = snap.Timeline.FirstOrDefault()?.Message ?? "Son olay yok";
                    _sectionTimestamps.TimelineUtc = DateTime.UtcNow;
                    SchedulePersistSnapshot(localKey, snap);
                    _ = PersistFullSnapshotAsync(localKey, snap);
                    UpdateInsightsStatusStrip(server, localKey, pingMs, rdpOpen, now, ct);
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when switching servers or stopping refresh.
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Deferred event log enrichment failed.", ex, category: "WMI", action: "EnrichEventLogs");
            }
        }

        internal static bool IsAgentAuthenticationFailureForTests(WmiSnapshot? snapshot) => IsAgentAuthenticationFailure(snapshot);

        private static bool IsAgentAuthenticationFailure(WmiSnapshot? snapshot) =>
            snapshot?.DataSource == SnapshotDataSources.Agent &&
            string.Equals(snapshot.Error, "Agent authentication failed.", StringComparison.Ordinal);

        private async Task<WmiSnapshot?> TryFetchSnapshotViaAgentAsync(ServerInfo server, CancellationToken ct)
        {
            try
            {
                var uri = AgentEndpointHelper.TryBuildMetricsUri(server.Ip, SettingsRuntime.Current);
                if (uri == null)
                    return null;

                using var req = new HttpRequestMessage(HttpMethod.Get, uri);

                var key = SettingsApplicator.ResolveAgentMetricsKey(SettingsRuntime.Current);
                if (string.IsNullOrWhiteSpace(key))
                    return null;

                req.Headers.TryAddWithoutValidation("X-Netvium-Key", key);

                using var resp = await _agentHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        return new WmiSnapshot { Connected = false, DataSource = SnapshotDataSources.Agent, Error = "Agent authentication failed." };
                    return null;
                }

                var json = await resp.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(json)) return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool connected = root.TryGetProperty("connected", out var connectedEl) && connectedEl.ValueKind == JsonValueKind.True;
                if (!connected) return new WmiSnapshot { Connected = false, DataSource = SnapshotDataSources.Agent };

                var snap = new WmiSnapshot { Connected = true, DataSource = SnapshotDataSources.Agent };

                if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                    snap.Error = errEl.GetString();

                if (root.TryGetProperty("diskUsedPct", out var diskUsedEl) && diskUsedEl.ValueKind == JsonValueKind.Number)
                    snap.DiskUsedPct = diskUsedEl.GetDouble();

                if (root.TryGetProperty("diskFreeGb", out var diskFreeEl) && diskFreeEl.ValueKind == JsonValueKind.Number)
                    snap.DiskFreeGb = diskFreeEl.GetDouble();

                if (root.TryGetProperty("cpuPct", out var cpuEl) && cpuEl.ValueKind == JsonValueKind.Number)
                    snap.CpuPct = cpuEl.GetInt32();

                if (root.TryGetProperty("ramUsedPct", out var ramUsedEl) && ramUsedEl.ValueKind == JsonValueKind.Number)
                    snap.RamUsedPct = ramUsedEl.GetDouble();

                if (root.TryGetProperty("ramFreeGb", out var ramFreeEl) && ramFreeEl.ValueKind == JsonValueKind.Number)
                    snap.RamFreeGb = ramFreeEl.GetDouble();

                if (root.TryGetProperty("uptimeSeconds", out var upEl) && upEl.ValueKind == JsonValueKind.Number)
                {
                    try { snap.Uptime = TimeSpan.FromSeconds(upEl.GetDouble()); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                }

                if (root.TryGetProperty("activeUsers", out var auEl) && auEl.ValueKind == JsonValueKind.Number)
                    snap.ActiveUsers = auEl.GetInt32();

                if (root.TryGetProperty("services", out var svcEl) && svcEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in svcEl.EnumerateArray())
                    {
                        var name = it.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                        var display = it.TryGetProperty("display", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                        var state = it.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(display) && !string.IsNullOrWhiteSpace(state))
                            snap.Services.Add(new ServiceRow(name!, display!, state!));
                    }
                }

                if (root.TryGetProperty("sessions", out var sesEl) && sesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in sesEl.EnumerateArray())
                    {
                        var user = it.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                        var state = it.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                        var idle = it.TryGetProperty("idle", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                        var logon = it.TryGetProperty("logon", out var lo) && lo.ValueKind == JsonValueKind.String ? lo.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(user))
                            snap.Sessions.Add(new SessionRow(user!, state ?? "-", idle ?? "-", logon ?? "-"));
                    }
                }

                // Agent metrics do not include Windows Event Log entries; the deferred WMI enrichment path fills this when available.
                snap.Timeline = new List<TimelineEvent>();
                snap.CollectedAt = DateTime.Now;

                return snap;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateUiWithSnapshot(WmiSnapshot? snap, ServerInfo server, string localKey, long? pingMs, bool rdpOpen, DateTime now, CancellationToken ct, DashboardUpdateTier tier = DashboardUpdateTier.Deep)
        {
            using var perf = PerformanceTrace.Start($"UpdateUiWithSnapshot:{tier}");
            try
            {
                _lastDashboardRdpOpen = rdpOpen;
                if (ct.IsCancellationRequested) return;

                var (badge, reason) = CalculateOverallHealth(pingMs, rdpOpen, snap);
                UpdateNodeStatus(server, badge);

                if (_insightsKey != localKey) return;

                if (tier == DashboardUpdateTier.Placeholder)
                {
                    UpdateInsightsStatusStrip(server, localKey, pingMs, rdpOpen, now, ct, snap, badge, reason);
                    return;
                }

                if (tier == DashboardUpdateTier.Deep
                    && IsLiveMetricsActive(localKey)
                    && _lastSnapshot != null)
                {
                    ApplyDeepDashboardEnrichment(server, localKey, _lastSnapshot, pingMs, rdpOpen, now, ct);
                    return;
                }

                var telemetryError = snap?.Error;
                snap = IsLiveMetricsActive(localKey)
                    ? ApplyIncomingSnapshotForUi(snap, localKey)
                    : ResolveEffectiveSnapshot(snap, localKey);
                if (snap != null && string.IsNullOrWhiteSpace(snap.Ip))
                    snap.Ip = server.Ip;
                if (snap == null || !SnapshotMergeHelper.HasDisplayableData(snap))
                {
                    if (tier == DashboardUpdateTier.Basic || tier == DashboardUpdateTier.Deep)
                    {
                        _refreshLifecycle?.CompleteFailed(_refreshGeneration, localKey);
                        ShowTelemetryUnavailableState(telemetryError, pingMs, rdpOpen);
                        EvaluateAlerts(server, null, pingMs);
                    }
                    return;
                }

                var rdpText = rdpOpen ? "Açık" : "Kapalı";
                var pingText = pingMs.HasValue ? $"{pingMs.Value} ms" : "-";

                HealthBadgeDot.Background = GetBadgeBrush(badge);
                ApplySelectedStatusBadge(badge);
                SelectedStatusText.Text = badge.ToString().ToUpper();
                ApplyHealthGaugeVisual(CalculateHealthScore(pingMs, snap, rdpOpen).Score);

                var statusPrefix = badge switch
                {
                    HealthBadge.Healthy => "Healthy",
                    HealthBadge.Warning => "Warning",
                    HealthBadge.Critical => "Critical",
                    _ => "Offline"
                };

                if (tier == DashboardUpdateTier.Basic)
                    UpdateStaleDataBanner(DateTime.UtcNow, refreshing: true);
                else
                    HealthSummaryText.Text = $"{statusPrefix}{(reason != null ? ": " + reason : "")} | Ping: {pingText} | RDP: {rdpText} | Son Kontrol: {now:HH:mm}";

                SetTextIfChanged(SelectedTitleText, server.Name?.ToUpper() ?? "SUNUCU");
                SetTextIfChanged(SelectedCompatibilityText, snap!.Compatibility ?? "-");
                SetTextIfChanged(SelectedCpuCountText, snap.CPUCoreCount?.ToString() ?? "-");
                SetTextIfChanged(SelectedRamSizeText, snap.TotalRamGb.HasValue ? $"{snap.TotalRamGb.Value:0} GB" : "-");

                SetTextIfChanged(SelectedLastUserText, snap.LastUser ?? "Yok");
                SetTextIfChanged(SelectedLastLogonText, snap.LastLogon ?? "-");
                SetTextIfChanged(SelectedLastSessionText, snap.LastSession ?? "-");

                var activeAccountList = snap.Sessions
                    .Where(s => !string.IsNullOrEmpty(s.User) && s.User != "-")
                    .Select(s => s.User.Contains("\\") ? s.User.Split('\\').Last() : s.User)
                    .Distinct()
                    .ToList();
                SetTextIfChanged(ActiveAccountsValueText, activeAccountList.Any() ? string.Join(", ", activeAccountList) : "Yok");
                SyncInfoCompactContent();

                var ips = snap.IPAddresses ?? new List<string>();
                if (ips.Count == 0) ips.Add(server.Ip);
                _ipAddresses.SyncWithDiff(ips, (a, b) => a == b);

                _dashboardState.CommitUiSnapshot(localKey, snap, fromLivePath: IsLiveMetricsActive(localKey));
                snap = _lastSnapshot ?? snap;
                EvaluateAlerts(server, snap, pingMs);
                UpdateRdpToolbarState();
                UpdateDashboardAgentStatus(snap);

                var uiSettings = SettingsRuntime.Current;

                UpdateServicesList(snap, uiSettings);

                server.LastCpu = snap.CpuPct ?? 0;
                server.LastRam = snap.RamUsedPct.HasValue && snap.TotalRamGb.HasValue
                    ? (snap.RamUsedPct.Value / 100.0 * snap.TotalRamGb.Value)
                    : 0;
                server.LastDisk = snap.DiskUsedPct ?? 0;
                server.Latency = pingMs.HasValue ? $"{pingMs.Value}ms" : "Active";
                server.RdpSessions = snap.ActiveUsers?.ToString() ?? "0";

                if (tier == DashboardUpdateTier.Basic)
                {
                    _dashboardState.TouchMetricsSections();
                    UpdateStaleDataBanner(DateTime.UtcNow, refreshing: true);
                    _refreshLifecycle?.Report(_refreshGeneration, localKey, LiveRefreshMilestone.FastMetrics);
                    _refreshLifecycle?.Report(_refreshGeneration, localKey, LiveRefreshMilestone.Services);
                    WarmServerHistoryCache(localKey, snap);
                    UpdateLiveMetrics(server, localKey, snap, pingMs, addChartSample: true, recordNetwork: true);
                    StartLiveMetricSampling(server, localKey, pingMs);
                    if (IsLiveTelemetryAuthoritative(localKey))
                    {
                        if (_lastSnapshot != null && SnapshotMergeHelper.IsFullWmiMetrics(_lastSnapshot))
                            _ = PersistFullSnapshotAsync(localKey, _lastSnapshot);
                        else if (SnapshotMergeHelper.IsFullWmiMetrics(snap))
                            _ = PersistFullSnapshotAsync(localKey, snap);
                        else
                            SchedulePersistSnapshot(localKey, snap);
                    }
                    return;
                }

                server.AlertMessage = badge != HealthBadge.Healthy ? (reason ?? string.Empty) : string.Empty;
                server.AiInsight = IntelligenceEngine.GetQuickInsight(snap);
                server.LastEvent = snap.Timeline.FirstOrDefault()?.Message ?? "Son olay yok";

                var history = _serverHistory.GetOrAdd(localKey, _ => new List<WmiSnapshot>());
                lock (history)
                {
                    history.Add(snap);
                    SnapshotSecurityService.TrimHistoryInPlace(history);
                }
                UpdateOverviewDashboard();

                var snapFingerprint = ComputeSnapshotFingerprint(snap);
                var snapChanged = snapFingerprint != _lastIntelligenceFingerprint || localKey != _lastIntelligenceServerKey;
                if (snapChanged)
                {
                    UpdateIntelligenceUi(snap, server.Name ?? "SUNUCU", forceRegenerate: true);
                    _lastIntelligenceFingerprint = snapFingerprint;
                    _lastIntelligenceServerKey = localKey;
                }

                UpdateTimeline(snap, uiSettings, isFinal: true);

                if (tier == DashboardUpdateTier.Deep)
                {
                    _dashboardState.TouchTimelineSection();
                    var healthResult = CalculateHealthScore(pingMs, snap, rdpOpen);
                    HealthScoreValueText.Text = $"Bağlantı Sağlık Skoru: {healthResult.Score}/100";
                    HealthBreakdownText.Text = healthResult.Breakdown;
                    ApplyHealthGaugeVisual(healthResult.Score);

                    try
                    {
                        System.Windows.Media.Color scoreColor;
                        if (healthResult.Score >= 85)
                            scoreColor = System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80); // Ultra/Stable
                        else if (healthResult.Score >= 60)
                            scoreColor = System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24); // Warning
                        else
                            scoreColor = System.Windows.Media.Color.FromRgb(0xD4, 0x84, 0x8A); // Soft risk
                        HealthScoreValueText.Foreground = new SolidColorBrush(scoreColor);
                    }
                    catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

                    SchedulePersistSnapshot(localKey, snap);
                    _ = PersistFullSnapshotAsync(localKey, snap);
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }


        private void ShowTelemetryUnavailableState(string? error, long? pingMs, bool rdpOpen)
        {
            var reason = string.IsNullOrWhiteSpace(error)
                ? "WMI/Agent \u00fczerinden veri al\u0131namad\u0131. Kimlik bilgilerini ve uzak y\u00f6netim eri\u015fimini kontrol edin."
                : error.Trim();
            var pingText = pingMs is >= 0 ? $"{pingMs.Value} ms" : "yan\u0131t yok";
            HealthSummaryText.Text = $"Canl\u0131 telemetri al\u0131namad\u0131: {reason} Ping: {pingText}, RDP: {(rdpOpen ? "a\u00e7\u0131k" : "kapal\u0131")}.";

            if (PerformanceChartPlaceholder != null)
            {
                PerformanceChartPlaceholder.Text = "\uE783\nCanl\u0131 telemetri al\u0131namad\u0131";
                PerformanceChartPlaceholder.Visibility = Visibility.Visible;
            }
            if (PerformanceCanvas != null)
                PerformanceCanvas.Visibility = Visibility.Collapsed;
        }

        private void ApplyDeepDashboardEnrichment(
            ServerInfo server,
            string localKey,
            WmiSnapshot snap,
            long? pingMs,
            bool rdpOpen,
            DateTime now,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested || _insightsKey != localKey)
                return;

            var (badge, reason) = CalculateOverallHealth(pingMs, rdpOpen, snap);
            server.AlertMessage = badge != HealthBadge.Healthy ? (reason ?? string.Empty) : string.Empty;
            server.AiInsight = IntelligenceEngine.GetQuickInsight(snap);
            server.LastEvent = snap.Timeline.FirstOrDefault()?.Message ?? "Son olay yok";

            var uiSettings = SettingsRuntime.Current;
            UpdateServicesList(snap, uiSettings);

            var history = _serverHistory.GetOrAdd(localKey, _ => new List<WmiSnapshot>());
            lock (history)
            {
                if (!history.Any())
                    history.Add(snap);
                else if (SnapshotMergeHelper.ShouldAcceptIncomingVolatiles(snap, history[^1]))
                    history[^1] = SnapshotMergeHelper.MergeLiveOverBaseline(snap, history[^1]);
                SnapshotSecurityService.TrimHistoryInPlace(history);
            }

            var snapFingerprint = ComputeSnapshotFingerprint(snap);
            var snapChanged = snapFingerprint != _lastIntelligenceFingerprint || localKey != _lastIntelligenceServerKey;
            if (snapChanged)
            {
                UpdateIntelligenceUi(snap, server.Name ?? "SUNUCU", forceRegenerate: true);
                _lastIntelligenceFingerprint = snapFingerprint;
                _lastIntelligenceServerKey = localKey;
            }

            UpdateTimeline(snap, uiSettings, isFinal: true);

            _sectionTimestamps.TimelineUtc = DateTime.UtcNow;
            var healthResult = CalculateHealthScore(pingMs, snap, rdpOpen);
            HealthScoreValueText.Text = $"Bağlantı Sağlık Skoru: {healthResult.Score}/100";
            HealthBreakdownText.Text = healthResult.Breakdown;
            ApplyHealthGaugeVisual(healthResult.Score);

            try
            {
                System.Windows.Media.Color scoreColor;
                if (healthResult.Score >= 85)
                    scoreColor = System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80);
                else if (healthResult.Score >= 60)
                    scoreColor = System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24);
                else
                    scoreColor = System.Windows.Media.Color.FromRgb(0xD4, 0x84, 0x8A);
                HealthScoreValueText.Foreground = new SolidColorBrush(scoreColor);
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            if (_lastSnapshot != null && SnapshotMergeHelper.IsFullWmiMetrics(_lastSnapshot))
            {
                SchedulePersistSnapshot(localKey, _lastSnapshot);
                _ = PersistFullSnapshotAsync(localKey, _lastSnapshot);
            }
        }

        private void UpdateInsightsStatusStrip(
            ServerInfo server,
            string localKey,
            long? pingMs,
            bool rdpOpen,
            DateTime now,
            CancellationToken ct,
            WmiSnapshot? snap = null,
            HealthBadge? badgeOverride = null,
            string? reasonOverride = null)
        {
            if (ct.IsCancellationRequested || _insightsKey != localKey)
                return;

            HealthBadge badge;
            string? reason;
            if (badgeOverride.HasValue)
            {
                badge = badgeOverride.Value;
                reason = reasonOverride;
            }
            else
            {
                (badge, reason) = CalculateOverallHealth(pingMs, rdpOpen, snap);
            }
            HealthBadgeDot.Background = GetBadgeBrush(badge);
            ApplySelectedStatusBadge(badge);
            SelectedStatusText.Text = badge.ToString().ToUpper();
            if (snap != null)
                ApplyHealthGaugeVisual(CalculateHealthScore(pingMs, snap, rdpOpen).Score);

            var pingText = pingMs.HasValue ? $"{pingMs.Value} ms" : "-";
            var rdpText = rdpOpen ? "Açık" : "Kapalı";
            var statusPrefix = badge switch
            {
                HealthBadge.Healthy => "Healthy",
                HealthBadge.Warning => "Warning",
                HealthBadge.Critical => "Critical",
                _ => "Offline"
            };

            HealthSummaryText.Text = $"{statusPrefix}{(reason != null ? ": " + reason : "")} | Ping: {pingText} | RDP: {rdpText} | Son Kontrol: {now:HH:mm}";
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Walk up from the clicked element to find the DataGridRow and select it
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
                dep = VisualTreeHelper.GetParent(dep);
            if (dep is DataGridRow row)
            {
                row.IsSelected = true;
                // Also set focus so SelectedItem updates
                if (row.DataContext != null)
                {
                    var grid = ItemsControl.ItemsControlFromItemContainer(row) as DataGrid;
                    if (grid != null) grid.SelectedItem = row.DataContext;
                }
            }
        }

        private async void OnLogoffSessionClick(object sender, RoutedEventArgs e)
        {
            SessionRow? session = SessionsGrid.SelectedItem as SessionRow;
            var server = _selectedServer ?? _selectedNode?.Server;

            if (session != null && server != null && session.Id.HasValue)
            {
                AuditLogService.LogRequest(
                    AuditActions.Logoff,
                    AuditCategories.RemoteAdmin,
                    server,
                    $"Session logoff requested for {session.User}.",
                    JsonSerializer.Serialize(new { session.User, session.Id, session.State }));
                if (!ConfirmActionDialog.Show(this, ConfirmActionKind.LogoffSession, server.Name, server.Ip, session.User))
                    return;

                UpdateConnectionStatus($"{session.User} oturumu kapatılıyor...", true);
                using var audit = AuditOperationScope.BeginRemoteAdmin(AuditActions.Logoff, server);
                var (success, message) = await WmiCollector.ExecuteSessionActionAsync(server.Ip, server.Username, server.Password, session.Id.Value, "logoff");
                if (success)
                    audit.CompleteSuccess();
                else
                    audit.CompleteFailed(message);
                UpdateConnectionStatus(success ? "Oturum kapatma komutu gönderildi." : message, success);
                if (!success) System.Windows.MessageBox.Show(message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                else System.Windows.MessageBox.Show("Oturum kapatma komutu başarıyla gönderildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                if (success) {
                    await Task.Delay(2000);
                    _ = RefreshInsightsAsync(server, CancellationToken.None);
                }
            }
        }



        private async void OnRefreshAiAnalysisClick(object sender, RoutedEventArgs e)
        {
            var server = _selectedServer ?? _selectedNode?.Server;
            if (server == null) return;

            if (string.IsNullOrWhiteSpace(SettingsApplicator.ResolveAiApiKey(SettingsRuntime.Current)))
            {
                ShowToast("AI API anahtarı yapılandırılmamış. Ayarlar > AI bölümünden ekleyin.", "⚠️");
                return;
            }
            
            var localKey = GetServerKey(server);
            var remaining = InsightEngine.GetAiCooldownRemaining(localKey);
            if (remaining > TimeSpan.Zero || InsightEngine.IsAiRequestInFlight(localKey))
            {
                var waitText = remaining > TimeSpan.Zero ? $"Tekrar denemek için {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} saniye bekleyin." : "AI analizi hâlen çalışıyor.";
                ShowToast(waitText, "ℹ️", 4000);
                return;
            }
            _lastAiAnalysisTime = DateTime.MinValue;
            _sectionTimestamps.AiAnalysisUtc = null;

            UpdateConnectionStatus("AI Analizi yeniden başlatılıyor...", false);
            AiAnalysisStatusText.Text = "Yenileniyor...";
            AiAnalysisStatusText.Visibility = Visibility.Visible;
            RequestSmartAnalysis(server, force: true);
        }

        private void RequestSmartAnalysis(ServerInfo? server, bool force = false)
        {
            server ??= _selectedServer ?? _selectedNode?.Server;
            if (server == null) return;

            var localKey = GetServerKey(server);
            var snap = _lastSnapshot ?? GetLatestCachedSnapshot(server);
            var rdpOpen = _lastDashboardRdpOpen;

            if (snap == null)
            {
                _refreshLifecycle?.MarkAiSkipped(_refreshGeneration, localKey, "no-snapshot");
                ShowToast("AI analizi için sunucu verisi gerekli. Önce WMI yenilemesini bekleyin.", "⚠️");
                return;
            }

            var aiWillRun = FeatureEntitlementService.Instance.IsAiAnalysisEnabled(SettingsRuntime.Current)
                && !string.IsNullOrWhiteSpace(SettingsApplicator.ResolveAiApiKey(SettingsRuntime.Current))
                && InsightEngine.HasSufficientLiveDataForAi(snap, rdpOpen, snap.PingMs >= 0 ? snap.PingMs : null)
                && (force
                    || SnapshotTtl.IsExpired(_sectionTimestamps.AiAnalysisUtc, SnapshotTtl.AiAnalysis)
                    || !SettingsRuntime.Current.AiCacheEnabled
                    || !_smartInsights.Any(x => x.IsSectionHeader));

            if (!aiWillRun)
                _refreshLifecycle?.MarkAiSkipped(_refreshGeneration, localKey, "skipped-or-cached");

            List<WmiSnapshot>? history = null;
            if (_serverHistory.TryGetValue(localKey, out var h))
                history = h;

            long? pingMs = snap.PingMs >= 0 ? snap.PingMs : null;
            TriggerAiAnalysis(snap, server, localKey, history, pingMs, rdpOpen, _insightsCts?.Token ?? CancellationToken.None, force);
        }

        private static void SetTextIfChanged(System.Windows.Controls.TextBlock? block, string value)
        {
            if (block == null) return;
            if (!string.Equals(block.Text, value, StringComparison.Ordinal))
                block.Text = value;
        }

        internal void SyncInfoCompactContent()
        {
            if (InfoCompactOsText == null)
                return;

            var snap = _lastSnapshot;
            var server = _selectedServer ?? _selectedNode?.Server ?? _activeDashboardServer;

            static string OrDash(string? value) =>
                string.IsNullOrWhiteSpace(value) || value is "—" or "-" ? "—" : value.Trim();

            var os = snap?.OSName;
            if (!SnapshotMergeHelper.IsMeaningfulOs(os))
                os = SelectedOsText?.Text;
            os = OrDash(os);

            var hostname = OrDash(server?.Name ?? SelectedTitleText?.Text);
            var memory = snap?.TotalRamGb is > 0
                ? $"{snap.TotalRamGb.Value:0} GB"
                : OrDash(SelectedRamSizeText?.Text?.Replace(" GB", "", StringComparison.Ordinal));
            var cpu = snap?.CPUCoreCount?.ToString()
                ?? OrDash(SelectedCpuCountText?.Text?.Replace(" core", "", StringComparison.OrdinalIgnoreCase));
            var disk = snap?.DiskUsedPct is >= 0
                ? $"{snap.DiskUsedPct.Value:0}%"
                : OrDash(HeaderDiskValue?.Text);
            var ip = OrDash(!string.IsNullOrWhiteSpace(snap?.Ip) ? snap!.Ip : server?.Ip ?? SelectedDnsNameText?.Text);
            string uptime;
            var serverKey = server != null ? GetServerKey(server) : null;
            if (!string.IsNullOrEmpty(serverKey)
                && IsLiveMetricsActive(serverKey)
                && snap?.Uptime is TimeSpan liveUptime)
            {
                uptime = OrDash(LiveDashboardMetricsBuilder.FormatUptimePair(liveUptime).Text);
            }
            else
            {
                uptime = OrDash(DashUptimeText?.Text);
            }
            var lastUser = OrDash(snap?.LastUser ?? SelectedLastUserText?.Text);

            SetTextIfChanged(InfoCompactOsText, os);
            SetTextIfChanged(InfoCompactHostnameText, hostname);
            SetTextIfChanged(InfoCompactMemoryText, memory);
            SetTextIfChanged(InfoCompactCpuText, cpu);
            SetTextIfChanged(InfoCompactIpText, ip);
            SetTextIfChanged(InfoCompactUptimeText, uptime);
            SetTextIfChanged(InfoCompactLastUserText, lastUser);
            SetTextIfChanged(SelectedLastUserText, lastUser);

            if (InfoCompactDiskText != null)
            {
                SetTextIfChanged(InfoCompactDiskText, disk);
                if (snap?.DiskUsedPct is >= 90)
                    InfoCompactDiskText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));
                else if (snap?.DiskUsedPct is >= 70)
                    InfoCompactDiskText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24));
                else
                    InfoCompactDiskText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24));
            }

            if (!string.Equals(os, "—", StringComparison.Ordinal))
                InfoCompactOsText.ToolTip = os;
        }

        private static string ComputeSnapshotFingerprint(WmiSnapshot snap)
        {
            var timelineTick = snap.Timeline?.FirstOrDefault()?.Timestamp.Ticks ?? 0;
            var svcSig = snap.Services?.Count > 0
                ? string.Join("|", snap.Services.Take(8).Select(s => $"{s.Display}:{s.State}"))
                : "0";
            return $"{snap.CpuPct}|{snap.RamUsedPct:F1}|{snap.DiskUsedPct:F1}|{snap.PingMs}|{snap.ActiveUsers}|{timelineTick}|{svcSig}";
        }

        private static string ComputeServicesFingerprint(WmiSnapshot snap, bool monitorServices)
        {
            if (!monitorServices) return "disabled";
            if (snap.Services == null || snap.Services.Count == 0) return "empty";
            return string.Join("|", snap.Services.Select(s => $"{s.Display}:{s.State}:{s.StartMode}"));
        }

        private static string ComputeTimelineFingerprint(WmiSnapshot snap)
        {
            if (snap.Timeline == null || snap.Timeline.Count == 0) return "empty";
            var first = snap.Timeline[0];
            return $"{snap.Timeline.Count}|{first.Timestamp.Ticks}|{first.Message?.Length ?? 0}";
        }

        private void ResetChartRedrawState()
        {
            _lastChartCpuCount = 0;
            _lastChartRamCount = 0;
            _lastChartCpuTail = int.MinValue;
            _lastChartRamTail = double.MinValue;
            _chartUpdateThrottle.Reset();
        }

        private void ResetLiveChartSession(string? serverKey)
        {
            _liveMetricSampler.Cancel();
            _networkTracker.Reset(serverKey);
            _liveChartSession.Reset(serverKey);
            _liveChartServerKey = serverKey;
            _lastDrawnSampleCount = 0;
            ResetChartRedrawState();
            ClearPerformanceChartVisuals();
            UpdatePerformanceChartPlaceholder();
        }

        private void ScheduleDeferredChartUpdate()
        {
            if (_chartRedrawScheduled)
                return;

            _chartRedrawScheduled = true;
            Dispatcher.BeginInvoke(() =>
            {
                _chartRedrawScheduled = false;
                UpdatePerformanceChart();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ClearPerformanceChartVisuals()
        {
            try
            {
                CpuPolyline.Points.Clear();
                RamPolyline.Points.Clear();
                DiskPolyline.Points.Clear();
                NetworkPolyline.Points.Clear();
                CpuLastDot.Visibility = Visibility.Collapsed;
                RamLastDot.Visibility = Visibility.Collapsed;
                DiskLastDot.Visibility = Visibility.Collapsed;
                NetworkLastDot.Visibility = Visibility.Collapsed;
                HidePerformanceTooltip();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void UpdatePerformanceChartPlaceholder()
        {
            try
            {
                if (PerformanceChartPlaceholder == null || PerformanceCanvas == null)
                    return;

                var count = _liveChartSession.SampleCount;
                var hasSnapshotMetrics = _lastSnapshot != null && (
                    _lastSnapshot.CpuPct.HasValue
                    || _lastSnapshot.RamUsedPct.HasValue
                    || _lastSnapshot.DiskUsedPct.HasValue);

                if ((!_liveChartSession.IsActive || count == 0) && !hasSnapshotMetrics)
                {
                    PerformanceChartPlaceholder.Text = "\uE9D2\nWaiting for live telemetry";
                    PerformanceChartPlaceholder.Visibility = Visibility.Visible;
                    PerformanceCanvas.Visibility = Visibility.Collapsed;
                    HidePerformanceTooltip();
                    return;
                }

                if (count == 0 && hasSnapshotMetrics)
                {
                    PerformanceChartPlaceholder.Visibility = Visibility.Collapsed;
                    PerformanceCanvas.Visibility = Visibility.Visible;
                    return;
                }

                PerformanceChartPlaceholder.Visibility = Visibility.Collapsed;
                PerformanceCanvas.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void RestoreCachedAiInsights(string localKey)
        {
            if (_smartInsights.Any(x => x.IsSectionHeader))
            {
                UpdateAiCacheStatusLabel(_sectionTimestamps.AiAnalysisUtc);
                return;
            }

            if (!_cachedAiInsightsByServer.TryGetValue(localKey, out var cached) || cached.Count == 0)
            {
                UpdateAiCacheStatusLabel(null);
                return;
            }

            _smartInsights.Clear();
            foreach (var ins in cached.OrderBy(x => x.SortOrder))
                _smartInsights.Add(ins);

            SmartAnalysisNormalText.Visibility = _smartInsights.Any(x => !x.IsSectionHeader)
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateAiCacheStatusLabel(_sectionTimestamps.AiAnalysisUtc);
        }

        private void CacheAiInsights(string localKey)
        {
            if (_smartInsights.Count == 0) return;
            _cachedAiInsightsByServer[localKey] = _smartInsights.ToList();
            _sectionTimestamps.AiAnalysisUtc = DateTime.UtcNow;
            if (_lastSnapshot != null && SnapshotMergeHelper.IsFullWmiMetrics(_lastSnapshot))
                _ = PersistFullSnapshotAsync(localKey, _lastSnapshot);
        }

        private void ApplySmartAnalysisInsights(List<SmartInsight> insights, bool showLoadingIndicator)
        {
            _smartInsights.Clear();
            foreach (var ins in insights.OrderBy(x => x.SortOrder))
                _smartInsights.Add(ins);

            if (showLoadingIndicator && !_smartInsights.Any(x => x.Icon == "🔍"))
            {
                _smartInsights.Add(new SmartInsight("AI Smart Analysis derinleştiriliyor...", InsightSeverity.Info, "🔍")
                {
                    SortKey = 9999
                });
            }

            SmartAnalysisNormalText.Visibility = _smartInsights.Any(x => !x.IsSectionHeader) ? Visibility.Collapsed : Visibility.Visible;
            UpdateAiWidgetState();
            RefreshAiExpandedWorkspaceIfVisible();
        }

        private void RefreshAiExpandedWorkspaceIfVisible()
        {
            if (AiExpandedAnalysisConsole?.Visibility == Visibility.Visible)
                UpdateAiExpandedAnalysisSummary();
        }

        private void UpdateAiExpandedAnalysisSummary()
        {
            if (AiExpandedAnalysisConsole == null)
                return;

            try
            {
                var server = _selectedServer ?? _activeDashboardServer ?? _selectedNode?.Server;
                var snap = _lastSnapshot;
                long? pingMs = snap?.PingMs >= 0 ? snap.PingMs : null;
                var health = CalculateHealthScore(pingMs, snap, _lastDashboardRdpOpen);

                var ctx = _lastAnalysisContext;
                if (ctx == null && snap != null && server != null)
                {
                    var localKey = GetServerKey(server);
                    List<WmiSnapshot>? history = null;
                    if (_serverHistory.TryGetValue(localKey, out var h))
                        history = h;
                    ctx = ServerAnalysisPipeline.Build(snap, server, localKey, pingMs, _lastDashboardRdpOpen, history, health.Score);
                }

                AiSmartAnalysisReport? report = _lastAiSmartReport;
                if (report == null && ctx != null)
                    report = AiAnalysisFormatter.BuildRuleBasedReport(ctx);
                else if (report != null && !report.FromAi && ctx != null
                         && (_lastAiSmartReport == null || !InsightEngine.IsQualityAiReport(report)))
                    report = AiAnalysisFormatter.BuildRuleBasedReport(ctx);

                var riskLabel = SelectedStatusText?.Text ?? "Unknown";
                var state = AiExpandedWorkspaceMapper.Build(
                    report,
                    ctx,
                    _currentIntelligence,
                    snap,
                    health.Score,
                    ctx?.RiskScore ?? 0,
                    riskLabel,
                    _sectionTimestamps.AiAnalysisUtc,
                    _lastAiAnalysisDuration,
                    _previousAiAnalysisUtc);

                AiExpandedAnalysisConsole.Bind(state);
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"AI expanded workspace update failed: {ex.Message}", category: "Dashboard", action: "AIExpanded");
            }
        }

        private void UpdateAiWidgetState()
        {
            try
            {
                var hasRisks = _currentIntelligence.Insights is { Count: > 0 };
                var hasRecommendations = _currentIntelligence.Recommendations is { Count: > 0 };

                if (IntelligenceInsightsList != null)
                    IntelligenceInsightsList.Visibility = hasRisks ? Visibility.Visible : Visibility.Collapsed;
                if (AiTopRiskEmptyText != null)
                    AiTopRiskEmptyText.Visibility = hasRisks ? Visibility.Collapsed : Visibility.Visible;

                if (IntelligenceRecommendationsList != null)
                    IntelligenceRecommendationsList.Visibility = hasRecommendations ? Visibility.Visible : Visibility.Collapsed;
                if (AiRecommendationEmptyText != null)
                    AiRecommendationEmptyText.Visibility = hasRecommendations ? Visibility.Collapsed : Visibility.Visible;

                if (SmartInsightList != null && SmartInsightList.ItemsSource == null)
                    SmartInsightList.ItemsSource = _smartInsights;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"AI widget state update failed: {ex.Message}", category: "Dashboard", action: "AI");
            }
        }

        private bool IsInsightsRefreshActive() => _refreshCoordinator.ShouldDeferSidebarScan();

        private void UpdateServicesList(WmiSnapshot snap, AppSettings uiSettings)
        {
            using var perf = PerformanceTrace.Start("UpdateServicesList");
            var fingerprint = ComputeServicesFingerprint(snap, uiSettings.MonitorServices);
            if (string.Equals(fingerprint, _lastServicesFingerprint, StringComparison.Ordinal))
                return;

            _lastServicesFingerprint = fingerprint;

            var newSessions = new List<SessionRow>();
            var userCounts = snap.Sessions
                .Where(s => !string.IsNullOrEmpty(s.User) && s.User != "-")
                .GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var s in snap.Sessions)
            {
                string risk = "Normal";
                if (!string.IsNullOrEmpty(s.User) && s.User != "-"
                    && userCounts.TryGetValue(s.User, out int cnt) && cnt > 1)
                    risk = "Duplicate";
                else if (TryParseIdleMinutes(s.Idle, out int idleMin) && idleMin > 120)
                    risk = "Idle";

                newSessions.Add(new SessionRow(s.User, s.State, s.Idle, s.Logon, risk));
            }
            _sessionRows.SyncWithDiff(newSessions, (a, b) => a.User == b.User && a.State == b.State && a.Risk == b.Risk);
            UpdateSessionsWidget(snap);

            if (uiSettings.MonitorServices)
                _serviceRows.SyncWithDiff(snap.Services, (a, b) => a.Display == b.Display && a.State == b.State);
            else
                _serviceRows.SyncWithDiff(new List<ServiceRow>(), (a, b) => a.Display == b.Display);

            RefreshServicesFilter();
            UpdateServicesWidgetState(snap, uiSettings);

            SetTextIfChanged(ActiveUsersBadgeText, snap.ActiveUsers.HasValue ? $"Aktif Kullanıcı: {snap.ActiveUsers.Value}" : "Aktif Kullanıcı: -");
        }

        private void OnServicesFilterChanged(object sender, RoutedEventArgs e) => RefreshServicesFilter();

        private void RefreshServicesFilter()
        {
            try
            {
                if (ServicesGrid == null)
                    return;

                var view = CollectionViewSource.GetDefaultView(_serviceRows);
                if (view == null)
                    return;

                view.Filter = item =>
                {
                    if (item is not ServiceRow service)
                        return false;

                    var query = ServicesSearchBox?.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(query)
                        && !service.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !service.Display.Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !service.State.Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !service.StartMode.Contains(query, StringComparison.OrdinalIgnoreCase))
                        return false;

                    var selected = (ServicesStateFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
                    return selected switch
                    {
                        "Running" => service.State.Equals("Running", StringComparison.OrdinalIgnoreCase),
                        "Stopped" => service.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase),
                        "Critical" => service.IsCritical,
                        _ => true
                    };
                };
                view.Refresh();
                UpdateServicesWidgetState(_lastSnapshot, SettingsRuntime.Current);
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Services filter failed: {ex.Message}", category: "Dashboard", action: "Services");
            }
        }

        private void UpdateServicesWidgetState(WmiSnapshot? snap, AppSettings settings)
        {
            try
            {
                if (ServicesGrid == null || ServicesEmptyText == null)
                    return;

                var view = CollectionViewSource.GetDefaultView(_serviceRows);
                var hasRows = _serviceRows.Count > 0 && view is { IsEmpty: false };
                if (hasRows)
                {
                    ServicesGrid.Visibility = Visibility.Visible;
                    ServicesEmptyText.Visibility = Visibility.Collapsed;
                    return;
                }

                ServicesGrid.Visibility = Visibility.Collapsed;
                ServicesEmptyText.Text = !settings.MonitorServices
                    ? "Service monitoring is disabled in settings."
                    : snap?.Connected == true && _serviceRows.Count > 0
                        ? "No services match the current search or filter."
                        : snap?.Connected == true
                            ? "No service records were returned in the latest WMI snapshot."
                            : "Service data requires a live WMI snapshot.";
                ServicesEmptyText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Services state update failed: {ex.Message}", category: "Dashboard", action: "Services");
            }
        }

        private void UpdateTimeline(WmiSnapshot snap, AppSettings uiSettings, bool isFinal)
        {
            using var perf = PerformanceTrace.Start("UpdateTimeline");
            if (!uiSettings.MonitorEventLogs)
                return;

            if (snap.Timeline != null && snap.Timeline.Any())
            {
                var fingerprint = ComputeTimelineFingerprint(snap);
                if (string.Equals(fingerprint, _lastTimelineFingerprint, StringComparison.Ordinal))
                    return;

                _lastTimelineFingerprint = fingerprint;
                _currentTimelinePageItems.Clear();
                _currentTimelinePageItems.AddRange(snap.Timeline);
                ShowTimelinePage(1);
                return;
            }

            if (!isFinal)
                return;

            if (_currentTimelinePageItems.Count <= 1 && (snap.Timeline == null || !snap.Timeline.Any())
                && _currentTimelinePageItems.All(x => x.Icon == "🔍" || x.Icon == "🛡️"))
            {
                _lastTimelineFingerprint = "stable";
                _currentTimelinePageItems.Clear();
                SetTimelineEmptyState("No significant Windows Event Log entries were found in the selected time window.");
            }
        }

        private void UpdateIntelligenceUi(WmiSnapshot snap, string serverName, bool forceRegenerate = false)
        {
            if (snap == null) return;

            var localKey = $"{snap.Ip}|{snap.Username}";
            var fingerprint = ComputeSnapshotFingerprint(snap);
            var regenerate = forceRegenerate
                || fingerprint != _lastIntelligenceFingerprint
                || !string.Equals(localKey, _lastIntelligenceServerKey, StringComparison.OrdinalIgnoreCase);

            if (regenerate)
            {
                List<WmiSnapshot>? history = null;
                if (_serverHistory.TryGetValue(localKey, out var h)) history = h;
                _currentIntelligence = IntelligenceEngine.GenerateIntelligence(snap, serverName, history);
                _lastIntelligenceFingerprint = fingerprint;
                _lastIntelligenceServerKey = localKey;
            }

            // Identity Section
            SetTextIfChanged(IntelligenceServerNameText, _currentIntelligence.ServerName);
            SetTextIfChanged(IntelligenceRoleText, _currentIntelligence.DetectedRole);
            SetTextIfChanged(IntelligenceConfidenceText, $"{_currentIntelligence.Confidence}%");
            
            // Set icon based on role
            string roleIcon = "🖥️";
            if (_currentIntelligence.DetectedRole.Contains("IIS")) roleIcon = "🌐";
            else if (_currentIntelligence.DetectedRole.Contains("Domain Controller")) roleIcon = "🛡️";
            else if (_currentIntelligence.DetectedRole.Contains("SQL")) roleIcon = "💾";
            else if (_currentIntelligence.DetectedRole.Contains("Terminal")) roleIcon = "💻";
            else if (_currentIntelligence.DetectedRole.Contains("File")) roleIcon = "📁";
            IntelligenceRoleIcon.Text = roleIcon;

            var settings = SettingsRuntime.Current;
            {
                IntelligenceComponentsList.ItemsSource = _currentIntelligence.Components;
                IntelligenceInsightsList.ItemsSource = _currentIntelligence.Insights;
                IntelligenceRecommendationsList.ItemsSource = _currentIntelligence.Recommendations;
                UpdateHealthExpandedSummary();
                UpdateHealthStatusState();
                UpdateAiWidgetState();

                if (_currentIntelligence.SmartInsights != null)
                {
                    var preservedAi = _smartInsights.Where(x => x.IsAi).ToList();
                    _smartInsights.SyncWithDiff(_currentIntelligence.SmartInsights, (a, b) => a.Text == b.Text && a.Severity == b.Severity);
                    foreach (var ai in preservedAi)
                    {
                        if (!_smartInsights.Any(x => x.IsAi && x.Text == ai.Text))
                            _smartInsights.Add(ai);
                    }
                    SmartInsightList.ItemsSource = _smartInsights;
                }
            }

            UpdateServicesWidgetState(snap, settings);
            if (TimelineItemsControl != null)
                TimelineItemsControl.Visibility = settings.MonitorEventLogs ? Visibility.Visible : Visibility.Collapsed;

            if (SelectedOsText != null)
                SetTextIfChanged(SelectedOsText, snap.OSName ?? "Unknown OS");
            UpdateGpuWidget(snap);
            UpdateProcessesWidget(snap);
            UpdateWindowsUpdatesWidget(snap);
            UpdateFirewallWidget(snap);
            UpdateBitLockerWidget(snap);
            UpdateDefenderWidget(snap);
            UpdateSecurityScoreWidget(snap);
            UpdateOpenPortsWidget(snap);
            UpdateDashboardAgentStatus(snap);

            long? pingMs = snap.PingMs >= 0 ? snap.PingMs : null;
            ApplyHealthGaugeVisual(CalculateHealthScore(pingMs, snap, _lastDashboardRdpOpen).Score);
        }

        private void OnOpenPortsFilterChanged(object sender, RoutedEventArgs e) => RefreshOpenPortsFilter();

        private void RefreshOpenPortsFilter()
        {
            try
            {
                if (OpenPortsGrid == null)
                    return;

                var view = CollectionViewSource.GetDefaultView(_openPortRows);
                if (view == null)
                    return;

                view.Filter = item =>
                {
                    if (item is not OpenPortRow row)
                        return false;

                    var query = OpenPortsSearchBox?.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(query)
                        && !row.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !row.Port.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !row.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !row.Process.Contains(query, StringComparison.OrdinalIgnoreCase))
                        return false;

                    var selected = (OpenPortsRiskFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
                    return selected.Equals("All", StringComparison.OrdinalIgnoreCase)
                           || row.Risk.Equals(selected, StringComparison.OrdinalIgnoreCase);
                };
                view.Refresh();
                UpdateOpenPortsWidget(_lastSnapshot);
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Open ports filter failed: {ex.Message}", category: "Dashboard", action: "OpenPorts");
            }
        }

        private void UpdateOpenPortsWidget(WmiSnapshot? snap)
        {
            try
            {
                if (OpenPortsGrid == null || OpenPortsEmptyText == null)
                    return;

                var rows = snap?.OpenPorts ?? new List<OpenPortRow>();
                _openPortRows.SyncWithDiff(rows, (a, b) => a.Protocol == b.Protocol && a.Port == b.Port && a.Pid == b.Pid);
                var view = CollectionViewSource.GetDefaultView(_openPortRows);
                view?.Refresh();
                var hasRows = _openPortRows.Count > 0 && view is { IsEmpty: false };

                if (hasRows)
                {
                    OpenPortsGrid.Visibility = Visibility.Visible;
                    OpenPortsEmptyText.Visibility = Visibility.Collapsed;
                    return;
                }

                OpenPortsGrid.Visibility = Visibility.Collapsed;
                OpenPortsEmptyText.Text = snap?.Connected == true && _openPortRows.Count > 0
                    ? "No open ports match the current search or risk filter."
                    : snap?.Connected == true
                        ? "No listening ports were returned by the StandardCimv2 provider."
                        : "Open port data requires a live WMI snapshot.";
                OpenPortsEmptyText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Open ports widget update failed: {ex.Message}", category: "Dashboard", action: "OpenPorts");
            }
        }

        private void UpdateSecurityScoreWidget(WmiSnapshot? snap)
        {
            try
            {
                if (SecurityScoreValueText == null)
                    return;

                if (snap?.Connected != true)
                {
                    SetTextIfChanged(SecurityScoreValueText, "--");
                    SetTextIfChanged(SecurityScoreRiskText, "Risk: Not Available");
                    SetTextIfChanged(SecurityScoreSummaryText, "Security score requires a live WMI snapshot.");
                    SetTextIfChanged(SecurityScoreInputsText, "Inputs: Firewall, BitLocker, Defender, Updates, Critical Services, Health");
                    return;
                }

                var score = 100;
                var findings = new List<string>();

                void Penalize(bool condition, int points, string finding)
                {
                    if (!condition) return;
                    score -= points;
                    findings.Add(finding);
                }

                Penalize(snap.FirewallDomainEnabled == false || snap.FirewallPrivateEnabled == false || snap.FirewallPublicEnabled == false, 18, "Firewall profile disabled");
                Penalize(snap.FirewallDomainEnabled == null && snap.FirewallPrivateEnabled == null && snap.FirewallPublicEnabled == null, 8, "Firewall profile state unavailable");
                Penalize(snap.BitLockerVolumes.Count == 0, 8, "BitLocker state unavailable");
                Penalize(snap.BitLockerVolumes.Any(v => v.ProtectionStatus.Equals("Off", StringComparison.OrdinalIgnoreCase)), 16, "BitLocker protection off");
                Penalize(snap.DefenderRealTimeProtectionEnabled == false, 20, "Defender real-time protection disabled");
                Penalize(snap.DefenderThreatCount is > 0, 18, "Active Defender threat detections");
                Penalize(snap.WindowsUpdateRebootRequired == true, 8, "Windows Update reboot pending");

                var components = _currentIntelligence.Components ?? new List<DetectedComponent>();
                var criticalComponents = components.Count(c => GetHealthStatusBucket(c.Status) is "critical" or "offline");
                Penalize(criticalComponents > 0, Math.Min(24, criticalComponents * 6), $"{criticalComponents} critical/offline services");

                score = Math.Clamp(score, 0, 100);
                var risk = score >= 85 ? "Low" : score >= 65 ? "Medium" : score >= 40 ? "High" : "Critical";
                SetTextIfChanged(SecurityScoreValueText, score.ToString());
                SetTextIfChanged(SecurityScoreRiskText, $"Risk: {risk}");
                SetTextIfChanged(SecurityScoreSummaryText,
                    findings.Count == 0 ? "No high-risk security findings were detected from available inputs." : string.Join("; ", findings.Take(3)));
                SetTextIfChanged(SecurityScoreInputsText,
                    $"Inputs: Firewall {(snap.FirewallServiceStatus ?? "N/A")}, BitLocker {snap.BitLockerVolumes.Count} volume(s), Defender {(snap.DefenderHealthState ?? "N/A")}, Critical {criticalComponents}");

                if (SecurityScoreGaugeArc != null)
                    SecurityScoreGaugeArc.StrokeDashArray = new DoubleCollection { score, Math.Max(0, 100 - score) };
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Security Score widget update failed: {ex.Message}", category: "Dashboard", action: "SecurityScore");
            }
        }

        private void UpdateDefenderWidget(WmiSnapshot? snap)
        {
            try
            {
                if (DefenderRealtimeText == null)
                    return;

                if (snap?.Connected != true)
                {
                    SetTextIfChanged(DefenderRealtimeText, "Unavailable");
                    SetTextIfChanged(DefenderHealthText, "WMI required");
                    SetTextIfChanged(DefenderLastScanText, "Not Available");
                    SetTextIfChanged(DefenderThreatCountText, "Not Available");
                    SetTextIfChanged(DefenderVersionText, "Signature: Not Available · Engine: Not Available");
                    return;
                }

                SetTextIfChanged(DefenderRealtimeText, snap.DefenderRealTimeProtectionEnabled.HasValue
                    ? (snap.DefenderRealTimeProtectionEnabled.Value ? "Enabled" : "Disabled")
                    : "Not Available");
                SetTextIfChanged(DefenderHealthText, string.IsNullOrWhiteSpace(snap.DefenderHealthState) ? "Not Available" : snap.DefenderHealthState);
                SetTextIfChanged(DefenderLastScanText, string.IsNullOrWhiteSpace(snap.DefenderLastScan) ? "Not Available" : snap.DefenderLastScan);
                SetTextIfChanged(DefenderThreatCountText, snap.DefenderThreatCount.HasValue ? snap.DefenderThreatCount.Value.ToString() : "Not Available");
                SetTextIfChanged(DefenderVersionText,
                    $"Signature: {(string.IsNullOrWhiteSpace(snap.DefenderSignatureVersion) ? "Not Available" : snap.DefenderSignatureVersion)} · Engine: {(string.IsNullOrWhiteSpace(snap.DefenderEngineVersion) ? "Not Available" : snap.DefenderEngineVersion)}");
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Defender widget update failed: {ex.Message}", category: "Dashboard", action: "Defender");
            }
        }

        private void UpdateBitLockerWidget(WmiSnapshot? snap)
        {
            try
            {
                if (BitLockerGrid == null || BitLockerEmptyText == null)
                    return;

                if (snap?.Connected == true && snap.BitLockerVolumes is { Count: > 0 })
                {
                    BitLockerGrid.ItemsSource = snap.BitLockerVolumes;
                    BitLockerGrid.Visibility = Visibility.Visible;
                    BitLockerEmptyText.Visibility = Visibility.Collapsed;
                    return;
                }

                BitLockerGrid.ItemsSource = null;
                BitLockerGrid.Visibility = Visibility.Collapsed;
                BitLockerEmptyText.Text = snap?.Connected == true
                    ? "No BitLocker volume data was returned. Permission or Windows edition may not support the provider."
                    : "BitLocker data requires a live WMI snapshot.";
                BitLockerEmptyText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"BitLocker widget update failed: {ex.Message}", category: "Dashboard", action: "BitLocker");
            }
        }

        private void UpdateFirewallWidget(WmiSnapshot? snap)
        {
            try
            {
                if (FirewallDomainText == null)
                    return;

                if (snap?.Connected != true)
                {
                    SetTextIfChanged(FirewallDomainText, "Unavailable");
                    SetTextIfChanged(FirewallPrivateText, "Unavailable");
                    SetTextIfChanged(FirewallPublicText, "Unavailable");
                    SetTextIfChanged(FirewallServiceText, "Service: WMI required");
                    return;
                }

                SetTextIfChanged(FirewallDomainText, FormatFirewallState(snap.FirewallDomainEnabled));
                SetTextIfChanged(FirewallPrivateText, FormatFirewallState(snap.FirewallPrivateEnabled));
                SetTextIfChanged(FirewallPublicText, FormatFirewallState(snap.FirewallPublicEnabled));
                SetTextIfChanged(FirewallServiceText,
                    $"Service: {(string.IsNullOrWhiteSpace(snap.FirewallServiceStatus) ? "Not Available" : snap.FirewallServiceStatus)}");
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Firewall widget update failed: {ex.Message}", category: "Dashboard", action: "Firewall");
            }
        }

        private static string FormatFirewallState(bool? enabled) => enabled.HasValue
            ? (enabled.Value ? "Enabled" : "Disabled")
            : "Not Available";

        private void UpdateWindowsUpdatesWidget(WmiSnapshot? snap)
        {
            try
            {
                if (WindowsUpdatePendingText == null)
                    return;

                if (snap?.Connected != true)
                {
                    SetTextIfChanged(WindowsUpdatePendingText, "Unavailable");
                    SetTextIfChanged(WindowsUpdateRebootText, "WMI required");
                    SetTextIfChanged(WindowsUpdateLastCheckText, "Not Available");
                    SetTextIfChanged(WindowsUpdateLastInstallText, "Not Available");
                    SetTextIfChanged(WindowsUpdateAutoText, "Automatic Updates: Not Available");
                    return;
                }

                SetTextIfChanged(WindowsUpdatePendingText,
                    snap.WindowsUpdatePendingCount.HasValue ? snap.WindowsUpdatePendingCount.Value.ToString() : "Not Available");
                SetTextIfChanged(WindowsUpdateRebootText,
                    snap.WindowsUpdateRebootRequired.HasValue ? (snap.WindowsUpdateRebootRequired.Value ? "Required" : "No") : "Not Available");
                SetTextIfChanged(WindowsUpdateLastCheckText, string.IsNullOrWhiteSpace(snap.WindowsUpdateLastCheck) ? "Not Available" : snap.WindowsUpdateLastCheck);
                SetTextIfChanged(WindowsUpdateLastInstallText, string.IsNullOrWhiteSpace(snap.WindowsUpdateLastInstall) ? "Not Available" : snap.WindowsUpdateLastInstall);
                SetTextIfChanged(WindowsUpdateAutoText,
                    $"Automatic Updates: {(string.IsNullOrWhiteSpace(snap.WindowsUpdateAutomaticUpdates) ? "Not Available" : snap.WindowsUpdateAutomaticUpdates)} · Service: {(string.IsNullOrWhiteSpace(snap.WindowsUpdateServiceStatus) ? "Not Available" : snap.WindowsUpdateServiceStatus)}");
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Windows Updates widget update failed: {ex.Message}", category: "Dashboard", action: "WindowsUpdates");
            }
        }

        private void UpdateProcessesWidget(WmiSnapshot? snap)
        {
            try
            {
                if (ProcessesGrid == null || ProcessesEmptyText == null)
                    return;

                if (snap?.Connected == true && snap.TopProcesses is { Count: > 0 })
                {
                    _processRows.SyncWithDiff(snap.TopProcesses, (a, b) => a.Id == b.Id && a.Name == b.Name);
                    ProcessesGrid.Visibility = Visibility.Visible;
                    ProcessesEmptyText.Visibility = Visibility.Collapsed;
                    return;
                }

                _processRows.SyncWithDiff(new List<ProcessRow>(), (a, b) => a.Id == b.Id);
                ProcessesGrid.Visibility = Visibility.Collapsed;
                ProcessesEmptyText.Text = snap?.Connected == true
                    ? "No process data was returned in the latest WMI snapshot."
                    : "Process data requires a live WMI snapshot.";
                ProcessesEmptyText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Processes widget update failed: {ex.Message}", category: "Dashboard", action: "Processes");
            }
        }

        private void UpdateSessionsWidget(WmiSnapshot? snap)
        {
            try
            {
                if (SessionsGrid == null || SessionsEmptyText == null)
                    return;

                if (_sessionRows.Count > 0)
                {
                    SessionsGrid.Visibility = Visibility.Visible;
                    SessionsEmptyText.Visibility = Visibility.Collapsed;
                    return;
                }

                SessionsGrid.Visibility = Visibility.Collapsed;
                SessionsEmptyText.Text = snap?.Connected == true
                    ? "No active interactive or remote sessions were returned in the latest WMI snapshot."
                    : "Session data requires a live WMI snapshot.";
                SessionsEmptyText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Sessions widget update failed: {ex.Message}", category: "Dashboard", action: "Sessions");
            }
        }

        private void UpdateGpuWidget(WmiSnapshot? snap)
        {
            try
            {
                if (DashGpuNameText == null || DashGpuMemoryText == null)
                    return;

                if (snap == null || !snap.Connected)
                {
                    SetTextIfChanged(DashGpuNameText, "Unavailable");
                    SetTextIfChanged(DashGpuMemoryText, "WMI snapshot required");
                    return;
                }

                SetTextIfChanged(DashGpuNameText,
                    string.IsNullOrWhiteSpace(snap.GpuName) ? "Not Available" : snap.GpuName.Trim());
                SetTextIfChanged(DashGpuMemoryText,
                    snap.GpuMemoryGb.HasValue ? $"{snap.GpuMemoryGb.Value:0.0} GB adapter memory" : "Adapter memory unavailable");
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"GPU widget update failed: {ex.Message}", category: "Dashboard", action: "GPU");
            }
        }

        private void UpdateDashboardAgentStatus(WmiSnapshot? snap)
        {
            try
            {
                if (DashAgentStatusText == null)
                    return;

                var health = _currentAgentHealth;
                if (health != null && health.EndpointReachable)
                {
                    var label = health.Status switch
                    {
                        AgentHealthStatus.Online => "Online",
                        AgentHealthStatus.Warning => "Warning",
                        _ => "Offline"
                    };
                    var detail = health.Version != null ? $" v{health.Version}" : "";
                    if (health.LatencyMs.HasValue)
                        detail += $" ({health.LatencyMs}ms)";

                    DashAgentStatusText.Text = label + detail;
                    DashAgentStatusText.Foreground = new SolidColorBrush(health.Status switch
                    {
                        AgentHealthStatus.Online => System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80),
                        AgentHealthStatus.Warning => System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24),
                        _ => System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71)
                    });
                    if (DashAgentStatusDot != null)
                    {
                        DashAgentStatusDot.Fill = DashAgentStatusText.Foreground;
                    }
                    return;
                }

                var wmiReachable = snap?.Connected == true;
                DashAgentStatusText.Text = wmiReachable ? "WMI" : "Offline";
                DashAgentStatusText.Foreground = new SolidColorBrush(wmiReachable
                    ? System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)
                    : System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));
                if (DashAgentStatusDot != null)
                    DashAgentStatusDot.Fill = DashAgentStatusText.Foreground;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private async Task RefreshAgentHealthAsync(ServerInfo server, CancellationToken ct)
        {
            try
            {
                var key = SettingsApplicator.ResolveAgentMetricsKey(SettingsRuntime.Current);
                var health = await AgentHealthService.CheckAsync(server.Ip, key, SettingsRuntime.Current, ct).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    _currentAgentHealth = health;
                    UpdateDashboardAgentStatus(_lastSnapshot);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Agent health refresh failed: {ex.Message}", category: "Agent", action: "Health");
            }
        }

        private static string FormatUptime(TimeSpan? uptime) =>
            LiveDashboardMetricsBuilder.FormatUptimePair(uptime).Text;

        private void OnViewAllServicesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ServicesGrid == null) return;
                ServicesGrid.MaxHeight = ServicesGrid.MaxHeight > 200 ? 176 : 360;
                ServicesGrid.VerticalScrollBarVisibility = ServicesGrid.MaxHeight > 200
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void SetPerfRangeActive(System.Windows.Controls.Button activeBtn)
        {
            foreach (var btn in new[] { PerfRange1hBtn, PerfRange6hBtn, PerfRange24hBtn, PerfRange7dBtn })
            {
                if (btn == null) continue;
                btn.Style = btn == activeBtn
                    ? (Style)FindResource("DashTimeRangeButtonActive")
                    : (Style)FindResource("DashTimeRangeButton");
            }
        }

        private void OnPerfRange1hClick(object sender, RoutedEventArgs e) => SetPerfRange(PerfRange1hBtn, PerfChartTimeRange.OneMinute);
        private void OnPerfRange6hClick(object sender, RoutedEventArgs e) => SetPerfRange(PerfRange6hBtn, PerfChartTimeRange.FiveMinutes);
        private void OnPerfRange24hClick(object sender, RoutedEventArgs e) => SetPerfRange(PerfRange24hBtn, PerfChartTimeRange.FifteenMinutes);
        private void OnPerfRange7dClick(object sender, RoutedEventArgs e) => SetPerfRange(PerfRange7dBtn, PerfChartTimeRange.OneHour);

        private void SetPerfRange(System.Windows.Controls.Button activeBtn, PerfChartTimeRange range)
        {
            SetPerfRangeActive(activeBtn);
            _perfChartRange = range;
            _liveChartSession.SetTimeRange(range);
            _chartUpdateThrottle.Reset();
            _lastDrawnSampleCount = -1;
            HidePerformanceTooltip();
            UpdatePerformanceChart();
        }


        private DashboardBuilderController? _dashboardBuilder;
        private string? _dashboardBuilderServerKey;

        private void EnsureDashboardBuilderInitialized(string serverKey)
        {
            _dashboardBuilder ??= new DashboardBuilderController(this);
            RegisterDashboardMiniPanels();
            _dashboardBuilder.EnsureHostReady(DashboardWorkspaceHost);
            DashboardAddWidgetPanel.Attach(_dashboardBuilder);

            if (!string.Equals(_dashboardBuilderServerKey, serverKey, StringComparison.OrdinalIgnoreCase)
                || !_dashboardBuilder.ViewModel.Widgets.Any())
            {
                _dashboardBuilder.LoadServerDashboard(serverKey);
                _dashboardBuilderServerKey = serverKey;
            }

            RefreshDashWorkspaceCombo();
            UpdateDashBuilderToolbarState();
            SyncInfoCompactContent();
        }

        private void RegisterDashboardMiniPanels()
        {
            if (_dashboardBuilder == null)
                return;

            _dashboardBuilder.RegisterPanel("health", DashPanelHealth);
            _dashboardBuilder.RegisterPanel("performance", DashPanelPerformance);
            _dashboardBuilder.RegisterPanel("services", DashPanelServices);
            _dashboardBuilder.RegisterPanel("events", DashPanelEvents);
            _dashboardBuilder.RegisterPanel("ai", DashPanelAiAnalysis);
            _dashboardBuilder.RegisterPanel("info", DashPanelGeneralInfo);
            _dashboardBuilder.RegisterPanel("cpu", DashWidgetCpuPanel);
            _dashboardBuilder.RegisterPanel("ram", DashWidgetRamPanel);
            _dashboardBuilder.RegisterPanel("disk", DashWidgetDiskPanel);
            _dashboardBuilder.RegisterPanel("network", DashWidgetNetworkPanel);
            _dashboardBuilder.RegisterPanel("latency", DashWidgetLatencyPanel);
            _dashboardBuilder.RegisterPanel("gpu", DashWidgetGpuPanel);
            _dashboardBuilder.RegisterPanel("processes", DashWidgetProcessesPanel);
            _dashboardBuilder.RegisterPanel("sessions", DashWidgetSessionsPanel);
            _dashboardBuilder.RegisterPanel("windows-updates", DashWidgetWindowsUpdatesPanel);
            _dashboardBuilder.RegisterPanel("firewall", DashWidgetFirewallPanel);
            _dashboardBuilder.RegisterPanel("bitlocker", DashWidgetBitLockerPanel);
            _dashboardBuilder.RegisterPanel("windows-defender", DashWidgetDefenderPanel);
            _dashboardBuilder.RegisterPanel("security-score", DashWidgetSecurityScorePanel);
            _dashboardBuilder.RegisterPanel("open-ports", DashWidgetOpenPortsPanel);
            _dashboardBuilder.RegisterPanel("agent-health", DashWidgetAgentHealthPanel);
            _dashboardBuilder.RegisterPanel("uptime", DashWidgetUptimePanel);
            _dashboardBuilder.RegisterPanel("ip-address", DashWidgetIpPanel);
        }

        private void RefreshDashWorkspaceCombo()
        {
            if (_dashboardBuilder == null || DashWorkspaceCombo == null)
                return;

            var selected = _dashboardBuilder.ViewModel.ActiveWorkspace?.Id;
            DashWorkspaceCombo.ItemsSource = _dashboardBuilder.ViewModel.Workspaces;
            DashWorkspaceCombo.DisplayMemberPath = "Name";
            DashWorkspaceCombo.SelectedValuePath = "Id";
            DashWorkspaceCombo.SelectedValue = selected;
        }

        private void UpdateDashBuilderToolbarState()
        {
            if (_dashboardBuilder == null)
                return;

            var vm = _dashboardBuilder.ViewModel;
            var edit = vm.IsEditMode && vm.CanEditWidgets;
            DashEditLayoutBtn.Content = edit ? "Done Editing" : "Edit Layout";
            DashLockLayoutBtn.Content = vm.IsLayoutLocked ? "Unlock Layout" : "Lock Layout";
            DashAddWidgetBtn.IsEnabled = edit;
            DashAutoArrangeBtn.IsEnabled = edit;
            DashEditMenuBtn.Content = DashEditActionsPanel.Visibility == Visibility.Visible ? "Kapat" : "Düzenle";
        }

        private string? GetCurrentDashboardServerKey()
        {
            var server = _selectedServer ?? _selectedNode?.Server;
            return server == null ? null : GetServerKey(server);
        }

        private void EnsureDashboardBuilderForCurrentServer()
        {
            var key = _dashboardBuilder?.CurrentServerKey ?? GetCurrentDashboardServerKey();
            if (string.IsNullOrWhiteSpace(key))
                return;
            EnsureDashboardBuilderInitialized(key);
        }

        private void OnDashAddWidgetClick(object sender, RoutedEventArgs e)
        {
            var key = GetCurrentDashboardServerKey();
            if (key == null)
                return;
            EnsureDashboardBuilderInitialized(key);
            if (!_dashboardBuilder!.ViewModel.IsEditMode)
            {
                _dashboardBuilder.ToggleEditMode();
                UpdateDashBuilderToolbarState();
            }
            DashboardAddWidgetPanel.ShowPanel();
        }

        private void OnDashEditMenuClick(object sender, RoutedEventArgs e)
        {
            DashEditActionsPanel.Visibility = DashEditActionsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateDashBuilderToolbarState();
        }

        private void OnDashEditLayoutClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.ToggleEditMode();
            UpdateDashBuilderToolbarState();
        }

        private void OnDashSaveLayoutClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.SaveLayout();
        }

        private void OnDashAutoArrangeClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.AutoArrangeLayout();
        }

        private void OnDashLockLayoutClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.SetLayoutLocked(!_dashboardBuilder.ViewModel.IsLayoutLocked);
            UpdateDashBuilderToolbarState();
        }

        private void OnDashResetLayoutClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            if (ConfirmActionDialog.Show(this, ConfirmActionKind.ServiceAction, "Dashboard", "layout", "Reset dashboard layout to default?"))
                _dashboardBuilder!.ResetLayout();
            RefreshDashWorkspaceCombo();
            UpdateDashBuilderToolbarState();
        }

        private void OnDashImportLayoutClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.ImportLayout();
            RefreshDashWorkspaceCombo();
            UpdateDashBuilderToolbarState();
        }

        private void OnDashExportLayoutClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.ExportLayout();
        }

        private void OnDashWorkspaceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_dashboardBuilder == null || DashWorkspaceCombo?.SelectedValue is not string id)
                return;
            _dashboardBuilder.SwitchWorkspace(id);
            UpdateDashBuilderToolbarState();
        }

        private void OnDashWorkspaceCreateClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.CreateWorkspace($"Workspace {_dashboardBuilder.ViewModel.Workspaces.Count + 1}");
            RefreshDashWorkspaceCombo();
        }

        private void OnDashWorkspaceDuplicateClick(object sender, RoutedEventArgs e)
        {
            EnsureDashboardBuilderForCurrentServer();
            _dashboardBuilder!.DuplicateActiveWorkspace();
            RefreshDashWorkspaceCombo();
        }

        private void OnDashMainGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
        }

        private void OnDashPanelMaximizeClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string key)
                return;

            ExpandDashboardWidget(key);
        }

        public void ExpandDashboardWidget(string key)
        {
            var panel = ResolveDashPanel(key);
            if (panel == null)
                return;

            if (_dashExpandedPanel == panel)
            {
                RestoreDashPanel();
                return;
            }

            ExpandDashPanel(panel, key);
        }

        private void OnDashPanelRestoreClick(object sender, RoutedEventArgs e) => RestoreDashPanel();

        private void OnDashExpandOverlayMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_dashExpandedPanel != null)
            {
                e.Handled = true;
                RestoreDashPanel();
            }
        }

        private void OnDashExpandModalMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private void OnDashExpandPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var handled = _dashExpandedKey switch
            {
                "ai" => AiExpandedAnalysisConsole?.ScrollByWheelDelta(e.Delta) == true,
                "health" => ScrollExpandedViewerByWheel(HealthExpandedScrollViewer, e.Delta),
                "info" => ScrollExpandedViewerByWheel(InfoExpandedOverview, e.Delta),
                _ => false
            };

            if (!handled)
                handled = ScrollExpandedViewerByWheel(DashExpandScrollViewer, e.Delta);

            if (handled)
                e.Handled = true;
        }

        private static bool ScrollExpandedViewerByWheel(ScrollViewer? viewer, int wheelDelta)
        {
            if (viewer == null || viewer.Visibility != Visibility.Visible || viewer.ScrollableHeight <= 0)
                return false;

            var direction = wheelDelta > 0 ? -1 : 1;
            var step = Math.Max(48, SystemParameters.WheelScrollLines * 24);
            var target = Math.Clamp(viewer.VerticalOffset + direction * step, 0, viewer.ScrollableHeight);
            if (Math.Abs(target - viewer.VerticalOffset) < 0.5)
                return false;

            viewer.ScrollToVerticalOffset(target);
            return true;
        }

        private void OnDashExpandScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateExpandedPanelViewportConstraint();
        }

        private void UpdateExpandedPanelViewportConstraint()
        {
            if (_dashExpandedPanel == null
                || (!string.Equals(_dashExpandedKey, "ai", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(_dashExpandedKey, "health", StringComparison.OrdinalIgnoreCase)))
                return;

            var viewportHeight = DashExpandScrollViewer.ViewportHeight;
            if (viewportHeight <= 0 || double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight))
                viewportHeight = DashExpandScrollViewer.ActualHeight;
            if (viewportHeight <= 0 || double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight))
                return;

            var constrainedHeight = Math.Max(240, viewportHeight);
            if (Math.Abs(_dashExpandedPanel.Height - constrainedHeight) >= 0.5
                || Math.Abs(_dashExpandedPanel.MaxHeight - constrainedHeight) >= 0.5)
            {
                _dashExpandedPanel.Height = constrainedHeight;
                _dashExpandedPanel.MaxHeight = constrainedHeight;
            }

            var contentHeight = Math.Max(160, constrainedHeight - 88);
            if (string.Equals(_dashExpandedKey, "ai", StringComparison.OrdinalIgnoreCase))
            {
                AiExpandedAnalysisConsole.Height = contentHeight;
                AiExpandedAnalysisConsole.MaxHeight = contentHeight;
            }
            else if (string.Equals(_dashExpandedKey, "health", StringComparison.OrdinalIgnoreCase))
            {
                HealthExpandedConsole.Height = contentHeight;
                HealthExpandedConsole.MaxHeight = contentHeight;
            }
        }

        private Border? ResolveDashPanel(string key) => key switch
        {
            "performance" => DashPanelPerformance,
            "services" => DashPanelServices,
            "events" => DashPanelEvents,
            "health" => DashPanelHealth,
            "ai" => DashPanelAiAnalysis,
            "info" => DashPanelGeneralInfo,
            "cpu" => DashWidgetCpuPanel,
            "ram" => DashWidgetRamPanel,
            "disk" => DashWidgetDiskPanel,
            "network" => DashWidgetNetworkPanel,
            "latency" => DashWidgetLatencyPanel,
            "gpu" => DashWidgetGpuPanel,
            "processes" => DashWidgetProcessesPanel,
            "sessions" => DashWidgetSessionsPanel,
            "windows-updates" => DashWidgetWindowsUpdatesPanel,
            "firewall" => DashWidgetFirewallPanel,
            "bitlocker" => DashWidgetBitLockerPanel,
            "windows-defender" => DashWidgetDefenderPanel,
            "security-score" => DashWidgetSecurityScorePanel,
            "open-ports" => DashWidgetOpenPortsPanel,
            "agent-health" => DashWidgetAgentHealthPanel,
            "uptime" => DashWidgetUptimePanel,
            "ip-address" => DashWidgetIpPanel,
            _ => null
        };

        private static string GetDashPanelTitle(string key) => key switch
        {
            "performance" => "System Performance",
            "services" => "Aktif Servisler",
            "events" => "Son Etkinlikler",
            "health" => "Sistem Durumu",
            "ai" => "AI Smart Analysis",
            "info" => "Genel Bilgiler",
            "cpu" => "CPU Usage",
            "ram" => "RAM Usage",
            "disk" => "Disk Usage",
            "network" => "Network",
            "latency" => "Latency",
            "gpu" => "GPU",
            "processes" => "Processes",
            "sessions" => "Sessions",
            "windows-updates" => "Windows Updates",
            "firewall" => "Firewall",
            "bitlocker" => "BitLocker",
            "windows-defender" => "Windows Defender",
            "security-score" => "Security Score",
            "open-ports" => "Open Ports",
            "agent-health" => "Agent Health",
            "uptime" => "Uptime",
            "ip-address" => "IP Address",
            _ => "Panel"
        };

        private void ExpandDashPanel(Border panel, string key)
        {
            if (panel.Parent is not System.Windows.Controls.Panel parent)
                return;

            if (_dashExpandedPanel != null)
                RestoreDashPanel();

            _dashExpandedSlot = new DashPanelSlot
            {
                Parent = parent,
                Row = Grid.GetRow(panel),
                Column = Grid.GetColumn(panel),
                RowSpan = Grid.GetRowSpan(panel),
                ColumnSpan = Grid.GetColumnSpan(panel),
                Margin = panel.Margin,
                Height = panel.Height,
                MinHeight = panel.MinHeight,
                MaxHeight = panel.MaxHeight
            };

            parent.Children.Remove(panel);
            DashExpandHost.Child = panel;
            panel.Margin = new Thickness(0);
            panel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            panel.VerticalAlignment = VerticalAlignment.Stretch;

            ApplyDashPanelExpandLayout(key, expanded: true);

            if (DashExpandTitleText != null)
                DashExpandTitleText.Text = GetDashPanelTitle(key);

            if (DashboardWorkspaceHost != null)
                DashboardWorkspaceHost.Visibility = Visibility.Collapsed;
            if (DashboardAddWidgetPanel != null)
            {
                _dashAddPanelVisibilityBeforeExpand = DashboardAddWidgetPanel.Visibility;
                DashboardAddWidgetPanel.Visibility = Visibility.Collapsed;
            }
            DashExpandOverlay.Visibility = Visibility.Visible;
            _dashExpandedPanel = panel;
            _dashExpandedKey = key;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateExpandedPanelViewportConstraint();
                UpdatePerformanceChart();
            }), DispatcherPriority.Loaded);
        }

        private void RestoreDashPanel()
        {
            if (_dashExpandedPanel == null || _dashExpandedSlot == null)
                return;

            var panel = _dashExpandedPanel;
            var slot = _dashExpandedSlot;
            var key = _dashExpandedKey;

            DashExpandHost.Child = null;
            panel.Margin = slot.Margin;
            panel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            panel.VerticalAlignment = VerticalAlignment.Stretch;
            panel.Height = slot.Height;
            panel.MinHeight = slot.MinHeight;
            panel.MaxHeight = slot.MaxHeight;
            slot.Parent.Children.Add(panel);
            Grid.SetRow(panel, slot.Row);
            Grid.SetColumn(panel, slot.Column);
            Grid.SetRowSpan(panel, slot.RowSpan);
            Grid.SetColumnSpan(panel, slot.ColumnSpan);

            if (!string.IsNullOrEmpty(key))
                ApplyDashPanelExpandLayout(key!, expanded: false);

            DashExpandOverlay.Visibility = Visibility.Collapsed;
            DashMainGrid.Visibility = Visibility.Collapsed;
            if (DashboardWorkspaceHost != null)
                DashboardWorkspaceHost.Visibility = Visibility.Visible;
            if (DashboardAddWidgetPanel != null)
                DashboardAddWidgetPanel.Visibility = _dashAddPanelVisibilityBeforeExpand;

            _dashExpandedPanel = null;
            _dashExpandedSlot = null;
            _dashExpandedKey = null;

            SyncInfoCompactContent();
            Dispatcher.BeginInvoke(new Action(UpdatePerformanceChart), DispatcherPriority.Loaded);
        }

        private void RestoreDashPanelIfExpanded()
        {
            if (_dashExpandedPanel != null)
                RestoreDashPanel();
        }

        private void ApplyDashPanelExpandLayout(string key, bool expanded)
        {
            switch (key)
            {
                case "performance":
                    if (PerformanceChartHost != null)
                    {
                        PerformanceChartHost.Height = expanded ? double.NaN : 132;
                        PerformanceChartHost.MinHeight = expanded ? 360 : 0;
                    }
                    break;
                case "services":
                    if (ServicesGrid != null)
                    {
                        ServicesGrid.MaxHeight = expanded ? double.PositiveInfinity : 176;
                        ServicesGrid.VerticalScrollBarVisibility = expanded
                            ? ScrollBarVisibility.Auto
                            : ScrollBarVisibility.Disabled;
                    }
                    break;
                case "events":
                    if (TimelineItemsControl != null)
                        TimelineItemsControl.MaxHeight = expanded ? double.PositiveInfinity : 176;
                    break;
                case "ai":
                    if (AiCompactSummaryPanel != null)
                        AiCompactSummaryPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
                    if (AiExpandedAnalysisConsole != null)
                        AiExpandedAnalysisConsole.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    if (expanded)
                        UpdateAiExpandedAnalysisSummary();
                    if (!expanded && AiExpandedAnalysisConsole != null)
                    {
                        AiExpandedAnalysisConsole.Height = double.NaN;
                        AiExpandedAnalysisConsole.MaxHeight = double.PositiveInfinity;
                    }
                    break;
                case "health":
                    if (IntelligenceComponentsList != null)
                        IntelligenceComponentsList.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
                    if (HealthExpandedConsole != null)
                        HealthExpandedConsole.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    if (expanded)
                        UpdateHealthExpandedSummary();
                    if (!expanded && HealthExpandedConsole != null)
                    {
                        HealthExpandedConsole.Height = double.NaN;
                        HealthExpandedConsole.MaxHeight = double.PositiveInfinity;
                    }
                    break;
                case "info":
                    if (InfoCompactContent != null)
                        InfoCompactContent.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
                    if (InfoExpandedOverview != null)
                        InfoExpandedOverview.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    if (expanded)
                        UpdateServerOverviewDetails(_lastSnapshot);
                    break;
            }
        }

        private void UpdateServerOverviewDetails(WmiSnapshot? snap)
        {
            try
            {
                string ValueOrNotAvailable(string? value) =>
                    string.IsNullOrWhiteSpace(value) || value == "—" || value == "-"
                        ? "Not Available"
                        : value.Trim();

                var lastSnapshot = snap?.CollectedAt;
                SetTextIfChanged(ServerOverviewLastSnapshotText,
                    lastSnapshot.HasValue ? lastSnapshot.Value.ToLocalTime().ToString("HH:mm:ss") : "Not Available");

                SetTextIfChanged(ServerOverviewCpuModelText, ValueOrNotAvailable(snap?.CPUModel));
                SetTextIfChanged(ServerOverviewArchitectureText, ValueOrNotAvailable(snap?.Compatibility));
                SetTextIfChanged(ServerOverviewDiskCapacityText,
                    snap is { DiskTotalGb: > 0 }
                        ? $"{snap.DiskTotalGb:0} GB total / {snap.DiskFreeGb:0} GB free"
                        : "Not Available");
                SetTextIfChanged(ServerOverviewVirtualizationText, DetectVirtualization(snap));
                SetTextIfChanged(ServerOverviewDnsText,
                    snap?.DnsServers is { Count: > 0 } ? string.Join(", ", snap.DnsServers.Take(3)) : "Not Available");
                SetTextIfChanged(ServerOverviewGatewayText, ValueOrNotAvailable(snap?.DefaultGateway));
                SetTextIfChanged(ServerOverviewMacText, ValueOrNotAvailable(snap?.MacAddress));
                SetTextIfChanged(ServerOverviewDomainText, ValueOrNotAvailable(snap?.DomainName));
                SetTextIfChanged(ServerOverviewBuildText,
                    !string.IsNullOrWhiteSpace(snap?.OSVersion) || !string.IsNullOrWhiteSpace(snap?.OSBuildNumber)
                        ? $"{ValueOrNotAvailable(snap?.OSVersion)} / Build {ValueOrNotAvailable(snap?.OSBuildNumber)}"
                        : "Not Available");
                SetTextIfChanged(ServerOverviewBootTimeText,
                    snap?.Uptime is TimeSpan uptime
                        ? DateTime.Now.Subtract(uptime).ToString("yyyy-MM-dd HH:mm")
                        : "Not Available");
                SetTextIfChanged(ServerOverviewManufacturerText, ValueOrNotAvailable(snap?.Manufacturer));
                SetTextIfChanged(ServerOverviewModelText, ValueOrNotAvailable(snap?.Model));
                SetTextIfChanged(ServerOverviewMachineTypeText, ValueOrNotAvailable(snap?.MachineType));
                SyncInfoCompactContent();
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Server overview update failed: {ex.Message}", category: "Dashboard", action: "ServerOverview");
            }
        }

        private static string DetectVirtualization(WmiSnapshot? snap)
        {
            if (snap == null)
                return "Not Available";

            var combined = $"{snap.Manufacturer} {snap.Model} {snap.MachineType}";
            if (string.IsNullOrWhiteSpace(combined))
                return "Not Available";

            if (combined.Contains("VMware", StringComparison.OrdinalIgnoreCase))
                return "VMware virtual machine";
            if (combined.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase))
                return "VirtualBox virtual machine";
            if (combined.Contains("KVM", StringComparison.OrdinalIgnoreCase))
                return "KVM virtual machine";
            if (combined.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("Virtual Machine", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) && combined.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                return "Hyper-V virtual machine";

            return "Physical or not detected";
        }

        private void UpdateHealthExpandedSummary()
        {
            try
            {
                var components = _currentIntelligence.Components ?? new List<DetectedComponent>();
                var total = components.Count;
                var healthy = components.Count(c => GetHealthStatusBucket(c.Status) == "healthy");
                var warning = components.Count(c => GetHealthStatusBucket(c.Status) == "warning");
                var critical = components.Count(c => GetHealthStatusBucket(c.Status) == "critical");
                var offline = components.Count(c => GetHealthStatusBucket(c.Status) == "offline");
                var percent = total == 0 ? 0 : (int)Math.Round(healthy * 100.0 / total);
                var lastScan = _sectionTimestamps.MetricsUtc
                               ?? _lastSnapshot?.CollectedAt.ToUniversalTime()
                               ?? _lastSnapshot?.CollectedAt
                               ?? DateTime.MinValue;
                var lastScanText = lastScan == DateTime.MinValue
                    ? "-"
                    : lastScan.ToLocalTime().ToString("HH:mm:ss");

                SetTextIfChanged(HealthHealthyCountText, healthy.ToString());
                SetTextIfChanged(HealthWarningCountText, warning.ToString());
                SetTextIfChanged(HealthCriticalCountText, critical.ToString());
                SetTextIfChanged(HealthOfflineCountText, offline.ToString());
                SetTextIfChanged(HealthOverallPercentText, $"{percent}%");
                SetTextIfChanged(HealthLastScanText, lastScanText);
                if (HealthExpandedComponentsList != null)
                    HealthExpandedComponentsList.Tag = lastScanText;
                UpdateHealthStatusState();
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Health summary update failed: {ex.Message}", category: "Dashboard", action: "SystemStatus");
            }
        }

        private void UpdateHealthStatusState()
        {
            try
            {
                if (HealthStatusStatePanel == null || IntelligenceComponentsList == null)
                    return;

                var components = _currentIntelligence.Components ?? new List<DetectedComponent>();
                var hasComponents = components.Count > 0;
                var expanded = HealthExpandedConsole?.Visibility == Visibility.Visible;

                if (hasComponents)
                {
                    HealthStatusStatePanel.Visibility = Visibility.Collapsed;
                    if (!expanded)
                        IntelligenceComponentsList.Visibility = Visibility.Visible;
                    return;
                }

                if (!expanded)
                    IntelligenceComponentsList.Visibility = Visibility.Collapsed;

                HealthStatusStatePanel.Visibility = Visibility.Visible;

                if (_lastSnapshot == null)
                {
                    SetTextIfChanged(HealthStatusStateIcon, "\uE9D2");
                    SetTextIfChanged(HealthStatusStateTitle, "Loading system status");
                    SetTextIfChanged(HealthStatusStateMessage, "Waiting for the first server health snapshot.");
                    return;
                }

                if (!_lastSnapshot.Connected)
                {
                    SetTextIfChanged(HealthStatusStateIcon, "\uE783");
                    SetTextIfChanged(HealthStatusStateTitle, "System status unavailable");
                    SetTextIfChanged(HealthStatusStateMessage,
                        string.IsNullOrWhiteSpace(_lastSnapshot.Error)
                            ? "The server is disconnected or WMI access is not available."
                            : _lastSnapshot.Error);
                    return;
                }

                SetTextIfChanged(HealthStatusStateIcon, "\uE946");
                SetTextIfChanged(HealthStatusStateTitle, "No monitored components found");
                SetTextIfChanged(HealthStatusStateMessage,
                    "The server responded, but no supported Windows health components were returned in the latest snapshot.");
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Health status state update failed: {ex.Message}", category: "Dashboard", action: "SystemStatus");
            }
        }

        private static string GetHealthStatusBucket(string? status)
        {
            var s = (status ?? string.Empty).Trim().ToLowerInvariant();
            if (s.Contains("offline") || s.Contains("çevrimdışı") || s.Contains("cevrimdisi"))
                return "offline";
            if (s.Contains("kapalı") || s.Contains("kapali") || s.Contains("durdur") || s.Contains("closed") || s.Contains("critical"))
                return "critical";
            if (s.Contains("uyar") || s.Contains("pasif") || s.Contains("bilinmiyor") || s.Contains("unknown")
                || s.Contains("gerekli") || s.Contains("degraded") || s.Contains("desteklenmiyor")
                || s.Contains("şifreleme") || s.Contains("sifreleme"))
                return "warning";
            return "healthy";
        }


        private async void OnStartServiceClick(object sender, RoutedEventArgs e) => await ExecuteServiceActionInternal(sender, "start");
        private async void OnStopServiceClick(object sender, RoutedEventArgs e) => await ExecuteServiceActionInternal(sender, "stop");
        private async void OnRestartServiceClick(object sender, RoutedEventArgs e) => await ExecuteServiceActionInternal(sender, "restart");

        private async void OnStartAllCriticalServicesClick(object sender, RoutedEventArgs e)
        {
            var server = _selectedServer ?? _selectedNode?.Server;
            if (server == null || _lastSnapshot == null) return;

            var criticalStopped = _lastSnapshot.Services.Where(s => s.IsCritical && s.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!criticalStopped.Any())
            {
                System.Windows.MessageBox.Show("Durdurulmuş kritik servis bulunamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = System.Windows.MessageBox.Show($"{criticalStopped.Count} kritik servis başlatılacak. Devam edilsin mi?", "Toplu Başlat", MessageBoxButton.YesNo, MessageBoxImage.Question);
            AuditLogService.LogRequest(
                AuditActions.ServiceStart,
                AuditCategories.RemoteAdmin,
                server,
                $"Bulk critical service start requested for {criticalStopped.Count} service(s).",
                JsonSerializer.Serialize(new { services = criticalStopped.Select(s => s.Name).ToList() }));
            if (result != MessageBoxResult.Yes) return;

            UpdateConnectionStatus($"{criticalStopped.Count} kritik servis başlatılıyor...", true);
            
            int successCount = 0;
            foreach (var svc in criticalStopped)
            {
                var (res, _) = await WmiCollector.ControlServiceAsync(server.Ip, server.Username, server.Password, svc.Name, "start");
                if (res) successCount++;
                await Task.Delay(200);
            }

            UpdateConnectionStatus($"{successCount}/{criticalStopped.Count} servis başarıyla başlatıldı.", successCount > 0);
            System.Windows.MessageBox.Show($"{criticalStopped.Count} servisten {successCount} tanesi başlatıldı.", "İşlem Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            
            _ = RefreshInsightsAsync(server, CancellationToken.None, force: true);
        }

        private async Task ExecuteServiceActionInternal(object sender, string action)
        {
            ServiceRow? service = ServicesGrid.SelectedItem as ServiceRow;
            var server = _selectedServer ?? _selectedNode?.Server;

            if (service != null && server != null)
            {
                var actionLabel = action switch
                {
                    "start" => "servis başlatma",
                    "stop" => "servis durdurma",
                    "restart" => "servis yeniden başlatma",
                    _ => $"servis {action} işlemi"
                };
                var auditAction = action switch
                {
                    "start" => AuditActions.ServiceStart,
                    "stop" => AuditActions.ServiceStop,
                    "restart" => AuditActions.ServiceRestart,
                    _ => AuditActions.ServiceStart
                };
                AuditLogService.LogRequest(
                    auditAction,
                    AuditCategories.RemoteAdmin,
                    server,
                    $"Service {action} requested for {service.Display}.",
                    JsonSerializer.Serialize(new { service.Name, service.Display, service.State, action }));
                if (!ConfirmActionDialog.Show(this, ConfirmActionKind.ServiceAction, server.Name, server.Ip, $"{service.Display} — {actionLabel}"))
                    return;

                UpdateConnectionStatus($"{service.Display} ({action}) işlemi başlatıldı...", false);
                using var audit = AuditOperationScope.BeginRemoteAdmin(auditAction, server);
                var (success, message) = await WmiCollector.ControlServiceAsync(server.Ip, server.Username, server.Password, service.Name, action);
                if (success)
                    audit.CompleteSuccess();
                else
                    audit.CompleteFailed(message);
                
                string statusMsg = success 
                    ? $"{service.Display} başarıyla {action} edildi." 
                    : $"{service.Display} {action} edilemedi. Hata: {message}";

                UpdateConnectionStatus(statusMsg, false);
                if (success) {
                    await Task.Delay(500); 
                    _ = RefreshInsightsAsync(server, CancellationToken.None, force: true);
                }
                else
                {
                    System.Windows.MessageBox.Show(statusMsg, "İşlem Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private string GetWmiServiceErrorMessage(uint code)
        {
            return code switch
            {
                0 => "Başarılı",
                2 => "Erişim Engellendi (Access Denied)",
                3 => "Bağımlı Hizmetler Çalışıyor",
                4 => "Geçersiz Kontrol",
                5 => "Kontrol Kabul Edilemiyor",
                6 => "Hizmet Aktif Değil",
                7 => "Zaman Aşımı",
                8 => "Bilinmeyen Hata",
                9 => "Yol Bulunamadı",
                10 => "Hizmet Zaten Çalışıyor",
                14 => "Hizmet Devre Dışı (Disabled)",
                15 => "Oturum Açma Hatası",
                22 => "Geçersiz Hizmet Hesabı",
                404 => "Hizmet Bulunamadı",
                999 => "Bağlantı Hatası / WMI Hatası",
                8888 => "Komut kabul edildi ama hizmet çalışmıyor (Tetikle/Trigger Start olabilir)",
                _ => $"WMI Hatası {code}"
            };
        }

        private void OnOpenEventViewerClick(object sender, RoutedEventArgs e)
        {
            if (_selectedServer != null)
            {
                try {
                    Process.Start(new ProcessStartInfo("eventvwr.msc", $"/computer:{_selectedServer.Ip}") { UseShellExecute = true });
                } catch (Exception ex) {
                    UpdateConnectionStatus("Uzak yönetim işlemi tamamlanamadı.", false);
                }
            }
        }

        private void OnOpenRemoteServicesClick(object sender, RoutedEventArgs e)
        {
            if (_selectedServer != null)
            {
                try {
                    Process.Start(new ProcessStartInfo("services.msc", $"/computer:{_selectedServer.Ip}") { UseShellExecute = true });
                } catch (Exception ex) {
                    UpdateConnectionStatus("Uzak yönetim işlemi tamamlanamadı.", false);
                }
            }
        }

        private void OnScanAllClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var servers = _servers.ToList();
                if (!servers.Any()) return;

                UpdateConnectionStatus($"🔄 {servers.Count} sunucu arka planda taranıyor...", false);

                // Fire-and-forget background scan: the user can still interact with the UI
                GlobalExceptionHandler.Observe(Task.Run(async () =>
                {
                    await BackgroundScanAllAsync(_backgroundScanCts.Token);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateConnectionStatus("✅ Bütün sunucular tarandı ve önbelleğe kaydedildi.", false);
                    });
                }),
                    "Dashboard.ScanAll",
                    notifyUser: true,
                    userMessage: "Sunucu taramas\u0131 tamamlanamad\u0131. Ayr\u0131nt\u0131lar logland\u0131; l\u00fctfen yeniden deneyin.");
            }
            catch (Exception ex)
            {
                _ = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.AsyncCommand,
                    "Dashboard.ScanAll.Start",
                    notifyUser: true,
                    userMessage: "Sunucu taramas\u0131 ba\u015flat\u0131lamad\u0131. L\u00fctfen yeniden deneyin.");
            }
        }

        private void TriggerAiAnalysis(WmiSnapshot snap, ServerInfo server, string localKey, List<WmiSnapshot>? history, long? pingMs, bool rdpOpen, CancellationToken ct, bool force = false)
        {
            using var perf = PerformanceTrace.Start("UpdateAiAnalysis");

            var apiKey = SettingsApplicator.ResolveAiApiKey(SettingsRuntime.Current);
            var canCallAi = FeatureEntitlementService.Instance.IsAiAnalysisEnabled(SettingsRuntime.Current)
                && !string.IsNullOrWhiteSpace(apiKey);

            if (!force && canCallAi)
            {
                if (!SnapshotTtl.IsExpired(_sectionTimestamps.AiAnalysisUtc, SnapshotTtl.AiAnalysis)
                    && _smartInsights.Any(x => x.IsSectionHeader))
                {
                    UpdateAiCacheStatusLabel(_sectionTimestamps.AiAnalysisUtc);
                    _refreshLifecycle?.MarkAiSkipped(_refreshGeneration, localKey, "cache-valid");
                    return;
                }
            }

            if (!_refreshCoordinator.TryBegin(localKey, ServerRefreshCoordinator.RefreshPhase.Ai))
            {
                _refreshLifecycle?.MarkAiSkipped(_refreshGeneration, localKey, "in-flight");
                return;
            }

            _ = Task.Run(async () =>
            {
                var audit = AuditOperationScope.BeginAnalysis(AuditActions.AiAnalysis, server);
                try
                {
                    if (ct.IsCancellationRequested || _insightsKey != localKey)
                    {
                        audit.CompleteCancelled("Analysis superseded or cancelled");
                        return;
                    }

                    var healthResult = CalculateHealthScore(pingMs, snap, rdpOpen);
                    var analysisStartedUtc = DateTime.UtcNow;
                    var ctx = ServerAnalysisPipeline.Build(snap, server, localKey, pingMs, rdpOpen, history, healthResult.Score);
                    _lastAnalysisContext = ctx;
                    var ruleInsights = InsightEngine.BuildRuleOnlyInsights(ctx);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (ct.IsCancellationRequested || _insightsKey != localKey) return;
                        ApplySmartAnalysisInsights(ruleInsights, showLoadingIndicator: canCallAi);
                        AiAnalysisStatusText.Text = "Yenileniyor...";
                        AiAnalysisStatusText.Visibility = Visibility.Visible;
                    });

                    if (!InsightEngine.HasSufficientLiveDataForAi(snap, rdpOpen, pingMs))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (ct.IsCancellationRequested || _insightsKey != localKey) return;
                            ApplySmartAnalysisInsights(ruleInsights, showLoadingIndicator: false);
                            AiAnalysisStatusText.Visibility = Visibility.Collapsed;
                        });
                        audit.CompleteSuccess();
                        return;
                    }

                    if (ct.IsCancellationRequested || _insightsKey != localKey)
                    {
                        audit.CompleteCancelled("Analysis superseded before AI call");
                        return;
                    }

                    AiSmartAnalysisReport? aiReport = null;
                    if (canCallAi && !string.IsNullOrWhiteSpace(apiKey))
                        aiReport = await InsightEngine.GetAiReportAsync(apiKey, localKey, ctx, ct).ConfigureAwait(false);

                    var finalInsights = InsightEngine.MergeAiWithRuleReport(aiReport, ctx);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (ct.IsCancellationRequested || _insightsKey != localKey) return;

                        _previousAiAnalysisUtc = _sectionTimestamps.AiAnalysisUtc;
                        _lastAiAnalysisDuration = DateTime.UtcNow - analysisStartedUtc;

                        var existing = _smartInsights.ToList();
                        if (aiReport != null && InsightEngine.IsQualityAiReport(aiReport))
                        {
                            _lastAiSmartReport = aiReport;
                            ApplySmartAnalysisInsights(finalInsights, showLoadingIndicator: false);
                            CacheAiInsights(localKey);
                        }
                        else if (AiAnalysisFormatter.IsQualityReport(finalInsights))
                        {
                            _lastAiSmartReport = AiAnalysisFormatter.BuildRuleBasedReport(ctx);
                            ApplySmartAnalysisInsights(finalInsights, showLoadingIndicator: false);
                            CacheAiInsights(localKey);
                        }
                        else if (existing.Any(x => x.IsSectionHeader))
                        {
                            ApplySmartAnalysisInsights(existing.Where(x => x.Icon != "🔍").ToList(), showLoadingIndicator: false);
                        }
                        else
                        {
                            _lastAiSmartReport = AiAnalysisFormatter.BuildRuleBasedReport(ctx);
                            ApplySmartAnalysisInsights(ruleInsights, showLoadingIndicator: false);
                            CacheAiInsights(localKey);
                        }

                        _lastAiAnalysisTime = DateTime.Now;
                        UpdateAiCacheStatusLabel(_sectionTimestamps.AiAnalysisUtc);
                        SmartAnalysisNormalText.Visibility = _smartInsights.Any(x => !x.IsSectionHeader)
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    });

                    audit.CompleteSuccess();
                }
                catch (Exception)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (ct.IsCancellationRequested || _insightsKey != localKey) return;
                        var safeMessage = InsightEngine.GetLastAiStatusMessage(localKey) ?? "AI analizi tamamlanamadı. Lütfen tekrar deneyin.";
                        _smartInsights.Add(new SmartInsight("AI analiz hatası", InsightSeverity.Info, "❌", reason: safeMessage, action: "Yenile ile tekrar deneyin"));
                        AiAnalysisStatusText.Visibility = Visibility.Collapsed;
                        UpdateAiWidgetState();
                    });
                    audit.CompleteFailed("AI analizi tamamlanamadı.");
                }
                finally
                {
                    _refreshCoordinator.End(localKey, ServerRefreshCoordinator.RefreshPhase.Ai);
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        if (string.Equals(_insightsKey, localKey, StringComparison.OrdinalIgnoreCase))
                            _refreshLifecycle?.MarkAiFinished(_refreshGeneration, localKey);
                    });
                }
            });
        }

        private void UpdatePerformanceChart()
        {
            using var perf = PerformanceTrace.Start("UpdatePerformanceChart");
            try
            {
                if (SelectedServerPanel.Visibility != Visibility.Visible) return;

                UpdatePerformanceChartPlaceholder();

                var sampleCount = _liveChartSession.SampleCount;
                if (sampleCount == 0)
                {
                    ClearPerformanceChartVisuals();
                    return;
                }

                double w = PerformanceCanvas.ActualWidth;
                double h = PerformanceCanvas.ActualHeight;
                if (w <= 0 || h <= 0)
                {
                    ScheduleDeferredChartUpdate();
                    return;
                }

                var settings = SettingsService.Instance.Current;
                var cpuSeries = settings.MonitorCpu
                    ? _liveChartSession.GetCpuSeries().Select(v => (double)v).ToList()
                    : new List<double>();
                var ramSeries = settings.MonitorRam
                    ? _liveChartSession.GetRamSeries()
                    : new List<double>();
                var diskSeries = settings.MonitorDisk
                    ? _liveChartSession.GetDiskSeries()
                    : new List<double>();
                var networkSeries = settings.MonitorNetwork
                    ? _liveChartSession.GetNetworkSeries()
                    : new List<double>();

                var hasCpu = cpuSeries.Count > 0;
                var hasRam = ramSeries.Count > 0;
                var hasDisk = diskSeries.Count > 0;
                var hasNetwork = networkSeries.Count > 0;
                if (!hasCpu && !hasRam && !hasDisk && !hasNetwork)
                    return;

                var sampleCountChanged = sampleCount != _lastDrawnSampleCount;
                if (!sampleCountChanged && !_chartUpdateThrottle.ShouldUpdate())
                    return;

                _lastChartCpuCount = cpuSeries.Count;
                _lastChartRamCount = ramSeries.Count;
                _lastChartCpuTail = cpuSeries.Count > 0 ? (int)cpuSeries[^1] : int.MinValue;
                _lastChartRamTail = ramSeries.Count > 0 ? ramSeries[^1] : double.MinValue;
                _lastDrawnSampleCount = sampleCount;

                var lastSample = _liveChartSession.Samples.LastOrDefault();
                UpdatePerformanceChartAxisLabels();
                RefreshDebugLog.ChartDraw(
                    _liveChartServerKey ?? _insightsKey ?? "unknown",
                    sampleCount,
                    lastSample?.CpuPercent,
                    lastSample?.RamPercent,
                    lastSample?.DiskPercent,
                    w,
                    h,
                    draw: true);

                if (hasCpu)
                    UpdateHistoryLine(CpuPolyline, CpuLastDot, cpuSeries, w, h);
                else
                {
                    CpuPolyline.Points.Clear();
                    CpuLastDot.Visibility = Visibility.Collapsed;
                }

                if (hasRam)
                    UpdateHistoryLine(RamPolyline, RamLastDot, ramSeries.ToList(), w, h);
                else
                {
                    RamPolyline.Points.Clear();
                    RamLastDot.Visibility = Visibility.Collapsed;
                }

                if (hasDisk)
                    UpdateHistoryLine(DiskPolyline, DiskLastDot, diskSeries.ToList(), w, h);
                else
                {
                    DiskPolyline.Points.Clear();
                    DiskLastDot.Visibility = Visibility.Collapsed;
                }

                if (hasNetwork)
                    UpdateHistoryLine(NetworkPolyline, NetworkLastDot, networkSeries.ToList(), w, h);
                else
                {
                    NetworkPolyline.Points.Clear();
                    NetworkLastDot.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private void UpdateHistoryLine(Polyline polyline, Ellipse lastDot, List<double> values, double w, double h)
        {
            const double plotLeft = 32;
            const double plotRight = 12;
            const double plotTop = 8;
            const double plotBottom = 24;
            var plotWidth = Math.Max(1, w - plotLeft - plotRight);
            var plotHeight = Math.Max(1, h - plotTop - plotBottom);

            if (values.Count == 0)
            {
                polyline.Points.Clear();
                lastDot.Visibility = Visibility.Collapsed;
                return;
            }

            if (values.Count == 1)
            {
                polyline.Points.Clear();
                double x = Math.Round(plotLeft + plotWidth * 0.92);
                var clamped = Math.Clamp(values[0], 0, 100);
                double y = Math.Round(plotTop + plotHeight - (clamped / 100.0 * plotHeight));
                lastDot.Margin = new Thickness(x - 2, y - 2, 0, 0);
                lastDot.Visibility = Visibility.Visible;
                return;
            }

            var points = new PointCollection(values.Count);
            double step = values.Count > 1 ? plotWidth / (values.Count - 1) : 0;
            for (int i = 0; i < values.Count; i++)
            {
                double x = Math.Round(plotLeft + i * step);
                var clamped = Math.Clamp(values[i], 0, 100);
                double y = Math.Round(plotTop + plotHeight - (clamped / 100.0 * plotHeight));
                points.Add(new System.Windows.Point(x, y));
            }

            polyline.Points = points;

            var lastPoint = points[points.Count - 1];
            lastDot.Margin = new Thickness(lastPoint.X - 2, lastPoint.Y - 2, 0, 0);
            lastDot.Visibility = Visibility.Visible;
        }

        private void UpdatePerformanceChartAxisLabels()
        {
            try
            {
                var samples = _liveChartSession.Samples;
                if (samples.Count == 0)
                    return;

                SetTextIfChanged(PerformanceXAxisStartText, samples[0].TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
                SetTextIfChanged(PerformanceXAxisEndText, samples[^1].TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
                SetTextIfChanged(PerformanceYAxisTopText, "100%");
                SetTextIfChanged(PerformanceYAxisMidText, "50%");
                SetTextIfChanged(PerformanceYAxisBottomText, "0%");
            }
            catch (Exception ex) { AppLogger.Debug($"Performance axis update failed: {ex.Message}", category: "Dashboard", action: "PerformanceChart"); }
        }

        private void OnPerformanceCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (PerformanceCanvas.Visibility != Visibility.Visible)
                    return;

                var samples = _liveChartSession.Samples;
                if (samples.Count < 2)
                {
                    HidePerformanceTooltip();
                    return;
                }

                const double plotLeft = 32;
                const double plotRight = 12;
                var w = PerformanceCanvas.ActualWidth;
                if (w <= plotLeft + plotRight)
                    return;

                var pos = e.GetPosition(PerformanceCanvas);
                var plotWidth = Math.Max(1, w - plotLeft - plotRight);
                var ratio = Math.Clamp((pos.X - plotLeft) / plotWidth, 0, 1);
                var index = (int)Math.Round(ratio * (samples.Count - 1));
                index = Math.Clamp(index, 0, samples.Count - 1);

                var x = plotLeft + plotWidth * index / Math.Max(1, samples.Count - 1);
                PerformanceChartCrosshair.X1 = x;
                PerformanceChartCrosshair.X2 = x;
                PerformanceChartCrosshair.Visibility = Visibility.Visible;

                var sample = samples[index];
                PerformanceTooltipTimeText.Text = sample.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
                PerformanceTooltipCpuText.Text = $"CPU {FormatPercent(sample.CpuPercent)}";
                PerformanceTooltipRamText.Text = $"RAM {FormatPercent(sample.RamPercent)}";
                PerformanceTooltipDiskText.Text = $"Disk {FormatPercent(sample.DiskPercent)}";
                var totalNetwork = (sample.NetworkInBps ?? 0) + (sample.NetworkOutBps ?? 0);
                PerformanceTooltipNetworkText.Text = totalNetwork > 0
                    ? $"Net {LiveDashboardMetricsBuilder.FormatThroughput(totalNetwork)}"
                    : "Net —";

                var tooltipLeft = Math.Min(Math.Max(8, x + 12), Math.Max(8, w - 170));
                var tooltipTop = Math.Max(36, Math.Min(pos.Y + 12, Math.Max(36, PerformanceCanvas.ActualHeight - 110)));
                PerformanceChartTooltip.Margin = new Thickness(tooltipLeft, tooltipTop, 0, 0);
                PerformanceChartTooltip.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { AppLogger.Debug($"Performance tooltip failed: {ex.Message}", category: "Dashboard", action: "PerformanceChart"); }
        }

        private void OnPerformanceCanvasMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => HidePerformanceTooltip();

        private void HidePerformanceTooltip()
        {
            if (PerformanceChartTooltip != null)
                PerformanceChartTooltip.Visibility = Visibility.Collapsed;
            if (PerformanceChartCrosshair != null)
                PerformanceChartCrosshair.Visibility = Visibility.Collapsed;
        }

        private static string FormatPercent(double? value) => value.HasValue ? $"{value.Value:0}%" : "—";

        private static SolidColorBrush GetBadgeBrush(HealthBadge badge)
        {
            if (BadgeBrushCache.TryGetValue(badge, out var cached))
                return cached;

            var brush = new SolidColorBrush(GetBadgeAccentColor(badge));
            brush.Freeze();
            BadgeBrushCache[badge] = brush;
            return brush;
        }

        private static System.Windows.Media.Color GetBadgeAccentColor(HealthBadge badge) => badge switch
        {
            HealthBadge.Healthy => System.Windows.Media.Color.FromRgb(45, 107, 69),    // muted green
            HealthBadge.Warning => System.Windows.Media.Color.FromRgb(139, 105, 20),   // muted amber
            HealthBadge.Critical => System.Windows.Media.Color.FromRgb(143, 64, 73),   // muted rose
            _ => System.Windows.Media.Color.FromRgb(107, 114, 128)                    // gray
        };

        private void ApplySelectedStatusBadge(HealthBadge badge)
        {
            if (SelectedStatusBadge == null || SelectedStatusText == null)
                return;

            var (background, border, text) = badge switch
            {
                HealthBadge.Healthy => (
                    System.Windows.Media.Color.FromRgb(18, 42, 26),
                    System.Windows.Media.Color.FromRgb(45, 107, 69),
                    System.Windows.Media.Color.FromRgb(110, 231, 160)),
                HealthBadge.Warning => (
                    System.Windows.Media.Color.FromRgb(35, 28, 18),
                    System.Windows.Media.Color.FromRgb(139, 105, 20),
                    System.Windows.Media.Color.FromRgb(212, 168, 67)),
                HealthBadge.Critical => (
                    System.Windows.Media.Color.FromRgb(35, 20, 24),
                    System.Windows.Media.Color.FromRgb(143, 64, 73),
                    System.Windows.Media.Color.FromRgb(212, 132, 138)),
                _ => (
                    System.Windows.Media.Color.FromRgb(26, 31, 40),
                    System.Windows.Media.Color.FromRgb(75, 85, 99),
                    System.Windows.Media.Color.FromRgb(156, 163, 175))
            };

            SelectedStatusBadge.Visibility = Visibility.Visible;
            SelectedStatusBadge.Background = new SolidColorBrush(background);
            SelectedStatusBadge.BorderBrush = new SolidColorBrush(border);
            SelectedStatusBadge.BorderThickness = new Thickness(1);
            SelectedStatusText.Foreground = new SolidColorBrush(text);

            if (DashAiRiskBadge != null)
            {
                DashAiRiskBadge.Background = new SolidColorBrush(background);
                DashAiRiskBadge.BorderBrush = new SolidColorBrush(border);
                DashAiRiskBadge.BorderThickness = new Thickness(1);
            }
            if (DashAiRiskDot != null)
                DashAiRiskDot.Fill = new SolidColorBrush(text);
            if (DashAiRiskText != null)
                DashAiRiskText.Foreground = new SolidColorBrush(text);
        }

        private void ApplyHealthGaugeVisual(int score)
        {
            if (DashHealthGaugeScore != null)
                DashHealthGaugeScore.Text = score.ToString();

            if (DashHealthGaugeArc == null)
                return;

            System.Windows.Media.Color arcColor;
            if (score >= 85)
                arcColor = System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80);
            else if (score >= 60)
                arcColor = System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24);
            else
                arcColor = System.Windows.Media.Color.FromRgb(0xD4, 0x84, 0x8A);

            DashHealthGaugeArc.Stroke = new SolidColorBrush(arcColor);
            if (DashHealthGaugeScore != null)
                DashHealthGaugeScore.Foreground = new SolidColorBrush(arcColor);
        }

        private static System.Windows.Media.Color GetBadgeColor(HealthBadge badge) => GetBadgeAccentColor(badge);

        private static System.Windows.Media.Color GetDiskColor(double usedPct)
        {
            if (usedPct < 80) return System.Windows.Media.Color.FromRgb(34, 197, 94);
            if (usedPct < 90) return System.Windows.Media.Color.FromRgb(245, 158, 11);
            return System.Windows.Media.Color.FromRgb(239, 68, 68);
        }

        private static async Task<long?> TryPingMsAsync(string host, int timeoutMs)
        {
            try
            {
                using var p = new System.Net.NetworkInformation.Ping();
                var reply = await p.SendPingAsync(host, timeoutMs);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    return reply.RoundtripTime;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            return null;
        }

        private static async Task<bool> TryTcpOpenAsync(string host, int port, int timeoutMs)
        {
            try
            {
                using var c = new System.Net.Sockets.TcpClient();
                var connectTask = c.ConnectAsync(host, port);
                var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                return done == connectTask && c.Connected;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"TCP port check failed for {host}:{port}.", ex);
                return false;
            }
        }

        private (HealthBadge badge, string? reason) CalculateOverallHealth(long? pingMs, bool rdpOpen, WmiSnapshot? snap)
            => ServerHealthEvaluator.CalculateOverallHealth(pingMs, rdpOpen, snap);

        public record HealthResult(int Score, string Breakdown);

        private HealthResult CalculateHealthScore(long? pingMs, WmiSnapshot? snap, bool rdpOpen)
        {
            int score = InsightEngine.CalculateRiskScore(snap, pingMs, rdpOpen);
            
            var parts = new List<string>();
            if (!rdpOpen) parts.Add("RDP: CLOSED");
            else parts.Add("RDP: OPEN");

            if (pingMs.HasValue)
            {
                if (pingMs < 150) parts.Add("Latency: GOOD");
                else if (pingMs < 300) parts.Add("Latency: FAIR");
                else parts.Add("Latency: POOR");
            }
            else parts.Add("Latency: ?");

            if (snap != null && snap.Connected)
            {
                if (snap.CpuPct < 90) parts.Add("CPU: OK"); else parts.Add("CPU: HIGH");
                if (snap.RamUsedPct < 90) parts.Add("RAM: OK"); else parts.Add("RAM: HIGH");
                if (snap.DiskUsedPct < 90) parts.Add("Disk: OK"); else parts.Add("Disk: FULL");
            }
            else parts.Add("Data: OFFLINE");

            string breakdown = string.Join(" • ", parts);
            return new HealthResult(score, breakdown);
        }

        private void OnOpenConsoleClick(object sender, RoutedEventArgs e)
        {
            var server = GetSelectedServer();
            if (server == null) return;

            try
            {
                SecurePowerShellLauncher.LaunchRemoteConsole(server.Ip, server.Username, server.Password ?? string.Empty);
            }
            catch (Exception ex)
            {
                _ = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.AsyncCommand,
                    "PowerShell.Launch",
                    notifyUser: true,
                    userMessage: "Uzak PowerShell konsolu a\u00e7\u0131lamad\u0131. Ayr\u0131nt\u0131lar logland\u0131.");
            }
        }


        private async void OnDisconnectClick(object sender, RoutedEventArgs e)
        {
            if (_activeSession != null)
            {
                CloseTabSession(_activeSession);
                return;
            }

            HideRdpSessionWorkspace();
            UpdateConnectionStatus("RDP bağlantısı yok.", false);
            await Task.CompletedTask;
        }

        private void OnConfigLostFocus(object sender, RoutedEventArgs e)
        {
            if (_syncingConfigUi) return;
            var server = GetSelectedServer();
            if (server == null) return;

            var oldKey = GetFavoriteServerKey(server);

            // Update model from config panel
            server.Name = ConfigNameBox.Text?.Trim() ?? server.Name;
            server.Ip = ConfigHostBox.Text?.Trim() ?? server.Ip;
            server.Username = ConfigUserBox.Text?.Trim() ?? server.Username;
            server.Password = ConfigPassBox.Password ?? server.Password;
            if (!string.IsNullOrEmpty(server.Password))
                server.StoredPasswordUnavailable = false;
            server.Folder = string.IsNullOrWhiteSpace(ConfigFolderBox.Text) ? server.Folder : ConfigFolderBox.Text.Trim();

            var portText = ConfigPortBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(portText) && int.TryParse(portText, out var port) && port is >= 1 and <= 65535)
            {
                server.Port = port;
            }

            SaveConnectionsToDisk();
            MigrateFavoriteKey(oldKey, GetFavoriteServerKey(server));
            BuildConnectionTree(selectServer: server);
            RefreshFavoritesUi();
        }

        private void FillConfigUi(ServerInfo server)
        {
            _syncingConfigUi = true;
            try
            {
                ConfigNameBox.Text = server.Name;
                ConfigHostBox.Text = server.Ip;
                ConfigUserBox.Text = server.Username;
                ConfigPassBox.Password = server.Password;
                ConfigFolderBox.Text = server.Folder;
                ConfigPortBox.Text = server.Port.ToString();
            }
            finally
            {
                _syncingConfigUi = false;
            }
        }

        private void ClearConfigUi()
        {
            _syncingConfigUi = true;
            try
            {
                ConfigNameBox.Text = string.Empty;
                ConfigHostBox.Text = string.Empty;
                ConfigUserBox.Text = string.Empty;
                ConfigPassBox.Password = string.Empty;
                ConfigFolderBox.Text = string.Empty;
                ConfigPortBox.Text = string.Empty;
            }
            finally
            {
                _syncingConfigUi = false;
            }
        }

        private ServerInfo? GetSelectedServer()
        {
            if (IsDashboardVisible() && _activeDashboardServer != null)
                return _activeDashboardServer;

            return _selectedNode?.Server ?? _selectedServer;
        }

        private void BuildConnectionTree(ServerInfo? selectServer = null)
        {
            _connectionTree.Clear();
            var searchFilter = GetActiveConnectionSearchQuery();
            
            // Get all unique folder names from servers and from explicit folders list
            var folderNames = _servers.Select(s => s.Folder).Concat(_explicitFolders)
                                     .Where(f => !string.IsNullOrWhiteSpace(f))
                                     .Distinct()
                                     .OrderBy(f => f)
                                     .ToList();

            if (!folderNames.Contains("Sahsi")) folderNames.Insert(0, "Sahsi");

            foreach (var folderName in folderNames)
            {
                var folderNode = new ConnectionTreeNode(folderName, subtitle: string.Empty, server: null);
                var serversInFolder = _servers.Where(s => (string.IsNullOrWhiteSpace(s.Folder) ? "Sahsi" : s.Folder) == folderName)
                                             .OrderBy(s => s.Name);
                
                foreach (var s in serversInFolder)
                {
                    if (!ConnectionSearchHelper.Matches(s, folderName, searchFilter))
                        continue;

                    var node = new ConnectionTreeNode(s.Name, subtitle: s.Ip, server: s)
                    {
                        IsFavorite = _favoriteServerKeys.Contains(GetFavoriteServerKey(s))
                    };
                    // Load last known status immediately
                    node.StatusBrush = new SolidColorBrush(GetBadgeColor(s.LastStatus));
                    _serverStatuses[s.Ip] = s.LastStatus;
                    folderNode.Children.Add(node);
                }

                if (!string.IsNullOrWhiteSpace(searchFilter) && folderNode.Children.Count == 0)
                    continue;

                _connectionTree.Add(folderNode);
            }
            
            // Trigger dashboard update once with initial cached values
            UpdateOverviewDashboard();

            if (selectServer != null)
            {
                FillConfigUi(selectServer);
                ApplyActiveDashboardBadges();
            }

            RefreshFavoritesList();
        }

        private void OnRdpGridSplitterDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _suspendRdpSizing = true;
        }

        private void OnRdpGridSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _suspendRdpSizing = false;
            ScheduleRdpResize();
        }

        private async Task RefreshAllSidebarStatusesAsync()
        {
            using var perf = PerformanceTrace.Start("SidebarStatusScan");
            // RDP aktifken veya WMI insights çalışırken sidebar ping taramasını ertele
            if (IsRdpActive())
                return;

            if (IsInsightsRefreshActive())
                return;

            if (Interlocked.CompareExchange(ref _sidebarScanInFlight, 1, 0) != 0)
                return;

            try
            {
                var serversToScan = _servers.ToList();
                var pendingUpdates = new List<(ServerInfo Server, HealthBadge Badge)>();

                var tasks = serversToScan.Select(async server =>
                {
                    await _scanSemaphore.WaitAsync();
                    try
                    {
                        var pingMs = await TryPingMsAsync(server.Ip, 1200);
                        var rdpOpen = await TryTcpOpenAsync(server.Ip, server.Port <= 0 ? 3389 : server.Port, 1200);
                        var (badge, _) = CalculateOverallHealth(pingMs, rdpOpen, null);
                        lock (pendingUpdates)
                        {
                            pendingUpdates.Add((server, badge));
                        }
                    }
                    catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    finally
                    {
                        _scanSemaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                var anyChanged = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var (server, badge) in pendingUpdates)
                    {
                        if (UpdateNodeStatus(server, badge))
                            anyChanged = true;
                    }

                    if (anyChanged)
                        UpdateOverviewDashboard();
                });

                if (anyChanged)
                    SaveConnectionsToDisk();
            }
            finally
            {
                Interlocked.Exchange(ref _sidebarScanInFlight, 0);
            }
        }

        private void UpdateOverviewDashboard()
        {
            try
            {
                int total = _servers.Count;
                int healthy = 0;
                int warning = 0;
                int critical = 0;

                foreach (var status in _serverStatuses.Values)
                {
                    switch (status)
                    {
                        case HealthBadge.Healthy: healthy++; break;
                        case HealthBadge.Warning: warning++; break;
                        case HealthBadge.Critical: critical++; break;
                        case HealthBadge.Offline: critical++; break; // Offline counts as critical for environment health
                    }
                }

                try
                {
                    SidebarTotalText.Text = total.ToString();
                    SidebarHealthyText.Text = healthy.ToString();
                    SidebarWarningText.Text = warning.ToString();
                    SidebarCriticalText.Text = critical.ToString();
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private ServerInfo? GetQuickActionServer()
        {
            if (IsDashboardVisible() && _activeDashboardServer != null)
                return _activeDashboardServer;
            return _activeSession?.Server ?? _selectedServer ?? _selectedNode?.Server;
        }

        private Func<string, Task<bool>>? GetRdpCommandExecutorForServer(ServerInfo server)
        {
            var session = _rdpSessions.FirstOrDefault(s =>
                s.CleanSession?.IsConnected == true &&
                s.Server != null &&
                string.Equals(s.Server.Ip?.Trim(), server.Ip?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (session?.CleanSession == null)
                return null;

            return cmd => session.CleanSession.TryExecuteRemoteShellCommandAsync(cmd);
        }

        private void OnQuickPingClick(object sender, RoutedEventArgs e)
        {
            var server = GetQuickActionServer();
            if (server == null || string.IsNullOrWhiteSpace(server.Ip))
            {
                ShowToast("Önce bir sunucu seçin veya bağlanın.", "⚠️");
                return;
            }

            if (!NetworkHostValidator.TryValidatePingTarget(server.Ip, out var host, out var errorMessage))
            {
                ShowToast(errorMessage, "⚠️");
                UpdateConnectionStatus("Geçersiz ping hedefi.", false);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ping.exe",
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                psi.ArgumentList.Add("-t");
                psi.ArgumentList.Add(host);

                Process.Start(psi);
                UpdateConnectionStatus($"Ping konsolu: {host}", false);
            }
            catch (Exception ex)
            {
                ShowToast("Konsol başlatılamadı. Sunucu adresini ve ağ bağlantısını kontrol edin.", "⚠️");
            }
        }

        private async void OnQuickRestartClick(object sender, RoutedEventArgs e)
        {
            var server = GetQuickActionServer();
            if (server == null || string.IsNullOrWhiteSpace(server.Ip))
            {
                ShowToast("Önce bir sunucu seçin veya bağlanın.", "⚠️");
                return;
            }

            if (!ConfirmActionDialog.Show(this, ConfirmActionKind.Restart, server.Name, server.Ip))
                return;

            using var audit = AuditOperationScope.BeginRemoteAdmin(AuditActions.Restart, server);

            var rdpExec = GetRdpCommandExecutorForServer(server);

            if (rdpExec == null && string.IsNullOrWhiteSpace(server.Username))
            {
                audit.CompleteCancelled("Username not configured");
                ShowToast("Sunucu kullanıcı adı tanımlı değil.", "⚠️");
                return;
            }

            if (rdpExec == null && string.IsNullOrWhiteSpace(server.Password))
            {
                audit.CompleteCancelled("Password not configured");
                ShowToast("Sunucu şifresi tanımlı değil. Önce RDP ile bağlanın veya admin şifresini kaydedin.", "⚠️", 5000);
                return;
            }

            UpdateConnectionStatus($"{server.Name} yeniden başlatılıyor...", false);
            try
            {
                var (success, message) = await WmiCollector.ExecuteOsPowerActionAsync(
                    server.Ip,
                    server.Username ?? string.Empty,
                    server.Password ?? string.Empty,
                    reboot: true,
                    rdpCommandExecutor: rdpExec);
                if (success)
                {
                    audit.CompleteSuccess();
                    ShowToast(message, "🔄");
                    UpdateConnectionStatus(message, true);
                }
                else
                {
                    audit.CompleteFailed(message);
                    ShowToast(message, "⚠️", 8000);
                    UpdateConnectionStatus($"Restart başarısız: {message}", false);
                }
            }
            catch (Exception ex)
            {
                audit.CompleteFailed("Uzak yeniden başlatma başarısız.");
                ShowToast("Yeniden başlatma işlemi tamamlanamadı. Sunucu erişimini kontrol edin.", "⚠️");
                UpdateConnectionStatus("Yeniden başlatma işlemi tamamlanamadı.", false);
            }
        }

        private async void OnQuickShutdownClick(object sender, RoutedEventArgs e)
        {
            var server = GetQuickActionServer();
            if (server == null || string.IsNullOrWhiteSpace(server.Ip))
            {
                ShowToast("Önce bir sunucu seçin veya bağlanın.", "⚠️");
                return;
            }

            if (!ConfirmActionDialog.Show(this, ConfirmActionKind.Shutdown, server.Name, server.Ip))
                return;

            using var audit = AuditOperationScope.BeginRemoteAdmin(AuditActions.Shutdown, server);

            var rdpExec = GetRdpCommandExecutorForServer(server);

            if (rdpExec == null && string.IsNullOrWhiteSpace(server.Username))
            {
                audit.CompleteCancelled("Username not configured");
                ShowToast("Sunucu kullanıcı adı tanımlı değil.", "⚠️");
                return;
            }

            if (rdpExec == null && string.IsNullOrWhiteSpace(server.Password))
            {
                audit.CompleteCancelled("Password not configured");
                ShowToast("Sunucu şifresi tanımlı değil. Önce RDP ile bağlanın veya admin şifresini kaydedin.", "⚠️", 5000);
                return;
            }

            UpdateConnectionStatus($"{server.Name} kapatılıyor...", false);
            try
            {
                var (success, message) = await WmiCollector.ExecuteOsPowerActionAsync(
                    server.Ip,
                    server.Username ?? string.Empty,
                    server.Password ?? string.Empty,
                    reboot: false,
                    rdpCommandExecutor: rdpExec);
                if (success)
                {
                    audit.CompleteSuccess();
                    ShowToast(message, "⏻");
                    UpdateConnectionStatus(message, true);
                }
                else
                {
                    audit.CompleteFailed(message);
                    ShowToast(message, "⚠️", 8000);
                    UpdateConnectionStatus($"Shutdown başarısız: {message}", false);
                }
            }
            catch (Exception ex)
            {
                audit.CompleteFailed("Uzak kapatma başarısız.");
                ShowToast("Kapatma işlemi tamamlanamadı. Sunucu erişimini kontrol edin.", "⚠️");
                UpdateConnectionStatus("Kapatma işlemi tamamlanamadı.", false);
            }
        }

        private bool UpdateNodeStatus(ServerInfo server, HealthBadge badge)
        {
            if (_serverStatuses.TryGetValue(server.Ip, out var existing) &&
                existing == badge &&
                server.LastStatus == badge)
            {
                return false;
            }

            _serverStatuses[server.Ip] = badge;
            server.LastStatus = badge;
            var node = FindNodeByServer(server);
            if (node != null)
            {
                node.StatusBrush = GetBadgeBrush(badge);
            }

            if (_selectedServer != null && _selectedServer.Ip == server.Ip)
            {
                UpdateOverviewDashboard();
            }

            return true;
        }

        private ConnectionTreeNode? FindNodeByServer(ServerInfo server, IEnumerable<ConnectionTreeNode>? nodes = null)
        {
            nodes ??= _connectionTree;
            foreach (var node in nodes)
            {
                if (node.Server == server) return node;
                var found = FindNodeByServer(server, node.Children);
                if (found != null) return found;
            }
            return null;
        }

        private async Task LoadConnectionsAsync()
        {
            _servers.Clear();
            _explicitFolders.Clear();
            var (servers, folders, favorites) = await StorageService.LoadConnectionsAsync(_serverHistory);
            foreach (var s in servers) _servers.Add(s);
            foreach (var f in folders) _explicitFolders.Add(f);
            _favoriteServerKeys.Clear();
            foreach (var key in favorites) _favoriteServerKeys.Add(key);

            // Warm memory + disk snapshots for all servers (non-blocking)
            var cacheTasks = servers.Select(async server =>
            {
                try
                {
                    var key = GetServerKey(server);
                    var persistent = await PersistentSnapshotStore.LoadAsync(key);
                    if (persistent != null && SnapshotMergeHelper.HasSnapshotUiContent(persistent))
                    {
                        var metrics = EnrichMetricsFromSnapshot(persistent);
                        var history = _serverHistory.GetOrAdd(key, _ => new List<WmiSnapshot>());
                        lock (history)
                        {
                            if (!history.Any() || !SnapshotMergeHelper.IsFullWmiMetrics(history[^1]))
                            {
                                if (history.Any())
                                {
                                    if (SnapshotMergeHelper.ShouldAcceptIncomingVolatiles(metrics, history[^1]))
                                        history[^1] = SnapshotMergeHelper.MergeWmiPreferNewer(metrics, history[^1]);
                                }
                                else
                                    history.Add(metrics);
                                SnapshotSecurityService.TrimHistoryInPlace(history);
                            }
                        }

                        var badge = DetermineHealthBadge(metrics);
                        _serverStatuses[server.Ip] = badge;
                        server.LastStatus = badge;
                        return;
                    }

                    var snap = await WmiCollector.LoadFromDiskAsync(server.Ip, server.Username);
                    if (snap == null) return;

                    var legacyKey = GetServerKey(server);
                    var historyLegacy = _serverHistory.GetOrAdd(legacyKey, _ => new List<WmiSnapshot>());
                    lock (historyLegacy)
                    {
                        if (!historyLegacy.Any())
                        {
                            historyLegacy.Add(snap);
                            SnapshotSecurityService.TrimHistoryInPlace(historyLegacy);
                        }
                    }

                    var legacyBadge = DetermineHealthBadge(snap);
                    _serverStatuses[server.Ip] = legacyBadge;
                    server.LastStatus = legacyBadge;
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            });

            await Task.WhenAll(cacheTasks);

            SnapshotInspector.InspectAllDiskSnapshots();
            _ = StartupSnapshotPreloadAsync(servers);

            await Dispatcher.InvokeAsync(() =>
            {
                BuildConnectionTree(SelectRememberedServer());
                RefreshFavoritesUi();
                UpdateOverviewDashboard();
                if (_welcomeOverlayVm.IsWelcomeOverlayVisible)
                    _welcomeOverlayVm.UpdateServerCount(_servers.Count);
                UpdateRdpToolbarState();
                ClearQuickConnectFields();
            });

            _ = LoadQuickConnectHistoryAsync();

            // Startup BackgroundScanAllAsync kaldırıldı:
            // 2sn sonra WMI/agent scan başlatıyordu, kullanıcı RDP açarsa in-flight task'lar bloklardı.
            // Sidebar timer (30sn) ve insights timer periyodik tarama için yeterli.
        }

        private async Task StartupSnapshotPreloadAsync(IReadOnlyList<ServerInfo> servers)
        {
            try
            {
                await Task.Delay(1500).ConfigureAwait(false);
                var settings = SettingsService.Instance.Current;
                if (!settings.EnableBackgroundTasks)
                    return;

                foreach (var server in servers)
                {
                    if (_rdpActive || HasAnyActiveSession())
                        return;

                    var key = GetServerKey(server);
                    if (PersistentSnapshotStore.HasSnapshotFile(key))
                        continue;

                    if (_serverHistory.TryGetValue(key, out var history) && history.Any())
                        continue;

                    var legacy = await PersistentSnapshotStore.TryLoadLegacyOnlyAsync(key).ConfigureAwait(false);
                    if (legacy?.Metrics != null)
                    {
                        var list = _serverHistory.GetOrAdd(key, _ => new List<WmiSnapshot>());
                        lock (list)
                        {
                            if (!list.Any())
                                list.Add(legacy.Metrics);
                        }
                    }

                    await Task.Delay(2500).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Startup snapshot preload failed.", ex, category: "Snapshot", action: "Preload");
            }
        }

        private HealthBadge DetermineHealthBadge(WmiSnapshot snap)
        {
            if (snap == null || !snap.Connected) return HealthBadge.Offline;

            bool critical = (snap.CpuPct.HasValue && snap.CpuPct.Value > 90) ||
                            (snap.DiskUsedPct.HasValue && snap.DiskUsedPct.Value > 90) ||
                            (snap.RamUsedPct.HasValue && snap.RamUsedPct.Value > 90);
            bool warning = (snap.CpuPct.HasValue && snap.CpuPct.Value > 70) ||
                           (snap.DiskUsedPct.HasValue && snap.DiskUsedPct.Value > 75) ||
                           (snap.RamUsedPct.HasValue && snap.RamUsedPct.Value > 80);

            if (critical) return HealthBadge.Critical;
            if (warning) return HealthBadge.Warning;
            return HealthBadge.Healthy;
        }

        private async Task BackgroundScanAllAsync(CancellationToken ct)
        {
            try
            {
                var servers = _servers.ToList();
                if (!servers.Any()) return;

                // Dashboard açıkken yalnızca seçili sunucuyu tara — arka planda tüm WMI yükünü kes.
                if (SelectedServerPanel?.Visibility == Visibility.Visible && _selectedNode?.Server != null)
                {
                    servers = new List<ServerInfo> { _selectedNode.Server };
                }

                var beforeSnapshot = CapturePersistedFingerprints();

                foreach (var server in servers)
                {
                    if (ct.IsCancellationRequested) break;
                    if (IsRdpActive()) break;

                    await RefreshInsightsAsync(server, ct, force: false);
                }

                if (!ct.IsCancellationRequested && !IsRdpActive())
                {
                    await Dispatcher.InvokeAsync(() => SaveConnectionsToDiskIfPersistedChanged(beforeSnapshot));
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private sealed class PersistedConnectionFingerprint
        {
            public HealthBadge LastStatus { get; init; }
            public int HistoryCount { get; init; }
        }

        private Dictionary<string, PersistedConnectionFingerprint> CapturePersistedFingerprints()
        {
            var result = new Dictionary<string, PersistedConnectionFingerprint>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in _servers)
            {
                var key = GetServerKey(server);
                var historyCount = 0;
                if (_serverHistory.TryGetValue(key, out var history))
                    historyCount = history.Count;

                result[key] = new PersistedConnectionFingerprint
                {
                    LastStatus = server.LastStatus,
                    HistoryCount = historyCount
                };
            }

            return result;
        }

        private static bool HasPersistedConnectionChanges(
            Dictionary<string, PersistedConnectionFingerprint> before,
            IEnumerable<ServerInfo> servers,
            System.Collections.Concurrent.ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            Func<ServerInfo, string> getServerKey)
        {
            foreach (var server in servers)
            {
                var key = getServerKey(server);
                if (!before.TryGetValue(key, out var old))
                    return true;

                var historyCount = 0;
                if (serverHistory.TryGetValue(key, out var history))
                    historyCount = history.Count;

                if (old.LastStatus != server.LastStatus || old.HistoryCount != historyCount)
                    return true;
            }

            return false;
        }

        private void SaveConnectionsToDiskIfPersistedChanged(Dictionary<string, PersistedConnectionFingerprint> beforeSnapshot)
        {
            if (HasPersistedConnectionChanges(beforeSnapshot, _servers, _serverHistory, GetServerKey))
                SaveConnectionsToDisk();
        }

        private void SaveConnectionsToDisk()
        {
            _ = StorageService.SaveConnectionsAsync(_servers, _explicitFolders, _serverHistory, _favoriteServerKeys);
        }

        private void OnShowAddServerClick(object sender, RoutedEventArgs e)
        {
            ServerIpBox.Focus();
        }

        private void OnClosePremiumPromoClick(object sender, RoutedEventArgs e)
        {
            PremiumPromoCard.Visibility = Visibility.Collapsed;
        }

        private bool _quickConnectExpanded = true;

        private void OnToggleQuickConnectPanel(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _quickConnectExpanded = !_quickConnectExpanded;
            QuickConnectBody.Visibility = _quickConnectExpanded ? Visibility.Visible : Visibility.Collapsed;
            QuickConnectChevron.Text = _quickConnectExpanded ? "\uE70E" : "\uE70D";
        }

        private void OnSortConnectionsClick(object sender, RoutedEventArgs e)
        {
            var sorted = _servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _servers.Clear();
            foreach (var server in sorted)
                _servers.Add(server);
            SaveConnectionsToDisk();
            BuildConnectionTree(selectServer: _selectedServer);
        }

        private void OnExpandAllFoldersClick(object sender, RoutedEventArgs e) => SetAllTreeItemsExpanded(true);

        private void OnCollapseAllFoldersClick(object sender, RoutedEventArgs e) => SetAllTreeItemsExpanded(false);

        private void SetAllTreeItemsExpanded(bool expanded)
        {
            ConnectionsTree.UpdateLayout();
            foreach (var item in EnumerateTreeViewItems(ConnectionsTree))
                item.IsExpanded = expanded;
        }

        private static IEnumerable<TreeViewItem> EnumerateTreeViewItems(ItemsControl parent)
        {
            for (int i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                {
                    parent.UpdateLayout();
                    if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem retryItem)
                        continue;
                    item = retryItem;
                }

                yield return item;
                foreach (var child in EnumerateTreeViewItems(item))
                    yield return child;
            }
        }

        private void OnConnectionItemMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;

            if (btn.Tag is Models.ConnectionTreeNode node)
            {
                _selectedNode = node;
                if (node.Server != null)
                    _selectedServer = node.Server;
            }

            DependencyObject? current = btn;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.ContextMenu != null)
                {
                    fe.ContextMenu.PlacementTarget = btn;
                    fe.ContextMenu.IsOpen = true;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        private void OnOpenAddPanelClick(object sender, RoutedEventArgs e)
        {
            ShowAddServerPanel(GetFolderForAddFromSelection());
        }

        private void ShowAddServerPanel(string folder)
        {
            AddServerPanel.Visibility = Visibility.Visible;
            OpenAddPanelButton.Visibility = Visibility.Collapsed;
            RdpPlaceholder.Visibility = Visibility.Collapsed;
            if (RdpHost != null) RdpHost.Visibility = Visibility.Collapsed;
            try { if (SelectedServerPanel != null) SelectedServerPanel.Visibility = Visibility.Collapsed; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try
            {
                AddFolderBox.Text = string.IsNullOrWhiteSpace(folder) ? "Sahsi" : folder;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            ServerIpBox.Focus();
        }

        private string GetFolderForAddFromSelection()
        {
            var node = _selectedNode;
            if (node?.Server != null)
            {
                return string.IsNullOrWhiteSpace(node.Server.Folder) ? "Sahsi" : node.Server.Folder;
            }
            if (node != null)
            {
                return string.IsNullOrWhiteSpace(node.Title) ? "Sahsi" : node.Title;
            }
            return "Sahsi";
        }

        private void OnAddServerToNodeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var mi = sender as MenuItem;
                var node = mi?.DataContext as ConnectionTreeNode;

                if (node == null)
                {
                    var cm = mi?.Parent as ContextMenu ?? (mi?.CommandParameter as ContextMenu);
                    var tvi = cm?.PlacementTarget as TreeViewItem;
                    node = tvi?.DataContext as ConnectionTreeNode;
                }

                if (node == null) node = _selectedNode;

                var folder = "Sahsi";
                if (node?.Server != null)
                {
                    folder = string.IsNullOrWhiteSpace(node.Server.Folder) ? "Sahsi" : node.Server.Folder;
                }
                else if (node != null)
                {
                    folder = string.IsNullOrWhiteSpace(node.Title) ? "Sahsi" : node.Title;
                }
                ShowAddServerPanel(folder);
            }
            catch
            {
                ShowAddServerPanel("Sahsi");
            }
        }

        private void OnAddFolderClick(object sender, RoutedEventArgs e)
        {
            var folderName = "Yeni Klasör";
            int counter = 1;
            while (_explicitFolders.Contains(folderName) || _servers.Any(s => s.Folder == folderName))
            {
                folderName = $"Yeni Klasör {counter++}";
            }

            _explicitFolders.Add(folderName);
            BuildConnectionTree();
            SaveConnectionsToDisk();
            
            // Optional: select the new folder node if possible
        }


        private System.Windows.Controls.TextBox? GetServerNameBox()
        {
            return FindName("ServerNameBox") as System.Windows.Controls.TextBox;
        }

        private void OnCloseAddPanelClick(object sender, RoutedEventArgs e)
        {
            AddServerPanel.Visibility = Visibility.Collapsed;
            OpenAddPanelButton.Visibility = Visibility.Visible;
            UpdateConnectionStatus(ConnectionStatusText.Text, false);
        }

        private ServerInfo ResolveServerForConnection(ServerInfo server)
        {
            var settings = SettingsRuntime.Current;
            var (user, pass) = SettingsApplicator.ResolveCredentials(server, settings);
            if (!settings.WindowsAuthentication && !settings.CredentialManagerEnabled &&
                !settings.UseDefaultCredentials)
                return server;

            var resolved = new ServerInfo(server.Name, server.Ip, user, pass, server.Folder, server.Port)
            {
                LastStatus = server.LastStatus
            };
            SettingsApplicator.PersistServerCredentials(resolved, settings);
            return resolved;
        }

        private void ConnectToServer(ServerInfo server, bool quickConnect = false)
        {
            if (server == null)
                return;

            if (string.IsNullOrWhiteSpace(server.Ip))
            {
                ShowToast("Server host is missing.", "\uE783", 4000);
                System.Windows.MessageBox.Show(
                    "Server host is missing.",
                    "Connect",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var resolved = ResolveServerForConnection(server);
            if (string.IsNullOrWhiteSpace(resolved.Username))
            {
                ShowToast("Username is required for RDP connection.", "\uE783", 4000);
                System.Windows.MessageBox.Show(
                    "Username is required for RDP connection. Set credentials for this server and try again.",
                    "Connect",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AuditLogService.LogRequest(
                quickConnect ? AuditActions.QuickConnect : AuditActions.Connect,
                AuditCategories.Rdp,
                server,
                quickConnect
                    ? $"Quick Connect used for {server.Name} ({server.Ip})."
                    : $"RDP connection started for {server.Name} ({server.Ip}).",
                JsonSerializer.Serialize(new { quickConnect, server.Port }));

            if (server.StoredPasswordUnavailable)
            {
                _pendingConnectAudit?.CompleteFailed("Stored password could not be decrypted");
                _pendingConnectAudit = null;
                ShowToast("Kayıtlı parola okunamadı. Lütfen bu bağlantının parolasını tekrar girip kaydedin.", "⚠️", 6000);
                System.Windows.MessageBox.Show(
                    "Kayıtlı parola okunamadı. Lütfen bu bağlantının parolasını tekrar girip kaydedin.",
                    "Parola Okunamadı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _connectAsQuickConnect = quickConnect;
            _pendingServer = resolved;
            _pendingConnectAudit = AuditOperationScope.BeginRdp(
                quickConnect ? AuditActions.QuickConnect : AuditActions.Connect,
                resolved,
                newCorrelation: true);
            try { StopInsightsRefresh(); } catch (Exception ex) { AppLogger.Error("Failed to stop insights refresh before connect.", ex, category: "RDP", action: "Connect"); }
            GlobalExceptionHandler.Observe(
                PersistLastConnectedServerAsync(resolved),
                "Settings.PersistLastConnectedServer",
                notifyUser: false);
            LaunchEmbeddedMstsc(resolved);
        }

        private void UpdateConnectionStatus(string message, bool isConnected)
        {
            ConnectionStatusText.Text = message;
            
            bool isConnecting = IsAnyRdpConnecting();


            if (isConnected || isConnecting)
            {
                RdpPlaceholder.Visibility = Visibility.Collapsed;
                SelectedServerPanel.Visibility = Visibility.Collapsed;

                ShowRdpSessionWorkspace();

                if (isConnecting && !isConnected)
                {
                    if (RdpLoadingOverlay != null) RdpLoadingOverlay.Visibility = Visibility.Visible;
                    if (RdpLoadingStatusText != null && !string.IsNullOrWhiteSpace(message))
                        RdpLoadingStatusText.Text = message;
                }
                
                if (isConnected)
                {
                    if (!IsRdpWarmingUp())
                    {
                        if (RdpLoadingOverlay != null) RdpLoadingOverlay.Visibility = Visibility.Collapsed;
                    }
                    else if (RdpLoadingOverlay != null)
                    {
                        RdpLoadingOverlay.Visibility = Visibility.Visible;
                        if (RdpLoadingStatusText != null)
                            RdpLoadingStatusText.Text = RdpWaitingForScreenMessage;
                    }

                    ApplyTopChromeLayout(false);

                    // Show active connection info in the top bar dashboard area
                    ActiveConnectionInfoPanel.Visibility = Visibility.Visible;
                    if (ActiveConnectionBadge != null) ActiveConnectionBadge.Visibility = Visibility.Visible;
                    var server = _activeSession?.Server ?? _pendingServer ?? _selectedNode?.Server;
                    ActiveConnStatusText.Text = server?.Name ?? "Sunucu";
                    ActiveConnIpText.Text = server?.Ip ?? "";
                    StopInsightsRefresh();
                }
                return;
            }

            // Not connected AND not connecting: hide active info, show overview if a server is selected
            if (RdpLoadingOverlay != null) RdpLoadingOverlay.Visibility = Visibility.Collapsed;

            if (HasOpenRdpTab())
            {
                ConnectionStatusText.Text = message;
                ShowRdpSessionWorkspace();
                UpdateConnectButtonState();
                return;
            }

            // Eğer aktif bir tab oturumu varsa hiçbir şeyi değiştirme — zoom değişimi gibi
            // geçici durumların yanlışlıkla ekranı değiştirmesini önler.
            if (HasAnyActiveSession())
                return;

            ActiveConnectionInfoPanel.Visibility = Visibility.Collapsed;
            if (ActiveConnectionBadge != null) ActiveConnectionBadge.Visibility = Visibility.Collapsed;

            if (_selectedNode?.Server != null)
            {
                RestoreRdpChromeAfterSession();
                RdpPlaceholder.Visibility = Visibility.Collapsed;
                SelectedServerPanel.Visibility = Visibility.Visible;
                HideRdpSessionWorkspace();
                ActivateDashboardForServer(_selectedNode.Server);
            }
            else
            {
                SelectedServerPanel.Visibility = Visibility.Collapsed;
                RdpPlaceholder.Visibility = Visibility.Visible;
                HideRdpSessionWorkspace();
                StopInsightsRefresh();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { StopConnectionMonitor(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            
            // Close all tab sessions
            foreach (var session in _rdpSessions.ToList())
            {
                try { CloseTabSession(session); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            }
            
            // Close all tab sessions — handled above via CloseTabSession
            
            try { _trayService?.Dispose(); } catch (Exception ex) { AppLogger.Error("Sistem tepsisi kaynağı kapatılamadı.", ex); }

            base.OnClosed(e);
        }

        private void OnRdpHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncRdpHostChildBounds();
        }

        private void StartConnectionMonitor(string ip)
        {
            // Legacy monitor is no longer used; the active Clean RDP session handles timeout, retry, and profile selection.
            return;
        }

        private string BuildRdpDiagnostic()
        {
            RdpSessionHostControl? host = null;
            try { host = GetActiveRdpHostControl(); } catch { /* host not prepared */ }
            var diagResult = RdpDiagnostics.Run(host?.ActiveControlClsid, host?.ActiveControlType);
            var reason = _activeSession?.CleanSession == null ? "Bağlantı yok" : "Bilgi yok";
            var viewportDiag = host?.GetViewportDiagnostics();
            
            return $"--- Sistem Durumu ---{Environment.NewLine}"
                + $"STA Thread: {diagResult.IsStaThread}{Environment.NewLine}"
                + $"CLSID Kayitli: {diagResult.ClsidRegistered}{Environment.NewLine}"
                + $"Secilen ActiveX: {diagResult.SelectedActiveXVersion ?? "Henuz secilmedi"}{Environment.NewLine}"
                + $"Oturum ID: {_activeSession?.TabView?.SessionId ?? "n/a"}{Environment.NewLine}"
                + $"Goruntu modu: {viewportDiag?.RenderingMode ?? "n/a"}{Environment.NewLine}"
                + $"SmartSizing: {viewportDiag?.SmartSizing == true}{Environment.NewLine}"
                + $"Etkin CLSID: {diagResult.SelectedClsid ?? "n/a"}{Environment.NewLine}"
                + $"MSTSC Mevcut: {diagResult.MstscExists}{Environment.NewLine}"
                + $"ActiveX Instance: {diagResult.CanCreateInstance}{Environment.NewLine}"
                + $"OS/App: {diagResult.OSArchitecture}/{diagResult.AppArchitecture}{Environment.NewLine}"
                + $"Versiyon: {diagResult.OcxVersion}{Environment.NewLine}{Environment.NewLine}"
                + $"--- Baglanti Durumu ---{Environment.NewLine}"
                + $"Son Sebep: {reason}{Environment.NewLine}"
                + $"Hata Mesaji: {diagResult.ErrorMessage}";        }

        private void StopConnectionMonitor()
        {
            if (_connectionTimer == null)
            {
                return;
            }
            _connectionTimer.Stop();
            _connectionTimer = null;
        }








        private void OnActionsClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                var menu = new System.Windows.Controls.ContextMenu();
                var mgr = GetActiveCleanSession();

                if (mgr != null && GetActiveTabView()?.Visibility == Visibility.Visible)
                {
                    var ctrlAltDelItem = new System.Windows.Controls.MenuItem { Header = "Ctrl+Alt+Del Gönder", Icon = "⌨️" };
                    ctrlAltDelItem.Click += (s, ev) =>
                    {
                        if (ConfirmSendCtrlAltDel())
                            mgr.SendCtrlAltDel();
                    };
                    menu.Items.Add(ctrlAltDelItem);

                    var reconnectItem = new System.Windows.Controls.MenuItem { Header = "Yeniden Bağlan", Icon = "🔄" };
                    reconnectItem.Click += async (s, ev) =>
                    {
                        var server = _activeSession?.Server ?? _pendingServer;
                        using var audit = AuditOperationScope.BeginRdp(AuditActions.Reconnect, server, _activeSession?.Id);
                        try
                        {
                            var host = GetActiveTabView()?.HostControl;
                            if (host != null)
                                await mgr.ReconnectAsync(host, CancellationToken.None);
                            if (mgr.IsConnected)
                                audit.CompleteSuccess();
                            else
                                audit.CompleteFailed("Reconnect completed without active connection");
                        }
                        catch (Exception ex)
                        {
                            audit.CompleteFailed(ex.Message, ex);
                        }
                    };
                    menu.Items.Add(reconnectItem);

                    var refreshItem = new System.Windows.Controls.MenuItem { Header = "Ekranı Yenile (Zorla)", Icon = "📺" };
                    refreshItem.Click += (s, ev) => mgr.ForceRefresh();
                    menu.Items.Add(refreshItem);

                    var clipboardItem = new System.Windows.Controls.MenuItem { Header = "Pano Paylaşımı (Clipboard)", Icon = "📋", IsCheckable = true, IsChecked = _rdpToolbarVm.ClipboardSyncEnabled };
                    clipboardItem.Click += (s, ev) =>
                    {
                        clipboardItem.IsChecked = !clipboardItem.IsChecked;
                        var enabled = clipboardItem.IsChecked == true;
                        var server = _activeSession?.Server ?? _pendingServer;
                        var action = enabled ? AuditActions.ClipboardEnable : AuditActions.ClipboardDisable;
                        using var audit = AuditOperationScope.BeginRdp(action, server, _activeSession?.Id);
                        try
                        {
                            mgr.SetClipboardRedirect(enabled);
                            _rdpToolbarVm.ClipboardSyncEnabled = enabled;
                            audit.CompleteSuccess();
                            ShowToast(enabled ? "Pano paylaşımı aktif" : "Pano paylaşımı pasif", "📋");
                        }
                        catch (Exception ex)
                        {
                            audit.CompleteFailed(ex.Message, ex);
                            ShowToast("Pano ayarı değiştirilemedi.", "⚠️");
                        }
                    };
                    menu.Items.Add(clipboardItem);

                    menu.Items.Add(new System.Windows.Controls.Separator());
                }

                var exportItem = new System.Windows.Controls.MenuItem { Header = "Export Configuration", Icon = new System.Windows.Controls.TextBlock { Text = "\uE74D", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets") } };
                exportItem.Click += (s, ev) => OnQuickExportConfigClick();
                
                var importItem = new System.Windows.Controls.MenuItem { Header = "Import Configuration", Icon = new System.Windows.Controls.TextBlock { Text = "\uE8B7", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets") } };
                importItem.Click += (s, ev) => OnQuickImportConfigClick();
                
                menu.Items.Add(exportItem);
                menu.Items.Add(importItem);
                menu.Items.Add(new System.Windows.Controls.Separator());
                
                var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings", Icon = new System.Windows.Controls.TextBlock { Text = "\uE713", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets") } };
                settingsItem.Click += (s, ev) => OpenSettingsWindow();
                menu.Items.Add(settingsItem);

                btn.ContextMenu = menu;
                menu.IsOpen = true;
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.F11)
            {
                ToggleRdpViewportFullscreen();
                e.Handled = true;
            }
        }

        private bool _isAiPanelOpen = false;

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _welcomeOverlayVm.IsWelcomeOverlayVisible)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (_dashExpandedPanel != null)
                {
                    RestoreDashPanel();
                    e.Handled = true;
                    return;
                }

                if (_rdpViewportVm.IsRdpFocusMode)
                {
                    ExitRdpFocusMode();
                    e.Handled = true;
                    return;
                }

                if (_chromeResizeVm.IsMeasured && !_chromeResizeVm.IsAtDefault)
                {
                    ResetRdpChromeLayout(animated: true);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                GlobalSearchBox?.Focus();
                GlobalSearchBox?.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.M && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (IsEditableInputFocused())
                    return;

                if (!IsAiGuideEntitled)
                {
                    ShowPremiumPlusRequiredPrompt();
                    e.Handled = true;
                    return;
                }

                if (_isAiPanelOpen)
                    TryBeginAiGuideCaptureOrNotify();
                else
                    ToggleAiPanel();
                e.Handled = true;
            }
        }

        private static bool IsEditableInputFocused()
        {
            var focused = Keyboard.FocusedElement;
            return focused is System.Windows.Controls.TextBox
                or System.Windows.Controls.PasswordBox
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.RichTextBox
                or System.Windows.Controls.Primitives.TextBoxBase;
        }

        private void OnToggleAiPanelClick(object sender, RoutedEventArgs e) => ToggleAiPanel();

        private void TryOpenAiGuidePanelContent()
        {
            if (!IsAiGuideEntitled)
            {
                EnforceAiGuideLicenseState();
                return;
            }

            if (ShouldBlockAiGuideForRdp())
            {
                AIGuideAnalysisLogger.CaptureSkippedBecauseRdpNotStable(
                    _aiGuidePausedForRdpConnection ? "paused" : "not-stable");
                SetAiGuideInputEnabled(false, AIGuideLanguage.RdpLoadingMessage);
                UpdateAiGuideStatus(
                    _aiGuidePausedForRdpConnection || IsAnyRdpConnecting()
                        ? AIGuideLanguage.AiGuidePausedForConnection
                        : AIGuideLanguage.RdpLoadingMessage,
                    "#FBBF24",
                    string.Empty);
                return;
            }

            if (HasRdpViewportForCapture())
            {
                if (IsRdpReadyForCapture())
                    TryBeginAiGuideCaptureOrNotify();
            }
            else
            {
                TryBeginAiGuideCaptureOrNotify();
            }
        }

        private void ToggleAiPanel()
        {
            if (!_isAiPanelOpen && !IsAiGuideEntitled)
            {
                ShowPremiumPlusRequiredPrompt();
                return;
            }

            _isAiPanelOpen = !_isAiPanelOpen;

            if (_rdpViewportVm.IsRdpFocusMode)
            {
                AiPanelColumn.Width = _isAiPanelOpen ? new GridLength(360) : new GridLength(0);
                AiSplitterColumn.Width = _isAiPanelOpen ? new GridLength(4) : new GridLength(0);
                AiPanelBorder.Visibility = _isAiPanelOpen ? Visibility.Visible : Visibility.Collapsed;
                StatusAiText.Text = _isAiPanelOpen ? "AI Guide: Open" : "AI Guide: Closed";
                StatusAiDot.Fill = new SolidColorBrush(_isAiPanelOpen
                    ? System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)
                    : System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA7));
                if (_isAiPanelOpen)
                {
                    if (SettingsRuntime.Current.AnimationsEnabled)
                    {
                        var slideIn = CloneStoryboard("SlideInFromRight");
                        if (slideIn != null) AiPanelBorder.BeginStoryboard(slideIn);
                    }
                    TryOpenAiGuidePanelContent();
                }
                else
                {
                    CancelAiGuideCapture();
                }
                ScheduleRdpResize();
                return;
            }

            AiPanelColumn.Width = _isAiPanelOpen ? new GridLength(360) : new GridLength(0);
            AiSplitterColumn.Width = _isAiPanelOpen ? new GridLength(4) : new GridLength(0);

            if (_isAiPanelOpen)
            {
                AiPanelBorder.Visibility = Visibility.Visible;
                StatusAiText.Text = "AI Guide: Open";
                StatusAiDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E));
                if (SettingsRuntime.Current.AnimationsEnabled)
                {
                    var slideIn = CloneStoryboard("SlideInFromRight");
                    if (slideIn != null)
                        AiPanelBorder.BeginStoryboard(slideIn);
                }
                TryOpenAiGuidePanelContent();
            }
            else
            {
                AiPanelBorder.Visibility = Visibility.Collapsed;
                StatusAiText.Text = "AI Guide: Closed";
                StatusAiDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA7));
                CancelAiGuideCapture();
            }
        }

        private void CancelAiGuideCapture()
        {
            CancelAiGuideGuidanceInFlight();
            try { _aiGuideCaptureCts?.Cancel(); } catch { /* ignore */ }
            try { _aiGuideCaptureCts?.Dispose(); } catch { /* ignore */ }
            _aiGuideCaptureCts = null;
            _aiGuideService?.ClearSession();
            ResetAiGuideSummaryUi();
            _aiChatMessages.Clear();
            UpdateAiGuideStatus(AIGuideLanguage.OpenHint, "#64748B", string.Empty);
        }

        private void CancelAiGuideAnalysisInFlight()
        {
            try { _aiGuideCaptureCts?.Cancel(); } catch { /* ignore */ }
            CancelAiGuideGuidanceInFlight();
            _aiGuideService?.CancelInFlightAnalysis();
            if (_isAiPanelOpen)
                RecoverAiGuideUiAfterCancelledAnalysis(cancelled: true);
        }

        private void CancelAiGuideGuidanceInFlight()
        {
            try { _aiGuideGuidanceCts?.Cancel(); } catch { /* ignore */ }
            try { _aiGuideGuidanceCts?.Dispose(); } catch { /* ignore */ }
            _aiGuideGuidanceCts = null;
        }

        private void RunOnAiGuideUi(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }

        private Task<T> RunOnAiGuideUiAsync<T>(Func<T> func)
        {
            if (Dispatcher.CheckAccess())
                return Task.FromResult(func());
            return Dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task;
        }
        private async Task WaitForLatestAiGuideFrameAsync(CancellationToken ct)
        {
            var hasRdpViewport = await Dispatcher.InvokeAsync(() =>
            {
                RdpContentGrid?.UpdateLayout();
                GetActiveRdpHostControl()?.Refresh();
                return HasRdpViewportForCapture();
            }, DispatcherPriority.Render);

            // ActiveX paints independently from WPF. Give an already queued remote frame one
            // display interval before resolving physical bounds and copying the screen pixels.
            if (hasRdpViewport)
                await Task.Delay(75, ct).ConfigureAwait(false);
        }




        private static bool IsRdpCaptureNotReady(ScreenCaptureSource source, MainWindow window) =>
            source == ScreenCaptureSource.RdpSession
            && (window.ShouldBlockAiGuideForRdp()
                || !window.IsRdpReadyForCapture());

        private static bool IsRdpCaptureContentUnavailable(ScreenCaptureSource source, ScreenContext screen) =>
            !screen.HasImage;

        private void LogAiGuidePipelineCheckpoint(long analysisId, int runId, string checkpoint)
        {
            _aiGuidePipelineCheckpoint = checkpoint;
            AIGuideAnalysisLogger.PipelineCheckpoint(analysisId, runId, checkpoint);
        }

        private static bool ShouldAbortAfterVision(ScreenCaptureSource source, VisionAnalysisResult result)
        {
            if (!result.Success || result.Observation == null)
                return true;

            if (source != ScreenCaptureSource.RdpSession)
                return false;

            if (result.Observation.Confidence >= 0.45)
                return false;

            return result.Observation.Limitations.Any(l =>
                       l.Contains("blank", StringComparison.OrdinalIgnoreCase)
                       || l.Contains("black", StringComparison.OrdinalIgnoreCase)
                       || l.Contains("rdp", StringComparison.OrdinalIgnoreCase)
                       || l.Contains("partial", StringComparison.OrdinalIgnoreCase))
                   || result.ImageBytesLength <= 0;
        }

        private void RecoverAiGuideUiAfterCancelledAnalysis(bool cancelled = false, int runId = 0)
        {
            if (!_isAiPanelOpen || _aiGuideService == null)
                return;

            if (runId != 0 && runId != _aiGuideCaptureRunId)
                return;

            var lastDashboard = _aiGuideService.LastMentorDashboard;
            if (lastDashboard != null && _aiGuideService.LastAnalysis?.Success == true)
            {
                ApplyVisualMentorDashboard(lastDashboard);
                ClearAiGuideFailureUi();
                UpdateAiGuideStatus(
                    lastDashboard.IsReady ? AIGuideLanguage.Ready : AIGuideLanguage.NeedsFocus,
                    lastDashboard.IsReady ? "#22C55E" : "#FBBF24",
                    string.Empty);
                SetAiGuideInputEnabled(true, AIGuideLanguage.InputHintReady);
                return;
            }

            if (cancelled)
            {
                ClearAiGuideFailureUi();
                UpdateAiGuideStatus(AIGuideLanguage.Cancelled, "#64748B", AIGuideLanguage.AnalysisCancelledDetail);
                SetAiGuideInputEnabled(
                    _aiGuideService.LastAnalysis?.Success == true,
                    _aiGuideService.LastAnalysis?.Success == true
                        ? AIGuideLanguage.InputHintReady
                        : AIGuideFailureInfo.OpenHint);
                return;
            }

            if (_lastAiGuideFailure != null)
            {
                ApplyAiGuideFailureUi(_lastAiGuideFailure);
                return;
            }

            UpdateAiGuideStatus(AIGuideFailureInfo.OpenHint, "#64748B", string.Empty);
            SetAiGuideInputEnabled(
                _aiGuideService.LastAnalysis?.Success == true,
                _aiGuideService.LastAnalysis?.Success == true
                    ? AIGuideLanguage.InputHintReady
                    : AIGuideFailureInfo.OpenHint);
        }

        private void FailAiGuideAnalysisUi(string statusBadge, string dotColor, string message, bool needsFocus = false)
        {
            var stage = needsFocus ? AIGuideFailureStage.BlankCapture : AIGuideFailureStage.Capture;
            var safeDetail = needsFocus
                ? RdpScreenCaptureProvider.BlankCaptureSafeDetail
                : message;
            ApplyAiGuideFailure(AIGuideFailureInfo.Create(
                stage,
                message,
                safeDetail,
                analysisId: 0,
                elapsedMs: _aiGuideAnalysisStopwatch?.ElapsedMilliseconds ?? 0), needsFocus);
        }

        private void ApplyAiGuideFailure(AIGuideFailureInfo info, bool needsFocus = false)
        {
            PreserveAiGuideFailureStage("ApplyAiGuideFailure", null, AIGuideFailureStage.None, info.Stage, info.AnalysisId);
            _lastAiGuideFailure = info;
            RunOnAiGuideUi(() => ApplyAiGuideFailureUi(info, needsFocus));
        }

        private void ApplyAiGuideFailureUi(AIGuideFailureInfo info, bool needsFocus = false)
        {
            AIGuideAnalysisLogger.AnalysisFailed(info.AnalysisId, info.Stage.ToString(), info.UserMessage);

            _aiGuideLastScreen = null;
            _aiGuideLastVisionResult = null;
            _aiGuideService?.ClearSession();
            ResetAiGuideSummaryUi();

            AiGuideFailurePanel.Visibility = Visibility.Visible;
            AiGuideFailureMainText.Text = info.UserMessage;
            AiGuideFailureStageText.Text = AIGuideLanguage.FormatStageLabel(info.Stage);
            if (info.AnalysisId > 0)
            {
                AiGuideFailureAnalysisIdText.Visibility = Visibility.Visible;
                AiGuideFailureAnalysisIdText.Text = AIGuideLanguage.FormatAnalysisId(info.AnalysisId);
            }
            else
            {
                AiGuideFailureAnalysisIdText.Visibility = Visibility.Collapsed;
                AiGuideFailureAnalysisIdText.Text = string.Empty;
            }
            AiGuideFailureDetailText.Text = AIGuideLanguage.FormatDetail(info.SafeDetail);
            AiGuideFailureRetryText.Text = AIGuideFailureInfo.RetryHint;

            var verbose = SettingsRuntime.Current.VerboseLogs;
            AiGuideFailureTechnicalText.Visibility = verbose ? Visibility.Visible : Visibility.Collapsed;
            AiGuideFailureTechnicalText.Text = verbose ? info.TechnicalMessage : string.Empty;

            if (verbose && (!string.IsNullOrWhiteSpace(info.ExceptionType)
                            || !string.IsNullOrWhiteSpace(info.ExceptionMessage)
                            || !string.IsNullOrWhiteSpace(info.OriginMethod)))
            {
                AiGuideFailureVerboseDiagnosticsPanel.Visibility = Visibility.Visible;
                AiGuideFailureExceptionTypeText.Text = string.IsNullOrWhiteSpace(info.ExceptionType)
                    ? string.Empty
                    : $"Exception Type: {info.ExceptionType}";
                AiGuideFailureExceptionMessageText.Text = string.IsNullOrWhiteSpace(info.ExceptionMessage)
                    ? string.Empty
                    : $"Exception Message: {info.ExceptionMessage}";
                AiGuideFailureOriginMethodText.Text = string.IsNullOrWhiteSpace(info.OriginMethod)
                    ? string.Empty
                    : $"Origin Method: {info.OriginMethod}";
            }
            else
            {
                AiGuideFailureVerboseDiagnosticsPanel.Visibility = Visibility.Collapsed;
                AiGuideFailureExceptionTypeText.Text = string.Empty;
                AiGuideFailureExceptionMessageText.Text = string.Empty;
                AiGuideFailureOriginMethodText.Text = string.Empty;
            }

            if (SettingsRuntime.Current.EnableAIGuideCaptureDiagnostics && _lastCaptureDiagnostics != null)
                UpdateAiGuideCaptureDiagnosticsPanel(_lastCaptureDiagnostics);

            if (needsFocus)
            {
                AiGuideMentorPanel.Visibility = Visibility.Visible;
                AiGuideMentorStatusBadge.Text = AIGuideLanguage.NeedsFocus;
                AiGuideMentorStatusBadge.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24));
                AiGuideMentorCurrentScreenText.Text = info.UserMessage;
            }

            UpdateAiGuideStatus(AIGuideLanguage.Failed, "#F87171", info.UserMessage);
            SetAiGuideInputEnabled(false, AIGuideFailureInfo.RetryHint);
        }

        private static void PreserveAiGuideFailureStage(
            string origin,
            Exception? ex,
            AIGuideFailureStage originalStage,
            AIGuideFailureStage currentStage,
            long analysisId)
        {
            if (originalStage == currentStage)
                return;

            AIGuideAnalysisLogger.StageTransition(
                origin,
                ex?.GetType().Name ?? "none",
                originalStage,
                currentStage,
                analysisId);
        }

        private static AIGuideFailureStage ResolveVisionFailureStage(VisionAnalysisResult result) =>
            result.FailureStage != AIGuideFailureStage.None && result.FailureStage != AIGuideFailureStage.Unknown
                ? result.FailureStage
                : AIGuideFailureStage.VisionRequest;

        private bool TryFinishInterruptedAnalysisRun(
            int runId,
            long analysisId,
            string? captureId,
            CancellationToken userCt,
            out bool completed)
        {
            completed = true;
            if (runId != _aiGuideCaptureRunId)
                return true;

            if (userCt.IsCancellationRequested)
            {
                RunOnAiGuideUi(() => RecoverAiGuideUiAfterCancelledAnalysis(cancelled: true, runId: runId));
                return true;
            }

            if (_aiGuideService == null || !_aiGuideService.IsAnalysisCurrent(analysisId, captureId))
                return true;

            completed = false;
            return false;
        }

        private void ClearAiGuideFailureUi()
        {
            _lastAiGuideFailure = null;
            AiGuideFailurePanel.Visibility = Visibility.Collapsed;
            AiGuideFailureMainText.Text = string.Empty;
            AiGuideFailureStageText.Text = string.Empty;
            AiGuideFailureAnalysisIdText.Text = string.Empty;
            AiGuideFailureAnalysisIdText.Visibility = Visibility.Collapsed;
            AiGuideFailureDetailText.Text = string.Empty;
            AiGuideFailureRetryText.Text = AIGuideFailureInfo.RetryHint;
            AiGuideFailureTechnicalText.Text = string.Empty;
            AiGuideFailureTechnicalText.Visibility = Visibility.Collapsed;
            AiGuideFailureVerboseDiagnosticsPanel.Visibility = Visibility.Collapsed;
            AiGuideFailureExceptionTypeText.Text = string.Empty;
            AiGuideFailureExceptionMessageText.Text = string.Empty;
            AiGuideFailureOriginMethodText.Text = string.Empty;
        }

        private void UpdateAiGuideCaptureDiagnosticsPanel(AIGuideCaptureDiagnostics? diagnostics)
        {
            _lastCaptureDiagnostics = diagnostics;
            if (diagnostics == null || !SettingsRuntime.Current.EnableAIGuideCaptureDiagnostics)
            {
                AiGuideCaptureDiagnosticsPanel.Visibility = Visibility.Collapsed;
                AiGuideCaptureDiagnosticsText.Text = string.Empty;
                return;
            }

            AiGuideCaptureDiagnosticsPanel.Visibility = Visibility.Visible;
            AiGuideCaptureDiagnosticsText.Text = diagnostics.ToDiagnosticText();
        }

        private void OnCopyCaptureDiagnosticsClick(object sender, RoutedEventArgs e)
        {
            if (_lastCaptureDiagnostics == null)
                return;

            try
            {
                System.Windows.Clipboard.SetText(_lastCaptureDiagnostics.ToDiagnosticText());
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Failed to copy capture diagnostics.", ex, category: "AIGuide", action: "CopyDiagnostics");
            }
        }

        private static AIGuideFailureInfo FailureFromScreenContext(
            ScreenContext screen,
            ScreenCaptureSource source,
            long analysisId,
            long elapsedMs)
        {
            var stage = screen.FailureStage != AIGuideFailureStage.None
                ? screen.FailureStage
                : AIGuideFailureStage.BlankCapture;
            PreserveAiGuideFailureStage(
                "FailureFromScreenContext",
                null,
                screen.FailureStage,
                stage,
                analysisId);
            var userMessage = stage == AIGuideFailureStage.BlankCapture
                ? RdpScreenCaptureProvider.BlankCaptureUserMessage
                : screen.CaptureWarning ?? "Screen capture failed.";
            var safeDetail = stage == AIGuideFailureStage.BlankCapture
                ? RdpScreenCaptureProvider.BlankCaptureSafeDetail
                : screen.CaptureWarning ?? userMessage;

            return AIGuideFailureInfo.Create(
                stage,
                userMessage,
                safeDetail,
                technicalMessage: screen.CaptureDiagnostics?.ToDiagnosticText() ?? safeDetail,
                analysisId: analysisId,
                elapsedMs: elapsedMs,
                captureSource: source,
                captureWidth: screen.Width,
                captureHeight: screen.Height,
                captureBytes: screen.ImageBytes?.Length ?? 0);
        }

        private static AIGuideFailureInfo FailureFromVisionResult(
            VisionAnalysisResult result,
            long analysisId,
            long elapsedMs)
        {
            var stage = ResolveVisionFailureStage(result);
            PreserveAiGuideFailureStage(
                "FailureFromVisionResult",
                null,
                result.FailureStage,
                stage,
                analysisId);
            return AIGuideFailureInfo.Create(
                stage,
                result.ErrorMessage ?? "Vision analysis failed.",
                result.SafeDetail ?? result.ErrorMessage ?? "Vision analysis failed.",
                analysisId: analysisId,
                elapsedMs: elapsedMs,
                captureSource: result.CaptureSource,
                captureBytes: result.ImageBytesLength,
                visionStatusCode: result.VisionStatusCode);
        }

        private static AIGuideFailureStage MapTimeoutStage(string message)
        {
            if (message.Contains("Vision", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.VisionTimeout;
            if (message.Contains("Screen capture", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.Capture;
            if (message.Contains("Smart summary", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.SmartSummary;
            if (message.Contains("Mentor", StringComparison.OrdinalIgnoreCase))
                return AIGuideFailureStage.MentorBuild;
            return AIGuideFailureStage.Unknown;
        }

        private async Task BeginAiGuideCaptureAsync()
        {
            if (!IsAiGuideEntitled)
                return;

            if (ShouldBlockAiGuideForRdp())
            {
                AIGuideAnalysisLogger.CaptureSkippedBecauseRdpNotStable(
                    _aiGuidePausedForRdpConnection ? "paused" : "not-stable");
                RunOnAiGuideUi(() =>
                {
                    if (_aiGuidePausedForRdpConnection || IsAnyRdpConnecting())
                        UpdateAiGuideStatus(AIGuideLanguage.AiGuidePausedForConnection, "#FBBF24", string.Empty);
                    else
                        UpdateAiGuideStatus(AIGuideLanguage.RdpLoadingMessage, "#FBBF24", string.Empty);
                    SetAiGuideInputEnabled(false, AIGuideLanguage.RdpLoadingMessage);
                });
                return;
            }

            var runId = Interlocked.Increment(ref _aiGuideCaptureRunId);
            AIGuideAnalysisLogger.CaptureRequested();
            CancelAiGuideGuidanceInFlight();
            try { _aiGuideCaptureCts?.Cancel(); } catch { /* ignore */ }
            try { _aiGuideCaptureCts?.Dispose(); } catch { /* ignore */ }

            var connectionCt = _activeSession?.ConnectionCts?.Token ?? CancellationToken.None;
            _aiGuideCaptureCts = connectionCt.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(connectionCt)
                : new CancellationTokenSource();
            var userCt = _aiGuideCaptureCts.Token;
            var aiGuide = GetAiGuideService();
            aiGuide.CancelInFlightAnalysis();
            aiGuide.PrepareFreshCaptureSession();

            long analysisId = 0;
            string? captureId = null;
            var completed = false;
            _aiGuideAnalysisStopwatch = Stopwatch.StartNew();
            _aiGuideLastScreen = null;
            _aiGuideLastVisionResult = null;
            LogAiGuidePipelineCheckpoint(0, runId, "BeginAiGuideCaptureAsync.Start");

            RunOnAiGuideUi(() =>
            {
                ResetAiGuideSummaryUi();
                SetAiGuideInputEnabled(false, AIGuideLanguage.InputHintAnalysisRequired);
                UpdateAiGuideStatus(AIGuideLanguage.Capturing, "#38BDF8", string.Empty);
            });

            try
            {
                await Task.Delay(50, userCt).ConfigureAwait(false);

                if (userCt.IsCancellationRequested || runId != _aiGuideCaptureRunId || ShouldBlockAiGuideForRdp())
                {
                    AIGuideAnalysisLogger.CaptureSkippedBecauseRdpNotStable("pre-capture-gate");
                    return;
                }

                LogAiGuidePipelineCheckpoint(0, runId, "ResolveAiGuideCaptureTarget");
                var snapshot = await RunOnAiGuideUiAsync(ResolveAiGuideCaptureTargetSnapshot).ConfigureAwait(false);
                if (!snapshot.HasTarget)
                {
                    var failure = AIGuideFailureInfo.Create(
                        AIGuideFailureStage.CaptureTargetResolve,
                        AIGuideLanguage.NoVisibleCaptureTarget,
                        AIGuideLanguage.T(
                            "Switch to a server dashboard or connect an RDP session.",
                            "Bir sunucu panosuna geçin veya bir RDP oturumuna bağlanın."),
                        elapsedMs: _aiGuideAnalysisStopwatch.ElapsedMilliseconds);
                    ApplyAiGuideFailure(failure);
                    completed = true;
                    return;
                }

                AIGuideAnalysisLogger.CaptureTargetResolved(0, snapshot.Source.ToString(), snapshot.TargetName);

                if (snapshot.Source == ScreenCaptureSource.RdpSession)
                {
                    if (snapshot.IsConnecting)
                    {
                        AIGuideAnalysisLogger.CaptureBlockedByRdpState(snapshot.RdpState, "connecting");
                        var failure = AIGuideFailureInfo.Create(
                            AIGuideFailureStage.Capture,
                            AIGuideLanguage.RdpConnectingMessage,
                            "Wait for the RDP session to finish connecting.",
                            elapsedMs: _aiGuideAnalysisStopwatch.ElapsedMilliseconds,
                            captureSource: snapshot.Source);
                        ApplyAiGuideFailure(failure, needsFocus: true);
                        completed = true;
                        return;
                    }

                    if (!snapshot.IsReady)
                    {
                        var warming = IsRdpWarmingUp();
                        AIGuideAnalysisLogger.CaptureBlockedByRdpState(snapshot.RdpState, warming ? "warming-up" : "not-ready");
                        if (warming)
                            AIGuideAnalysisLogger.CaptureAttemptSkippedBecauseRdpNotReady("warming-up");
                        var failure = AIGuideFailureInfo.Create(
                            AIGuideFailureStage.Capture,
                            warming ? AIGuideLanguage.RdpLoadingMessage : AIGuideLanguage.RdpNotReadyMessage,
                            warming
                                ? "Wait for the RDP screen to finish loading."
                                : "Connect to the remote session and bring it into view.",
                            elapsedMs: _aiGuideAnalysisStopwatch.ElapsedMilliseconds,
                            captureSource: snapshot.Source);
                        ApplyAiGuideFailure(failure, needsFocus: true);
                        completed = true;
                        return;
                    }
                }

                var server = snapshot.ServerName != null
                    ? (_activeSession?.Server ?? _selectedServer ?? _selectedNode?.Server)
                    : null;
                var serverKey = snapshot.ServerKey;
                var source = snapshot.Source;
                RunOnAiGuideUi(() => UpdateAiGuideStatus(
                    AIGuideLanguage.AnalyzingScreen,
                    "#FBBF24",
                    BuildAiGuideCaptureMeta(new ScreenContext
                    {
                        CaptureId = "pending",
                        Source = source,
                        ServerName = server?.Name
                    })));

                var settings = SettingsRuntime.Current;
                var captureOptions = await RunOnAiGuideUiAsync(() =>
                {
                    FrameworkElement fallback = RdpContentGrid != null ? RdpContentGrid : RdpColumnBorder;
                    return CaptureTargetResolver.ToCaptureOptions(snapshot, fallback);
                }).ConfigureAwait(false);

                LogAiGuidePipelineCheckpoint(0, runId, "CaptureForAnalysisAsync");
                ScreenContext screen;
                (screen, analysisId) = await AIGuideStageTimeouts.RunAsync(
                    ct => aiGuide.CaptureForAnalysisAsync(captureOptions, ct),
                    AIGuideStageTimeouts.Capture,
                    userCt,
                    "Screen capture").ConfigureAwait(false);

                _aiGuideLastScreen = screen;

                AIGuideAnalysisLogger.AnalysisStarted(analysisId);
                captureId = screen.CaptureId;
                if (TryFinishInterruptedAnalysisRun(runId, analysisId, captureId, userCt, out completed))
                    return;

                RunOnAiGuideUi(() => UpdateAiGuideCaptureDiagnosticsPanel(screen.CaptureDiagnostics));

                if (IsRdpCaptureContentUnavailable(source, screen))
                {
                    var failure = FailureFromScreenContext(
                        screen,
                        source,
                        analysisId,
                        _aiGuideAnalysisStopwatch.ElapsedMilliseconds);
                    ApplyAiGuideFailure(failure, needsFocus: true);
                    completed = true;
                    return;
                }

                AIGuideAnalysisLogger.VisionRequestStarted(analysisId);
                var visionTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.VisionTimeoutSeconds, 5, 120));
                LogAiGuidePipelineCheckpoint(analysisId, runId, "AnalyzeCapturedScreenAsync");
                var result = await AIGuideStageTimeouts.RunAsync(
                    ct => aiGuide.AnalyzeCapturedScreenAsync(settings, analysisId, ct),
                    visionTimeout,
                    userCt,
                    "Vision analysis").ConfigureAwait(false);

                _aiGuideLastVisionResult = result;

                AIGuideAnalysisLogger.VisionRequestCompleted(analysisId, _aiGuideAnalysisStopwatch.ElapsedMilliseconds);

                if (TryFinishInterruptedAnalysisRun(runId, analysisId, captureId, userCt, out completed))
                    return;

                if (ShouldAbortAfterVision(source, result))
                {
                    var visionFailure = result.Success
                        ? VisionAnalysisResult.Failed(
                            result.RequestId,
                            result.CaptureSource,
                            RdpScreenCaptureProvider.BlankCaptureUserMessage,
                            result.ImageBytesLength,
                            AIGuideFailureStage.BlankCapture,
                            safeDetail: RdpScreenCaptureProvider.BlankCaptureSafeDetail)
                        : result;
                    var failure = FailureFromVisionResult(
                        visionFailure,
                        analysisId,
                        _aiGuideAnalysisStopwatch.ElapsedMilliseconds);
                    ApplyAiGuideFailure(failure, needsFocus: true);
                    completed = true;
                    return;
                }

                if (!result.Success || result.Observation == null)
                {
                    var failure = FailureFromVisionResult(
                        result,
                        analysisId,
                        _aiGuideAnalysisStopwatch.ElapsedMilliseconds);
                    ApplyAiGuideFailure(failure);
                    completed = true;
                    return;
                }

                LogAiGuidePipelineCheckpoint(analysisId, runId, "BuildAiGuideRuntimeContext");
                var runtimeContext = BuildAiGuideRuntimeContext(server, result);
                aiGuide.SetRuntimeContext(runtimeContext);
                RunOnAiGuideUi(() =>
                {
                    ClearAiGuideFailureUi();
                    ApplyAiGuideAnalysisResult(result, runtimeContext);
                });

                if (TryFinishInterruptedAnalysisRun(runId, analysisId, captureId, userCt, out completed))
                    return;

                RunOnAiGuideUi(() => UpdateAiGuideStatus(AIGuideLanguage.SummarizingScreen, "#FBBF24", string.Empty));
                AIGuideAnalysisLogger.SummaryStarted(analysisId);

                LogAiGuidePipelineCheckpoint(analysisId, runId, "GenerateSmartSummaryAsync");
                SmartScreenSummary summary;
                try
                {
                    summary = await AIGuideStageTimeouts.RunAsync(
                        ct => aiGuide.GenerateSmartSummaryAsync(settings, analysisId, ct),
                        AIGuideStageTimeouts.SmartSummary,
                        userCt,
                        "Smart summary").ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    AIGuideAnalysisLogger.SummaryFailed(analysisId, "Timeout");
                    summary = SmartSummaryFallbackBuilder.Build(
                        Guid.NewGuid().ToString("N"),
                        result.Observation!,
                        runtimeContext);
                }

                if (TryFinishInterruptedAnalysisRun(runId, analysisId, captureId, userCt, out completed))
                    return;

                LogAiGuidePipelineCheckpoint(analysisId, runId, "DetectWorkflow");
                aiGuide.DetectWorkflow();
                VisualMentorDashboard dashboard;
                try
                {
                    LogAiGuidePipelineCheckpoint(analysisId, runId, "BuildVisualMentorDashboard");
                    dashboard = await AIGuideStageTimeouts.RunAsync(
                        _ => Task.FromResult(aiGuide.BuildVisualMentorDashboard(summaryGenerated: summary.Success)),
                        AIGuideStageTimeouts.MentorDashboardBuild,
                        userCt,
                        "Mentor dashboard").ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    dashboard = aiGuide.BuildVisualMentorDashboard(summaryGenerated: summary.Success);
                }

                if (TryFinishInterruptedAnalysisRun(runId, analysisId, captureId, userCt, out completed))
                    return;

                AIGuideAnalysisLogger.MentorBuildCompleted(analysisId);
                LogAiGuidePipelineCheckpoint(analysisId, runId, "ApplyVisualMentorDashboard");
                var readyDashboard = dashboard;
                RunOnAiGuideUi(() =>
                {
                    ClearAiGuideFailureUi();
                    ApplyVisualMentorDashboard(readyDashboard);
                    UpdateAiGuideStatus(
                        readyDashboard.IsReady ? AIGuideLanguage.Ready : AIGuideLanguage.NeedsFocus,
                        readyDashboard.IsReady ? "#22C55E" : "#FBBF24",
                        string.Empty);
                    SetAiGuideInputEnabled(true, AIGuideLanguage.InputHintReady);
                });

                completed = true;
            }
            catch (OperationCanceledException)
            {
                if (runId == _aiGuideCaptureRunId && userCt.IsCancellationRequested)
                    RunOnAiGuideUi(() => RecoverAiGuideUiAfterCancelledAnalysis(cancelled: true, runId: runId));
                completed = true;
            }
            catch (TimeoutException ex)
            {
                AppLogger.Warning(ex.Message, category: "AIGuide", action: "StageTimeout");
                var stage = MapTimeoutStage(ex.Message);
                PreserveAiGuideFailureStage("TimeoutException", ex, AIGuideFailureStage.None, stage, analysisId);
                var failure = AIGuideFailureInfo.Create(
                    stage,
                    ex.Message,
                    ex.Message,
                    analysisId: analysisId,
                    elapsedMs: _aiGuideAnalysisStopwatch?.ElapsedMilliseconds ?? 0);
                ApplyAiGuideFailure(failure);
                completed = true;
            }
            catch (Exception ex)
            {
                var root = AIGuideExceptionMapper.Unwrap(ex);
                AppLogger.Error(
                    $"AI Guide pipeline failed at {_aiGuidePipelineCheckpoint}: {root.GetType().Name}: {root.Message}",
                    root,
                    category: "AIGuide",
                    action: "CaptureWorkflow");

                if (SettingsRuntime.Current.VerboseLogs && !string.IsNullOrWhiteSpace(root.StackTrace))
                {
                    AppLogger.Debug(
                        root.StackTrace,
                        category: "AIGuide",
                        action: "CaptureWorkflowStack");
                }

                var failure = AIGuideExceptionMapper.Map(
                    ex,
                    _aiGuidePipelineCheckpoint,
                    analysisId,
                    _aiGuideAnalysisStopwatch?.ElapsedMilliseconds ?? 0,
                    _aiGuideLastScreen,
                    _aiGuideLastVisionResult);

                PreserveAiGuideFailureStage(
                    "Exception",
                    root,
                    AIGuideFailureStage.None,
                    failure.Stage,
                    analysisId);

                ApplyAiGuideFailure(failure);
                completed = true;
            }
            finally
            {
                if (!completed && _isAiPanelOpen && runId == _aiGuideCaptureRunId)
                    RunOnAiGuideUi(() => RecoverAiGuideUiAfterCancelledAnalysis(
                        cancelled: userCt.IsCancellationRequested,
                        runId: runId));
            }
        }

        private RuntimeContext BuildAiGuideRuntimeContext(ServerInfo? server, VisionAnalysisResult result)
        {
            WmiSnapshot? snap = null;
            if (server != null)
            {
                snap = GetLatestCachedSnapshot(server);
                if (snap == null && _lastSnapshot != null
                    && string.Equals(_lastSnapshot.Ip, server.Ip, StringComparison.OrdinalIgnoreCase))
                {
                    snap = _lastSnapshot;
                }
            }

            return _contextAwarenessEngine.Build(new AIGuideContextBuildInput
            {
                Server = server,
                Snapshot = snap,
                Observation = result.Observation,
                CaptureSource = result.CaptureSource,
                RdpSessionActive = HasAnyActiveSession(),
                AnalysisTimeUtc = result.AnalyzedAtUtc
            });
        }

        private void ApplyAiGuideAnalysisResult(VisionAnalysisResult result, RuntimeContext? runtimeContext = null)
        {
            runtimeContext ??= _aiGuideService?.LastRuntimeContext;

            if (result.Success && result.Observation != null)
            {
                ClearAiGuideFailureUi();
                var obs = result.Observation;
                UpdateAiGuideStatus("Ready", "#22C55E", string.Empty);
                AiGuideAwarenessPanel.Visibility = Visibility.Collapsed;
                AiGuideSummaryPanel.Visibility = SettingsRuntime.Current.VerboseLogs
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                AiGuideApplicationText.Text = $"Application: {(string.IsNullOrWhiteSpace(obs.Application) ? "—" : obs.Application)}";
                AiGuideWindowTitleText.Text = $"Window: {(string.IsNullOrWhiteSpace(obs.WindowTitle) ? "—" : obs.WindowTitle)}";
                AiGuideConfidenceText.Text = $"Confidence: {obs.Confidence:P0}";
                AiGuideControlsCountText.Text = $"Visible controls: {obs.VisibleControls?.Count ?? 0}";
                AiGuideErrorsText.Text = $"Errors: {obs.VisibleErrors?.Count ?? 0}";
                AiGuideWarningsText.Text = $"Warnings: {obs.VisibleWarnings?.Count ?? 0}";
                AiGuideSourceText.Text = $"Capture source: {result.CaptureSource}";
                AiGuideAnalyzedAtText.Text = $"Last analyzed: {result.AnalyzedAtUtc.ToLocalTime():g}";

                var limitations = obs.Limitations.Count > 0
                    ? string.Join(" · ", obs.Limitations)
                    : "None reported";
                AiGuideLimitationsText.Text = $"Limitations: {limitations}";

                AiGuideRawJsonBox.Text = result.RawJson ?? string.Empty;
                AiGuideShowRawJsonCheck.Visibility = SettingsRuntime.Current.VerboseLogs
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                var failure = FailureFromVisionResult(
                    result,
                    0,
                    _aiGuideAnalysisStopwatch?.ElapsedMilliseconds ?? 0);
                ApplyAiGuideFailure(failure);
                AiGuideSummaryPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyVisualMentorDashboard(VisualMentorDashboard dashboard)
        {
            AiGuideMentorPanel.Visibility = Visibility.Visible;

            AiGuideMentorStatusBadge.Text = dashboard.StatusBadge;
            AiGuideMentorStatusBadge.Foreground = dashboard.IsReady
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24));

            AiGuideMentorServerText.Text = AIGuideLanguage.FormatServerLabel(dashboard.ServerName);
            AiGuideMentorRoleText.Text = AIGuideLanguage.FormatRoleLabel(dashboard.Role);
            AiGuideMentorApplicationText.Text = AIGuideLanguage.FormatApplicationLabel(dashboard.Application);
            AiGuideMentorWorkflowText.Text = AIGuideLanguage.FormatWorkflowLabel(dashboard.WorkflowName);
            AiGuideMentorConfidenceText.Text = AIGuideLanguage.FormatConfidenceDetail(
                dashboard.ConfidenceLabel,
                dashboard.ConfidenceScore);

            var screen = dashboard.CurrentScreen;
            var screenText = !string.IsNullOrWhiteSpace(screen.Narrative)
                ? screen.Narrative
                : string.Join("\n", new[]
                {
                    string.IsNullOrWhiteSpace(screen.WindowTitle)
                        ? null
                        : AIGuideLocalizedCatalogs.CurrentWindow(screen.WindowTitle),
                    screen.SelectedObjects.Count > 0
                        ? AIGuideLanguage.T(
                            $"Selected: {string.Join(", ", screen.SelectedObjects)}",
                            $"Seçili: {string.Join(", ", screen.SelectedObjects)}")
                        : null,
                    screen.Dialogs.Count > 0
                        ? AIGuideLocalizedCatalogs.Dialogs(string.Join(", ", screen.Dialogs))
                        : null,
                    screen.VisibleErrors.Count > 0
                        ? AIGuideLanguage.T(
                            $"Errors visible: {string.Join("; ", screen.VisibleErrors)}",
                            $"Görünen hatalar: {string.Join("; ", screen.VisibleErrors)}")
                        : null,
                    screen.VisibleWarnings.Count > 0
                        ? AIGuideLanguage.T(
                            $"Warnings visible: {string.Join("; ", screen.VisibleWarnings)}",
                            $"Görünen uyarılar: {string.Join("; ", screen.VisibleWarnings)}")
                        : null
                }.Where(s => s != null));
            AiGuideMentorCurrentScreenText.Text = string.IsNullOrWhiteSpace(screenText)
                ? AIGuideLanguage.ScreenAnalyzedFallback
                : screenText;

            AiGuideMentorObservationsList.ItemsSource = dashboard.Observations.Items;

            var workflow = dashboard.Workflow;
            if (workflow == null || workflow.Status is WorkflowStatus.None or WorkflowStatus.Unknown)
            {
                AiGuideMentorWorkflowSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                AiGuideMentorWorkflowSection.Visibility = Visibility.Visible;
                var workflowName = AIGuideLanguage.LocalizeWorkflowName(
                    workflow.Workflow?.Id,
                    workflow.Workflow?.Name ?? AIGuideLanguage.T("Unknown workflow", "Bilinmeyen iş akışı"));
                var stepId = workflow.Workflow?.Steps.ElementAtOrDefault(workflow.Progress.CurrentStepIndex - 1)?.Id;
                var stepName = AIGuideLanguage.LocalizeWorkflowStepName(
                    workflow.Workflow?.Id,
                    stepId,
                    workflow.Progress.CurrentStepName);
                AiGuideMentorWorkflowNameText.Text = workflowName;
                AiGuideMentorWorkflowStepText.Text = AIGuideLanguage.FormatCurrentStep(
                    AIGuideLanguage.FormatWorkflowProgress(workflow.Progress.CurrentStepIndex, workflow.Progress.TotalSteps),
                    stepName);
                AiGuideMentorWorkflowProgressBar.Value = workflow.Progress.ProgressPercent;
                AiGuideMentorWorkflowProgressText.Text = AIGuideLanguage.FormatPercentComplete(workflow.Progress.ProgressPercent);
                AiGuideMentorWorkflowConfidenceText.Text = AIGuideLanguage.FormatWorkflowConfidence(
                    AIGuideLanguage.LocalizeConfidenceLabel(workflow.ConfidenceLabel),
                    workflow.Confidence);

                if (dashboard.ScreenChange.ApplicationChanged)
                {
                    AiGuideMentorWorkflowScreenChangeText.Visibility = Visibility.Visible;
                    var prev = dashboard.ScreenChange.PreviousApplication ?? workflow.PreviousApplication ?? "—";
                    var curr = dashboard.ScreenChange.CurrentApplication ?? workflow.CurrentApplication ?? "—";
                    AiGuideMentorWorkflowScreenChangeText.Text = AIGuideLanguage.FormatApplicationChanged(
                        prev,
                        curr,
                        AIGuideLanguage.FormatContinuedYesNo(dashboard.ScreenChange.WorkflowContinued == true));
                }
                else
                {
                    AiGuideMentorWorkflowScreenChangeText.Visibility = Visibility.Collapsed;
                }

                AiGuideMentorWorkflowTimelineText.Text = dashboard.WorkflowTimelineSteps.Count > 0
                    ? string.Join("\n↓\n", dashboard.WorkflowTimelineSteps)
                    : string.Empty;
            }

            var timelineEntries = dashboard.Timeline.Entries.TakeLast(8).ToList();
            AiGuideMentorTimelineList.ItemsSource = timelineEntries;
            AiGuideMentorTimelineExpander.Visibility = timelineEntries.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            AiGuideMentorQuestionsList.ItemsSource = dashboard.SuggestedQuestions;
            AiGuideMentorLearningTopicsList.ItemsSource = dashboard.LearningTopics.AllTopics;

            if (dashboard.Warnings.Count > 0)
            {
                AiGuideMentorWarningsSection.Visibility = Visibility.Visible;
                AiGuideMentorWarningsList.ItemsSource = dashboard.Warnings;
            }
            else
            {
                AiGuideMentorWarningsSection.Visibility = Visibility.Collapsed;
                AiGuideMentorWarningsList.ItemsSource = null;
            }

            AiGuideMentorEvidenceList.ItemsSource = dashboard.Evidence;
            AiGuideMentorEvidenceExpander.Visibility = dashboard.Evidence.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            AiGuideMentorReminderText.Text = dashboard.Reminder;
        }

        private void OnAiGuideLearningTopicClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
                return;

            var teachPrompt = button.Tag as string;
            if (string.IsNullOrWhiteSpace(teachPrompt) || AiGuideInputBox == null)
                return;

            AiGuideInputBox.Text = teachPrompt;
            AiGuideInputBox.Focus();
            AiGuideInputBox.CaretIndex = AiGuideInputBox.Text.Length;
        }

        private void OnAiGuideSuggestedTopicClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
                return;

            var question = button.Tag as string;
            if (string.IsNullOrWhiteSpace(question) || AiGuideInputBox == null)
                return;

            AiGuideInputBox.Text = question;
            AiGuideInputBox.Focus();
            AiGuideInputBox.CaretIndex = AiGuideInputBox.Text.Length;
        }

        private void ApplyAiGuideAwarenessPanel(RuntimeContext? context)
        {
            if (context == null)
            {
                AiGuideAwarenessPanel.Visibility = Visibility.Collapsed;
                return;
            }

            AiGuideAwarenessPanel.Visibility = Visibility.Visible;

            AiGuideAwarenessServerText.Text = $"Current server: {FormatContextValue(context.Server.ServerName)}";
            AiGuideAwarenessRoleText.Text = $"Detected role: {context.DetectedRole.DisplayName}";
            AiGuideAwarenessApplicationText.Text =
                $"Detected application: {FormatContextValue(context.Application.DetectedApplication ?? context.Application.WindowTitle)}";
            AiGuideAwarenessConnectionText.Text =
                $"Connection: {context.Server.ConnectionType} · {context.Server.ConnectionStatus}";
            AiGuideAwarenessAnalysisTimeText.Text =
                $"Analysis time: {context.AnalysisTimeUtc.ToLocalTime():g}";
            AiGuideAwarenessVisionConfidenceText.Text =
                $"Vision confidence: {context.Confidence.VisionLabel} ({context.Confidence.VisionConfidence:P0})";
            AiGuideAwarenessOverallConfidenceText.Text =
                $"Overall confidence: {context.Confidence.OverallLabel} ({context.Confidence.OverallGuidanceConfidence:P0})";
        }

        private static string FormatContextValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        private void OnAiGuideInputPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter || AiGuideInputBox?.IsEnabled != true)
                return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                return;

            e.Handled = true;
            _ = SendAiGuideQuestionAsync();
        }

        private void OnAiGuideSendClick(object sender, RoutedEventArgs e) =>
            _ = SendAiGuideQuestionAsync();

        private async Task SendAiGuideQuestionAsync()
        {
            if (!IsAiGuideEntitled)
            {
                ShowPremiumPlusRequiredPrompt();
                return;
            }

            var aiGuide = GetAiGuideService();
            if (aiGuide.State is AIGuideSessionState.Capturing
                or AIGuideSessionState.Analyzing
                or AIGuideSessionState.Guiding)
            {
                return;
            }

            if (ShouldBlockAiGuideForRdp())
            {
                AIGuideAnalysisLogger.CaptureSkippedBecauseRdpNotStable("guidance-blocked");
                if (_aiGuidePausedForRdpConnection || IsAnyRdpConnecting())
                    UpdateAiGuideStatus(AIGuideLanguage.AiGuidePausedForConnection, "#FBBF24", string.Empty);
                else
                    ShowToast(AIGuideLanguage.RdpLoadingMessage, "\uE946", 4000);
                return;
            }

            var question = AiGuideInputBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(question))
                return;

            CancelAiGuideGuidanceInFlight();
            _aiGuideGuidanceCts = new CancellationTokenSource();
            var ct = _aiGuideGuidanceCts.Token;

            AppendAiGuideUserMessage(question);
            if (AiGuideInputBox != null)
                AiGuideInputBox.Text = string.Empty;

            SetAiGuideInputEnabled(false, AIGuideLanguage.CapturingForQuestion);
            UpdateAiGuideStatus(AIGuideLanguage.CapturingForQuestion, "#38BDF8", string.Empty);

            try
            {
                await WaitForLatestAiGuideFrameAsync(ct).ConfigureAwait(false);

                var snapshot = await RunOnAiGuideUiAsync(ResolveAiGuideCaptureTargetSnapshot).ConfigureAwait(false);
                if (!snapshot.HasTarget)
                {
                    RunOnAiGuideUi(() =>
                    {
                        AppendAiGuideAssistantMessage(GuidanceResponse.Failed(
                            Guid.NewGuid().ToString("N"),
                            AIGuideLanguage.FreshCaptureFailedNoFallback));
                        UpdateAiGuideStatus(AIGuideLanguage.Ready, "#22C55E", string.Empty);
                        SetAiGuideInputEnabled(true, AIGuideLanguage.InputHintReady);
                    });
                    return;
                }

                var server = snapshot.ServerName != null
                    ? (_activeSession?.Server ?? _selectedServer ?? _selectedNode?.Server)
                    : null;
                var captureOptions = await RunOnAiGuideUiAsync(() =>
                {
                    FrameworkElement fallback = RdpContentGrid != null ? RdpContentGrid : RdpColumnBorder;
                    return CaptureTargetResolver.ToCaptureOptions(snapshot, fallback);
                }).ConfigureAwait(false);

                var response = await aiGuide.AskGuidanceWithFreshCaptureAsync(
                    question,
                    captureOptions,
                    SettingsRuntime.Current,
                    ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                    return;

                RunOnAiGuideUi(() =>
                {
                    if (response.Success)
                    {
                        AppendAiGuideAssistantMessage(response);
                        UpdateAiGuideStatus(AIGuideLanguage.Ready, "#22C55E", string.Empty);
                        SetAiGuideInputEnabled(true, AIGuideLanguage.InputHintReady);

                        var analysis = aiGuide.LastAnalysis;
                        if (analysis?.Success == true && analysis.Observation != null)
                        {
                            var runtimeContext = BuildAiGuideRuntimeContext(server, analysis);
                            aiGuide.SetRuntimeContext(runtimeContext);
                            ApplyAiGuideAnalysisResult(analysis, runtimeContext);
                            aiGuide.DetectWorkflow();
                            var dashboard = aiGuide.BuildVisualMentorDashboard(
                                summaryGenerated: aiGuide.LastSmartSummary?.Success == true);
                            ApplyVisualMentorDashboard(dashboard);
                        }
                    }
                    else
                    {
                        AppendAiGuideAssistantMessage(response);
                        UpdateAiGuideStatus(AIGuideLanguage.Ready, "#22C55E", string.Empty);
                        SetAiGuideInputEnabled(true, response.ErrorMessage ?? AIGuideLanguage.GuidanceFailed);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Panel closed or server switched.
            }
            catch (Exception ex)
            {
                AppLogger.Error("AI Guide guidance failed.", ex, category: "AIGuide", action: "GuidanceWorkflow");
                RunOnAiGuideUi(() =>
                {
                    AppendAiGuideAssistantMessage(GuidanceResponse.Failed(
                        Guid.NewGuid().ToString("N"),
                        AIGuideLanguage.FreshCaptureFailedNoFallback));
                    UpdateAiGuideStatus(AIGuideLanguage.Ready, "#22C55E", string.Empty);
                    SetAiGuideInputEnabled(true, AIGuideLanguage.InputHintReady);
                });
            }
            finally
            {
                if (ct.IsCancellationRequested && _isAiPanelOpen)
                {
                    RunOnAiGuideUi(() =>
                    {
                        UpdateAiGuideStatus(AIGuideLanguage.Ready, "#22C55E", string.Empty);
                        SetAiGuideInputEnabled(
                            aiGuide.State == AIGuideSessionState.Ready,
                            AIGuideLanguage.InputHintReady);
                    });
                }
            }
        }

        private void ResetAiGuideSummaryUi()
        {
            ClearAiGuideFailureUi();
            UpdateAiGuideCaptureDiagnosticsPanel(null);
            AiGuideMentorPanel.Visibility = Visibility.Collapsed;
            AiGuideAwarenessPanel.Visibility = Visibility.Collapsed;
            AiGuideSummaryPanel.Visibility = Visibility.Collapsed;
            AiGuideMentorStatusBadge.Text = "Screen Ready";
            AiGuideMentorServerText.Text = string.Empty;
            AiGuideMentorRoleText.Text = string.Empty;
            AiGuideMentorApplicationText.Text = string.Empty;
            AiGuideMentorWorkflowText.Text = string.Empty;
            AiGuideMentorConfidenceText.Text = string.Empty;
            AiGuideMentorCurrentScreenText.Text = string.Empty;
            AiGuideMentorObservationsList.ItemsSource = null;
            AiGuideMentorWorkflowSection.Visibility = Visibility.Collapsed;
            AiGuideMentorWorkflowNameText.Text = string.Empty;
            AiGuideMentorWorkflowStepText.Text = string.Empty;
            AiGuideMentorWorkflowProgressBar.Value = 0;
            AiGuideMentorWorkflowProgressText.Text = string.Empty;
            AiGuideMentorWorkflowConfidenceText.Text = string.Empty;
            AiGuideMentorWorkflowScreenChangeText.Text = string.Empty;
            AiGuideMentorWorkflowScreenChangeText.Visibility = Visibility.Collapsed;
            AiGuideMentorWorkflowTimelineText.Text = string.Empty;
            AiGuideMentorTimelineList.ItemsSource = null;
            AiGuideMentorTimelineExpander.Visibility = Visibility.Collapsed;
            AiGuideMentorQuestionsList.ItemsSource = null;
            AiGuideMentorLearningTopicsList.ItemsSource = null;
            AiGuideMentorWarningsSection.Visibility = Visibility.Collapsed;
            AiGuideMentorWarningsList.ItemsSource = null;
            AiGuideMentorEvidenceList.ItemsSource = null;
            AiGuideMentorEvidenceExpander.Visibility = Visibility.Collapsed;
            AiGuideMentorReminderText.Text = string.Empty;
            AiGuideAwarenessRoleText.Text = string.Empty;
            AiGuideAwarenessApplicationText.Text = string.Empty;
            AiGuideAwarenessConnectionText.Text = string.Empty;
            AiGuideAwarenessAnalysisTimeText.Text = string.Empty;
            AiGuideAwarenessVisionConfidenceText.Text = string.Empty;
            AiGuideAwarenessOverallConfidenceText.Text = string.Empty;
            AiGuideApplicationText.Text = string.Empty;
            AiGuideWindowTitleText.Text = string.Empty;
            AiGuideConfidenceText.Text = string.Empty;
            AiGuideControlsCountText.Text = string.Empty;
            AiGuideErrorsText.Text = string.Empty;
            AiGuideWarningsText.Text = string.Empty;
            AiGuideSourceText.Text = string.Empty;
            AiGuideAnalyzedAtText.Text = string.Empty;
            AiGuideLimitationsText.Text = string.Empty;
            AiGuideRawJsonBox.Text = string.Empty;
            AiGuideRawJsonBox.Visibility = Visibility.Collapsed;
            AiGuideShowRawJsonCheck.IsChecked = false;
            AiGuideShowRawJsonCheck.Visibility = Visibility.Collapsed;
            SetAiGuideInputEnabled(false, "Complete screen analysis before asking a question.");
            if (AiGuideInputBox != null)
                AiGuideInputBox.Text = string.Empty;
        }

        private void OnAiGuideRawJsonToggle(object sender, RoutedEventArgs e)
        {
            AiGuideRawJsonBox.Visibility = AiGuideShowRawJsonCheck.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static string BuildAiGuideCaptureMeta(ScreenContext screen)
        {
            var parts = new List<string>
            {
                $"Source: {screen.Source}",
                $"{screen.Width}x{screen.Height}",
                screen.HasImage ? $"{screen.ImageBytes!.Length / 1024} KB in memory" : "No image bytes"
            };
            if (!string.IsNullOrWhiteSpace(screen.ServerName))
                parts.Insert(0, screen.ServerName);
            if (!string.IsNullOrWhiteSpace(screen.CaptureWarning))
                parts.Add(screen.CaptureWarning);
            return string.Join(" · ", parts);
        }

        private void UpdateAiGuideStatus(string statusText, string dotColorHex, string metaText)
        {
            try
            {
                if (AiGuideStatusText != null)
                    AiGuideStatusText.Text = statusText;
                if (AiGuideCaptureMetaText != null)
                    AiGuideCaptureMetaText.Text = metaText ?? string.Empty;
                if (AiGuideStatusDot != null
                    && System.Windows.Media.ColorConverter.ConvertFromString(dotColorHex) is System.Windows.Media.Color color)
                {
                    AiGuideStatusDot.Fill = new SolidColorBrush(color);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"AI Guide status update failed: {ex.Message}", category: "AIGuide", action: "Status");
            }
        }

        private readonly RdpViewportFullscreenViewModel _rdpViewportVm = new();
        private readonly RdpChromeResizeViewModel _chromeResizeVm = new();
        private DispatcherTimer? _floatingToolbarHideTimer;
        private DispatcherTimer? _chromeAnimateTimer;
        private bool _isResizingRdpChrome;
        private double _resizeDragStartY;
        private double _resizeDragStartTopHeight;
        private DateTime _lastResizeFrameUtc;
        private const int RdpViewportTransitionMs = 250;

        private void OnToggleRdpViewportFullscreenClick(object sender, RoutedEventArgs e) => ToggleRdpViewportFullscreen();

        private void OnExitRdpViewportFullscreenClick(object sender, RoutedEventArgs e)
        {
            if (_rdpViewportVm.IsRdpFocusMode)
                ExitRdpFocusMode();
        }

        private void ToggleRdpViewportFullscreen()
        {
            if (_rdpViewportVm.IsRdpFocusMode)
                ExitRdpFocusMode();
            else
                EnterRdpFocusMode();
        }

        private bool CanEnterRdpFocusMode()
        {
            if (HasAnyActiveSession()) return true;
            if (_activeSession?.CleanSession?.IsConnected == true) return true;
            return GetActiveTabView()?.Visibility == Visibility.Visible && TabBarBorder.Visibility == Visibility.Visible;
        }

        private void EnterRdpFocusMode()
        {
            if (!CanEnterRdpFocusMode())
            {
                ShowToast("Önce bir RDP oturumuna bağlanın.", "⚠️");
                return;
            }

            EnsureChromeDefaultsMeasured();

            _rdpViewportVm.Capture(
                RdpWorkspaceGrid,
                RdpColumnBorder.Padding,
                _chromeResizeVm.CurrentTopChromeHeight);

            _rdpViewportVm.IsRdpFocusMode = true;
            _chromeResizeVm.CurrentTopChromeHeight = 0;
            _rdpToolbarVm.IsFocusModeActive = true;

            ApplyTopChromeLayout(animated: true);

            RdpFloatingToolbar.Visibility = Visibility.Visible;
            RdpFloatingToolbar.Opacity = 0.35;

            AnimateRdpFocusTransition(focus: true);
            ForceRdpViewportResize();
        }

        private void ExitRdpFocusMode()
        {
            if (!_rdpViewportVm.IsRdpFocusMode) return;

            RdpFloatingToolbar.Visibility = Visibility.Collapsed;
            _floatingToolbarHideTimer?.Stop();

            _rdpViewportVm.IsRdpFocusMode = false;
            _rdpToolbarVm.IsFocusModeActive = false;
            _chromeResizeVm.CurrentTopChromeHeight = _rdpViewportVm.SavedTopChromeHeight;
            _rdpViewportVm.Restore(RdpWorkspaceGrid, RdpColumnBorder);
            ApplyTopChromeLayout(animated: true);

            AnimateRdpFocusTransition(focus: false);
            ForceRdpViewportResize();
        }

        private void MeasureDefaultChromeHeights()
        {
            if (_chromeResizeVm.IsMeasured) return;

            MainContentGrid.UpdateLayout();

            var dashboard = 0.0;
            var active = ActiveConnectionHeader.ActualHeight > 1 ? ActiveConnectionHeader.ActualHeight + 10 : 76;
            var toolbar = ExtraIconToolbar.ActualHeight > 1 ? ExtraIconToolbar.ActualHeight + 4 : 52;
            var bottom = BottomPanels.ActualHeight > 1 ? BottomPanels.ActualHeight + 10 : 130;

            _chromeResizeVm.DefaultDashboardHeight = dashboard;
            _chromeResizeVm.DefaultActiveHeaderHeight = active;
            _chromeResizeVm.DefaultToolbarHeight = toolbar;
            _chromeResizeVm.DefaultTopChromeHeight = dashboard + active + toolbar;
            _chromeResizeVm.DefaultBottomChromeHeight = bottom;
            _chromeResizeVm.CurrentTopChromeHeight = _chromeResizeVm.DefaultTopChromeHeight;
            _chromeResizeVm.IsMeasured = true;
        }

        private void EnsureChromeDefaultsMeasured()
        {
            if (!_chromeResizeVm.IsMeasured)
                MeasureDefaultChromeHeights();
        }

        private void ResetRdpChromeLayout(bool animated)
        {
            EnsureChromeDefaultsMeasured();
            if (_rdpViewportVm.IsRdpFocusMode)
            {
                ExitRdpFocusMode();
                return;
            }

            if (animated)
                AnimateTopChromeTo(_chromeResizeVm.DefaultTopChromeHeight);
            else
            {
                _chromeResizeVm.CurrentTopChromeHeight = _chromeResizeVm.DefaultTopChromeHeight;
                ApplyTopChromeLayout(false);
            }
        }

        private void ApplyTopChromeLayout(bool animated)
        {
            EnsureChromeDefaultsMeasured();

            var topH = _chromeResizeVm.CurrentTopChromeHeight;
            var ratio = _chromeResizeVm.OpacityRatio;
            var collapsed = _chromeResizeVm.IsFullyCollapsed || _rdpViewportVm.IsRdpFocusMode;

            if (collapsed)
            {
                RdpDashboardRow.Height = new GridLength(0);
                RdpActiveConnectionRow.Height = new GridLength(0);
                RdpToolbarRow.Height = new GridLength(0);
                ActiveConnectionHeader.Visibility = Visibility.Collapsed;
                ExtraIconToolbar.Visibility = Visibility.Collapsed;
            }
            else
            {
                ActiveConnectionHeader.Visibility = Visibility.Visible;
                ExtraIconToolbar.Visibility = Visibility.Visible;

                RdpDashboardRow.Height = new GridLength(0);

                var totalDefault = _chromeResizeVm.DefaultActiveHeaderHeight
                                   + _chromeResizeVm.DefaultToolbarHeight;
                if (totalDefault <= 0) totalDefault = 1;

                var activeH = Math.Max(0, topH * (_chromeResizeVm.DefaultActiveHeaderHeight / totalDefault));
                var toolH = Math.Max(0, topH * (_chromeResizeVm.DefaultToolbarHeight / totalDefault));

                RdpActiveConnectionRow.Height = new GridLength(activeH);
                RdpToolbarRow.Height = new GridLength(toolH);

                ActiveConnectionHeader.Opacity = ratio;
                ExtraIconToolbar.Opacity = ratio;
            }

            if (_rdpViewportVm.IsRdpFocusMode)
            {
                RdpBottomChromeRow.Height = new GridLength(0);
                BottomPanels.Visibility = Visibility.Collapsed;
                Grid.SetRow(RdpWorkspaceGrid, 0);
                Grid.SetRowSpan(RdpWorkspaceGrid, 5);
                RdpColumnBorder.Padding = new Thickness(0);
                RdpContentGrid.Margin = new Thickness(0);
            }
            else if (ShouldShowRdpBottomChrome())
            {
                var topRatio = _chromeResizeVm.DefaultTopChromeHeight <= 0
                    ? 1
                    : Math.Clamp(_chromeResizeVm.CurrentTopChromeHeight / _chromeResizeVm.DefaultTopChromeHeight, 0, 1);
                var bottomH = _chromeResizeVm.DefaultBottomChromeHeight * topRatio;

                if (bottomH <= 1)
                {
                    RdpBottomChromeRow.Height = new GridLength(0);
                    BottomPanels.Visibility = Visibility.Collapsed;
                }
                else
                {
                    RdpBottomChromeRow.Height = new GridLength(bottomH);
                    BottomPanels.Visibility = Visibility.Visible;
                    BottomPanels.Opacity = Math.Max(0.35, topRatio);
                }

                Grid.SetRow(RdpWorkspaceGrid, 3);
                Grid.SetRowSpan(RdpWorkspaceGrid, 1);
            }
            else
            {
                RdpBottomChromeRow.Height = new GridLength(0);
                BottomPanels.Visibility = Visibility.Collapsed;
                Grid.SetRow(RdpWorkspaceGrid, 3);
                Grid.SetRowSpan(RdpWorkspaceGrid, 1);
            }

            RdpContentGrid.Margin = new Thickness(0);

            if (_isResizingRdpChrome)
                ApplyRdpViewportSizeNow();
            else
                ForceRdpViewportResize();

            if (animated && !_isResizingRdpChrome)
            {
                // subtle chrome fade handled via opacity above
            }
        }

        private void OnRdpResizeDragHandleMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                ToggleRdpViewportFullscreen();
                e.Handled = true;
                return;
            }

            if (_rdpViewportVm.IsRdpFocusMode) return;

            EnsureChromeDefaultsMeasured();
            _isResizingRdpChrome = true;
            _resizeDragStartY = e.GetPosition(MainContentGrid).Y;
            _resizeDragStartTopHeight = _chromeResizeVm.CurrentTopChromeHeight;
            _lastResizeFrameUtc = DateTime.MinValue;
            if (sender is System.Windows.UIElement element)
                element.CaptureMouse();
            e.Handled = true;
        }

        private void OnRdpResizeDragHandleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isResizingRdpChrome || e.LeftButton != MouseButtonState.Pressed) return;
            UpdateRdpChromeResizeFromMouse(e);
            e.Handled = true;
        }

        private void OnRdpResizeDragHandleMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isResizingRdpChrome) return;
            _isResizingRdpChrome = false;
            if (sender is System.Windows.UIElement element)
                element.ReleaseMouseCapture();
            SnapTopChromeAfterDrag();
            e.Handled = true;
        }

        private void OnRdpResizeDragHandleMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isResizingRdpChrome || e.LeftButton != MouseButtonState.Pressed) return;
            UpdateRdpChromeResizeFromMouse(e);
        }

        private void UpdateRdpChromeResizeFromMouse(System.Windows.Input.MouseEventArgs e)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastResizeFrameUtc).TotalMilliseconds < 16 && _lastResizeFrameUtc != DateTime.MinValue)
                return;
            _lastResizeFrameUtc = now;

            var y = e.GetPosition(MainContentGrid).Y;
            var deltaY = y - _resizeDragStartY;
            _chromeResizeVm.CurrentTopChromeHeight = Math.Clamp(
                _resizeDragStartTopHeight + deltaY,
                0,
                _chromeResizeVm.DefaultTopChromeHeight);

            ApplyTopChromeLayout(false);
        }

        private void SnapTopChromeAfterDrag()
        {
            var h = _chromeResizeVm.CurrentTopChromeHeight;
            if (h < RdpChromeResizeViewModel.CollapseThreshold * 0.5)
                AnimateTopChromeTo(0);
            else if (h > _chromeResizeVm.DefaultTopChromeHeight * 0.9)
                AnimateTopChromeTo(_chromeResizeVm.DefaultTopChromeHeight);
            else
                ForceRdpViewportResize();
        }

        private void AnimateTopChromeTo(double target)
        {
            _chromeAnimateTimer?.Stop();
            var from = _chromeResizeVm.CurrentTopChromeHeight;
            var started = DateTime.UtcNow;
            const int durationMs = 220;

            _chromeAnimateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _chromeAnimateTimer.Tick += (_, _) =>
            {
                var t = Math.Min(1.0, (DateTime.UtcNow - started).TotalMilliseconds / durationMs);
                var eased = 1 - Math.Pow(1 - t, 3);
                _chromeResizeVm.CurrentTopChromeHeight = from + ((target - from) * eased);
                ApplyTopChromeLayout(false);
                if (t >= 1)
                {
                    _chromeAnimateTimer.Stop();
                    _chromeResizeVm.CurrentTopChromeHeight = target;
                    ApplyTopChromeLayout(false);
                }
            };
            _chromeAnimateTimer.Start();
        }

        private void AnimateRdpFocusTransition(bool focus)
        {
        }

        private void ForceRdpViewportResize()
        {
            ScheduleRdpResize();
            Dispatcher.BeginInvoke(ScheduleRdpResize, DispatcherPriority.Render);
        }

        private void OnRdpViewportMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_rdpViewportVm.IsRdpFocusMode || RdpFloatingToolbar.Visibility != Visibility.Visible) return;

            var pos = e.GetPosition(RdpContentGrid);
            if (pos.Y <= 80)
                RevealFloatingToolbar();
            else
                ScheduleHideFloatingToolbar();
        }

        private void RevealFloatingToolbar()
        {
            _floatingToolbarHideTimer?.Stop();
            RdpFloatingToolbar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(RdpFloatingToolbar.Opacity, 0.96, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void ScheduleHideFloatingToolbar()
        {
            _floatingToolbarHideTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _floatingToolbarHideTimer.Stop();
            _floatingToolbarHideTimer.Tick -= OnFloatingToolbarHideTick;
            _floatingToolbarHideTimer.Tick += OnFloatingToolbarHideTick;
            _floatingToolbarHideTimer.Start();
        }

        private void OnFloatingToolbarHideTick(object? sender, EventArgs e)
        {
            _floatingToolbarHideTimer?.Stop();
            if (!_rdpViewportVm.IsRdpFocusMode) return;
            RdpFloatingToolbar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(RdpFloatingToolbar.Opacity, 0.22, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void OnFloatingCtrlAltDelClick(object sender, RoutedEventArgs e)
        {
            var server = _activeSession?.Server ?? GetQuickActionServer();
            using var audit = AuditOperationScope.BeginRdp(AuditActions.CtrlAltDel, server, _activeSession?.Id);
            try
            {
                if (!ConfirmSendCtrlAltDel())
                {
                    audit.CompleteCancelled("User declined confirmation");
                    return;
                }

                _activeSession?.CleanSession?.SendCtrlAltDel();
                audit.CompleteSuccess();
            }
            catch (Exception ex)
            {
                audit.CompleteFailed(ex.Message, ex);
                ShowToast("Ctrl+Alt+Del gönderilemedi.", "⚠️");
            }
        }

        private void OnFloatingAiClick(object sender, RoutedEventArgs e) => ToggleAiPanel();

        private void OnRdpScreenshotClick(object sender, RoutedEventArgs e)
        {
            ShowToast("Screenshot yakında eklenecek.", "📷");
        }

        private CancellationTokenSource? _toastCts;
        private void OnToggleGridMonitorClick(object sender, RoutedEventArgs e)
        {
            if (GridMonitorOverlay.Visibility == Visibility.Visible)
            {
                GridMonitorOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                GridMonitorItemsControl.ItemsSource = _servers;
                GridMonitorCountText.Text = $"{_servers.Count} Adet Sunucu";
                GridMonitorOverlay.Visibility = Visibility.Visible;
            }
        }

        private void OnGridMonitorConnectClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ServerInfo server)
            {
                GridMonitorOverlay.Visibility = Visibility.Collapsed;
                _selectedServer = server;
                ConnectToServer(server);
            }
        }

        private void OnGridMonitorInfoClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ServerInfo server)
            {
                GridMonitorOverlay.Visibility = Visibility.Collapsed;
                ActivateDashboardForServer(server);
            }
        }

        public async void ShowToast(string message, string icon = "ℹ️", int durationMs = -1, NotificationService.AlertLevel level = NotificationService.AlertLevel.Info)
        {
            var settings = SettingsService.Instance.Current;
            if (!NotificationService.ShouldShow(level, settings))
                return;

            if (durationMs < 0)
                durationMs = Math.Clamp(settings.NotificationDurationMs, 1000, 15000);

            SettingsApplicator.PlayNotificationSound(settings);
            NotificationService.ShowDesktop(settings.ApplicationName, message, settings, level);

            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var ct = _toastCts.Token;

            ToastIcon.Text = icon;
            ToastMessage.Text = message;
            ToastOverlay.Visibility = Visibility.Visible;
            ToastOverlay.Opacity = 0;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
            ToastOverlay.BeginAnimation(OpacityProperty, fadeIn);

            try
            {
                await Task.Delay(durationMs, ct);
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
                ToastOverlay.BeginAnimation(OpacityProperty, fadeOut);
                await Task.Delay(500, ct);
                ToastOverlay.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) { }
        }
    }
}
