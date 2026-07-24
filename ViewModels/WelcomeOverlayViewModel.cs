using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Netvium.Models;

namespace Netvium.ViewModels
{
    public enum WelcomeOverlayPhase
    {
        Welcome,
        Loading,
        Results,
        Failed
    }

    public sealed class WelcomeFeatureItem
    {
        public string IconGlyph { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string AccentColor { get; init; } = "#60A5FA";
    }

    public sealed class WelcomeOverlayViewModel : INotifyPropertyChanged
    {
        private bool _isWelcomeOverlayVisible = true;
        private WelcomeOverlayPhase _phase = WelcomeOverlayPhase.Welcome;
        private bool _isDailyAnalysisRunning;
        private bool _isDetailsExpanded;
        private string _greetingTitle = "Günaydın";
        private string _subtitle = "Netvium bugün 0 sunucuyu kontrol etmeye hazır.";
        private string _loadingMessage = "Sunucular kontrol ediliyor...";
        private string _resultSubtitle = "Günlük sunucu özeti hazır.";
        private string? _failureMessage;
        private double _loadingProgress;
        private int _totalServers;
        private int _healthyCount;
        private int _warningCount;
        private int _criticalCount;

        public WelcomeOverlayViewModel()
        {
            Features = new ObservableCollection<WelcomeFeatureItem>
            {
                new() { IconGlyph = "\uE95E", Title = "Sunucu sağlık durumu", Description = "CPU, RAM ve disk eşikleri", AccentColor = "#34D399" },
                new() { IconGlyph = "\uE7BA", Title = "Kritik uyarılar", Description = "Servis ve erişim sorunları", AccentColor = "#F87171" },
                new() { IconGlyph = "\uE72E", Title = "Açık port / risk özeti", Description = "RPC ve güvenlik yüzeyi", AccentColor = "#A78BFA" },
                new() { IconGlyph = "\uE9D2", Title = "Son bağlantı ve performans", Description = "Gecikme ve oturum özeti", AccentColor = "#60A5FA" }
            };

            CriticalFindings = new ObservableCollection<DailyServerFinding>();
            WarningFindings = new ObservableCollection<DailyServerFinding>();
            Recommendations = new ObservableCollection<string>();

            StartDailyAnalysisCommand = new AsyncRelayCommand(
                async _ => await OnStartAnalysisAsync(),
                _ => !IsDailyAnalysisRunning && Phase == WelcomeOverlayPhase.Welcome);

            SkipWelcomeCommand = new RelayCommand(
                _ => RequestClose?.Invoke(),
                _ => !IsDailyAnalysisRunning);

            CloseAnalysisOverlayCommand = new RelayCommand(
                _ => RequestClose?.Invoke(),
                _ => (Phase == WelcomeOverlayPhase.Results || Phase == WelcomeOverlayPhase.Failed) && !IsDailyAnalysisRunning);

            OpenDetailsCommand = new RelayCommand(
                _ => IsDetailsExpanded = !IsDetailsExpanded,
                _ => Phase == WelcomeOverlayPhase.Results && HasResultLists);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Func<Task>? StartAnalysisRequested;
        public event Action? RequestClose;

        public ObservableCollection<WelcomeFeatureItem> Features { get; }
        public ObservableCollection<DailyServerFinding> CriticalFindings { get; }
        public ObservableCollection<DailyServerFinding> WarningFindings { get; }
        public ObservableCollection<string> Recommendations { get; }

        public ICommand StartDailyAnalysisCommand { get; }
        public ICommand SkipWelcomeCommand { get; }
        public ICommand CloseAnalysisOverlayCommand { get; }
        public ICommand OpenDetailsCommand { get; }

        public bool IsWelcomeOverlayVisible
        {
            get => _isWelcomeOverlayVisible;
            set { if (_isWelcomeOverlayVisible == value) return; _isWelcomeOverlayVisible = value; OnPropertyChanged(); }
        }

        public WelcomeOverlayPhase Phase
        {
            get => _phase;
            private set
            {
                if (_phase == value) return;
                _phase = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWelcomePhase));
                OnPropertyChanged(nameof(IsLoadingPhase));
                OnPropertyChanged(nameof(IsResultsPhase));
                OnPropertyChanged(nameof(IsFailedPhase));
                RaiseCommandStates();
            }
        }

        public bool IsWelcomePhase => Phase == WelcomeOverlayPhase.Welcome;
        public bool IsLoadingPhase => Phase == WelcomeOverlayPhase.Loading;
        public bool IsResultsPhase => Phase == WelcomeOverlayPhase.Results;
        public bool IsFailedPhase => Phase == WelcomeOverlayPhase.Failed;

        public bool IsDailyAnalysisRunning
        {
            get => _isDailyAnalysisRunning;
            private set
            {
                if (_isDailyAnalysisRunning == value) return;
                _isDailyAnalysisRunning = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

        public bool IsDetailsExpanded
        {
            get => _isDetailsExpanded;
            set
            {
                if (_isDetailsExpanded == value) return;
                _isDetailsExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DetailsToggleLabel));
            }
        }

        public string DetailsToggleLabel => IsDetailsExpanded ? "Detayları Gizle" : "Detayları Aç";

        public bool HasResultLists => CriticalFindings.Count > 0 || WarningFindings.Count > 0;
        public bool HasCriticalFindings => CriticalFindings.Count > 0;
        public bool HasWarningFindings => WarningFindings.Count > 0;
        public bool HasRecommendations => Recommendations.Count > 0;

        public string GreetingTitle
        {
            get => _greetingTitle;
            set { if (_greetingTitle == value) return; _greetingTitle = value; OnPropertyChanged(); }
        }

        public string Subtitle
        {
            get => _subtitle;
            set { if (_subtitle == value) return; _subtitle = value; OnPropertyChanged(); }
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set { if (_loadingMessage == value) return; _loadingMessage = value; OnPropertyChanged(); }
        }

        public string ResultSubtitle
        {
            get => _resultSubtitle;
            set { if (_resultSubtitle == value) return; _resultSubtitle = value; OnPropertyChanged(); }
        }

        public string? FailureMessage
        {
            get => _failureMessage;
            private set { if (_failureMessage == value) return; _failureMessage = value; OnPropertyChanged(); }
        }

        public double LoadingProgress
        {
            get => _loadingProgress;
            set { if (Math.Abs(_loadingProgress - value) < 0.01) return; _loadingProgress = value; OnPropertyChanged(); }
        }

        public int TotalServers
        {
            get => _totalServers;
            private set { if (_totalServers == value) return; _totalServers = value; OnPropertyChanged(); }
        }

        public int HealthyCount
        {
            get => _healthyCount;
            private set { if (_healthyCount == value) return; _healthyCount = value; OnPropertyChanged(); }
        }

        public int WarningCount
        {
            get => _warningCount;
            private set { if (_warningCount == value) return; _warningCount = value; OnPropertyChanged(); }
        }

        public int CriticalCount
        {
            get => _criticalCount;
            private set { if (_criticalCount == value) return; _criticalCount = value; OnPropertyChanged(); }
        }

        public void UpdateServerCount(int serverCount) => ConfigureGreeting(GreetingTitle, serverCount);

        public void ConfigureGreeting(string greetingTitle, int serverCount)
        {
            GreetingTitle = greetingTitle;
            Subtitle = serverCount == 1
                ? "Netvium bugün 1 sunucuyu kontrol etmeye hazır."
                : $"Netvium bugün {serverCount} sunucuyu kontrol etmeye hazır.";
        }

        public void BeginAnalysis()
        {
            IsDailyAnalysisRunning = true;
            IsDetailsExpanded = false;
            Phase = WelcomeOverlayPhase.Loading;
            LoadingMessage = "Sunucular okunuyor...";
            LoadingProgress = 0;
            FailureMessage = null;
        }

        public void SetLoadingStage(string message, double progress)
        {
            LoadingMessage = message;
            LoadingProgress = Math.Clamp(progress, 0, 100);
        }

        public void ApplyReport(DailyAnalysisReport report)
        {
            IsDailyAnalysisRunning = false;
            LoadingProgress = 100;

            if (report.IsFailed)
            {
                FailureMessage = report.FailureMessage;
                Phase = WelcomeOverlayPhase.Failed;
                ResultSubtitle = report.FailureMessage ?? "Analiz tamamlanamadı.";
                return;
            }

            TotalServers = report.TotalServers;
            HealthyCount = report.HealthyCount;
            WarningCount = report.WarningCount;
            CriticalCount = report.CriticalCount;

            CriticalFindings.Clear();
            foreach (var item in report.CriticalFindings)
                CriticalFindings.Add(item);

            WarningFindings.Clear();
            foreach (var item in report.WarningFindings)
                WarningFindings.Add(item);

            Recommendations.Clear();
            foreach (var item in report.Recommendations)
                Recommendations.Add(item);

            ResultSubtitle = $"Toplam {report.TotalServers} sunucu — dashboard ile senkron özet.";
            Phase = WelcomeOverlayPhase.Results;
            IsDetailsExpanded = CriticalFindings.Count > 0 || WarningFindings.Count > 0;

            OnPropertyChanged(nameof(HasResultLists));
            OnPropertyChanged(nameof(HasCriticalFindings));
            OnPropertyChanged(nameof(HasWarningFindings));
            OnPropertyChanged(nameof(HasRecommendations));
            RaiseCommandStates();
        }

        public void ShowError(string message)
        {
            IsDailyAnalysisRunning = false;
            FailureMessage = message;
            ResultSubtitle = message;
            Phase = WelcomeOverlayPhase.Failed;
            RaiseCommandStates();
        }

        public void ResetToWelcome()
        {
            IsDailyAnalysisRunning = false;
            IsDetailsExpanded = false;
            Phase = WelcomeOverlayPhase.Welcome;
            LoadingProgress = 0;
            FailureMessage = null;
            CriticalFindings.Clear();
            WarningFindings.Clear();
            Recommendations.Clear();
        }

        private async Task OnStartAnalysisAsync()
        {
            if (StartAnalysisRequested == null)
            {
                ShowError("Analiz servisi bağlı değil.");
                return;
            }

            try
            {
                BeginAnalysis();
                await StartAnalysisRequested.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyAnalysis] {ex}");
                ShowError($"Analiz hatası: {ex.Message}");
            }
        }

        private void RaiseCommandStates()
        {
            (CloseAnalysisOverlayCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartDailyAnalysisCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SkipWelcomeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenDetailsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
