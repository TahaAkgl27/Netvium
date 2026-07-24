using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.ViewModels
{
    public enum RdpDisplayMode
    {
        SmartSizing,
        KeepAspectRatio,
        Stretch
    }

    public enum RdpPerformanceLevel
    {
        Low,
        Balanced,
        High
    }

    public sealed class RdpSessionToolbarViewModel : INotifyPropertyChanged
    {
        private bool _isConnected;
        private bool _hasServerContext;
        private bool _isPremium;
        private bool _hasCachedData;
        private bool _isFavorite;
        private bool _isFocusModeActive;
        private bool _isSendingCad;
        private bool _isClipboardMenuOpen;
        private bool _isSessionSettingsOpen;
        private bool _isEventSearchOpen;
        private bool _clipboardSyncEnabled;
        private bool _captureKeyboard = true;
        private bool _captureMouse = true;
        private RdpDisplayMode _displayMode = RdpDisplayMode.SmartSizing;
        private RdpPerformanceLevel _performanceLevel = RdpPerformanceLevel.Balanced;
        private int _resolutionScale = 100;
        private string _eventSearchQuery = string.Empty;
        private string _serverDisplayName = "No server selected";
        private string _favoriteDisabledTooltip = "Analiz ekranında sunucu seçin";
        private bool _isFavoritesEnabled;

        public RdpSessionToolbarViewModel()
        {
            ResolutionOptions = new ObservableCollection<int> { 100, 125, 150, 175, 200, 250 };
            FilteredEvents = new ObservableCollection<TimelineEvent>();

            ToggleClipboardMenuCommand = new RelayCommand(_ => ToggleExclusivePopup(ref _isClipboardMenuOpen, nameof(IsClipboardMenuOpen)));
            ClipboardCopyCommand = new RelayCommand(_ => RequestClipboardCopy?.Invoke(), _ => IsClipboardEnabled);
            ClipboardPasteCommand = new RelayCommand(_ => RequestClipboardPaste?.Invoke(), _ => IsClipboardEnabled);
            ClipboardFileTransferCommand = new RelayCommand(_ => RequestClipboardFileTransfer?.Invoke(), _ => IsClipboardEnabled);

            SendCtrlAltDelCommand = new AsyncRelayCommand(
                async _ => await SendCadAsync(),
                _ => IsCtrlAltDelEnabled && !IsSendingCad);

            ToggleFocusModeCommand = new RelayCommand(_ => RequestToggleFocusMode?.Invoke(), _ => IsFocusModeEnabled);
            ToggleSessionSettingsCommand = new RelayCommand(_ => ToggleExclusivePopup(ref _isSessionSettingsOpen, nameof(IsSessionSettingsOpen)), _ => IsSessionSettingsEnabled);
            ToggleEventSearchCommand = new RelayCommand(_ => ToggleEventSearch(), _ => IsEventSearchEnabled);
            OpenRiskAnalysisCommand = new RelayCommand(_ => RequestRiskAnalysis?.Invoke(), _ => IsRiskAnalysisEnabled);
            ToggleFavoriteCommand = new RelayCommand(_ => RequestToggleFavorite?.Invoke(), _ => IsFavoritesEnabled);
            _isFavoritesEnabled = false;

            SetDisplayModeCommand = new RelayCommand(
                p => ApplyDisplayMode(ParseDisplayMode(p)),
                _ => IsSessionSettingsEnabled);

            SetPerformanceCommand = new RelayCommand(
                p => ApplyPerformance(ParsePerformance(p)),
                _ => IsSessionSettingsEnabled);

            SetResolutionCommand = new RelayCommand(
                p => ApplyResolution(ParseInt(p, 100)),
                _ => IsSessionSettingsEnabled);

            ToggleClipboardSyncCommand = new RelayCommand(_ => ToggleClipboardSync(), _ => IsSessionSettingsEnabled);
            ToggleCaptureKeyboardCommand = new RelayCommand(_ => ToggleCaptureKeyboard(), _ => IsSessionSettingsEnabled);
            ToggleCaptureMouseCommand = new RelayCommand(_ => ToggleCaptureMouse(), _ => IsSessionSettingsEnabled);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RequestClipboardCopy;
        public event Action? RequestClipboardPaste;
        public event Action? RequestClipboardFileTransfer;
        public event Action? RequestToggleFocusMode;
        public event Action? RequestRiskAnalysis;
        public event Action? RequestToggleFavorite;
        public event Action<RdpDisplayMode>? RequestDisplayMode;
        public event Action<RdpPerformanceLevel>? RequestPerformanceLevel;
        public event Action<int>? RequestResolutionScale;
        public event Action<bool>? RequestClipboardSync;
        public event Action<bool>? RequestCaptureKeyboard;
        public event Action<bool>? RequestCaptureMouse;
        public event Func<Task>? RequestSendCtrlAltDel;
        public event Func<string, Task<IEnumerable<TimelineEvent>>>? RequestEventSearch;

        public ObservableCollection<int> ResolutionOptions { get; }
        public ObservableCollection<TimelineEvent> FilteredEvents { get; }

        public ICommand ToggleClipboardMenuCommand { get; }
        public ICommand ClipboardCopyCommand { get; }
        public ICommand ClipboardPasteCommand { get; }
        public ICommand ClipboardFileTransferCommand { get; }
        public ICommand SendCtrlAltDelCommand { get; }
        public ICommand ToggleFocusModeCommand { get; }
        public ICommand ToggleSessionSettingsCommand { get; }
        public ICommand ToggleEventSearchCommand { get; }
        public ICommand OpenRiskAnalysisCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand SetDisplayModeCommand { get; }
        public ICommand SetPerformanceCommand { get; }
        public ICommand SetResolutionCommand { get; }
        public ICommand ToggleClipboardSyncCommand { get; }
        public ICommand ToggleCaptureKeyboardCommand { get; }
        public ICommand ToggleCaptureMouseCommand { get; }

        public bool IsConnected
        {
            get => _isConnected;
            set { if (_isConnected == value) return; _isConnected = value; NotifyToolStates(); }
        }

        public bool HasServerContext
        {
            get => _hasServerContext;
            set { if (_hasServerContext == value) return; _hasServerContext = value; NotifyToolStates(); }
        }

        public bool IsPremium
        {
            get => _isPremium;
            set { if (_isPremium == value) return; _isPremium = value; NotifyToolStates(); }
        }

        public bool HasCachedData
        {
            get => _hasCachedData;
            set { if (_hasCachedData == value) return; _hasCachedData = value; NotifyToolStates(); }
        }

        public string ServerDisplayName
        {
            get => _serverDisplayName;
            set { _serverDisplayName = value; OnPropertyChanged(); }
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set { if (_isFavorite == value) return; _isFavorite = value; OnPropertyChanged(); OnPropertyChanged(nameof(FavoriteTooltip)); }
        }

        public bool IsFocusModeActive
        {
            get => _isFocusModeActive;
            set { if (_isFocusModeActive == value) return; _isFocusModeActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocusModeTooltip)); }
        }

        public bool IsSendingCad
        {
            get => _isSendingCad;
            set { if (_isSendingCad == value) return; _isSendingCad = value; OnPropertyChanged(); }
        }

        public bool IsClipboardMenuOpen
        {
            get => _isClipboardMenuOpen;
            set { if (_isClipboardMenuOpen == value) return; _isClipboardMenuOpen = value; OnPropertyChanged(); }
        }

        public bool IsSessionSettingsOpen
        {
            get => _isSessionSettingsOpen;
            set { if (_isSessionSettingsOpen == value) return; _isSessionSettingsOpen = value; OnPropertyChanged(); }
        }

        public bool IsEventSearchOpen
        {
            get => _isEventSearchOpen;
            set { if (_isEventSearchOpen == value) return; _isEventSearchOpen = value; OnPropertyChanged(); }
        }

        public bool ClipboardSyncEnabled
        {
            get => _clipboardSyncEnabled;
            set { if (_clipboardSyncEnabled == value) return; _clipboardSyncEnabled = value; OnPropertyChanged(); }
        }

        public bool CaptureKeyboard
        {
            get => _captureKeyboard;
            set { if (_captureKeyboard == value) return; _captureKeyboard = value; OnPropertyChanged(); }
        }

        public bool CaptureMouse
        {
            get => _captureMouse;
            set { if (_captureMouse == value) return; _captureMouse = value; OnPropertyChanged(); }
        }

        public RdpDisplayMode DisplayMode
        {
            get => _displayMode;
            set { if (_displayMode == value) return; _displayMode = value; OnPropertyChanged(); }
        }

        public RdpPerformanceLevel PerformanceLevel
        {
            get => _performanceLevel;
            set { if (_performanceLevel == value) return; _performanceLevel = value; OnPropertyChanged(); }
        }

        public int ResolutionScale
        {
            get => _resolutionScale;
            set { if (_resolutionScale == value) return; _resolutionScale = value; OnPropertyChanged(); }
        }

        public string EventSearchQuery
        {
            get => _eventSearchQuery;
            set
            {
                if (_eventSearchQuery == value) return;
                _eventSearchQuery = value ?? string.Empty;
                OnPropertyChanged();
                _ = RunEventSearchAsync();
            }
        }

        public bool IsClipboardEnabled => IsConnected;
        public bool IsCtrlAltDelEnabled => IsConnected;
        public bool IsFocusModeEnabled => IsConnected;
        public bool IsSessionSettingsEnabled => IsConnected;
        public bool IsRiskAnalysisEnabled =>
            FeatureEntitlementService.Instance.IsPremiumEnabled && (IsConnected || HasCachedData);
        public bool IsEventSearchEnabled => IsConnected || HasCachedData;
        public bool IsFavoritesEnabled
        {
            get => _isFavoritesEnabled;
            set
            {
                if (_isFavoritesEnabled == value) return;
                _isFavoritesEnabled = value;
                NotifyToolStates();
            }
        }

        public string FavoriteDisabledTooltip
        {
            get => _favoriteDisabledTooltip;
            set
            {
                if (_favoriteDisabledTooltip == value) return;
                _favoriteDisabledTooltip = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteTooltip));
            }
        }

        public string ClipboardTooltip => IsClipboardEnabled
            ? "Clipboard — copy, paste, file transfer"
            : "Clipboard (connection required)";

        public string CtrlAltDelTooltip => IsSendingCad
            ? "Sending Ctrl+Alt+Del..."
            : IsCtrlAltDelEnabled ? "Send Ctrl+Alt+Del" : "Ctrl+Alt+Del (connection required)";

        public string FocusModeTooltip => IsFocusModeActive
            ? "Focus mode active — click to exit"
            : IsFocusModeEnabled ? "Fullscreen / Focus mode" : "Focus mode (connection required)";

        public string SessionSettingsTooltip => IsSessionSettingsEnabled
            ? "Session settings — display, performance, input"
            : "Session settings (connection required)";

        public string RiskAnalysisTooltip => !FeatureEntitlementService.Instance.IsPremiumEnabled
            ? "Risk analysis (AI Premium required)"
            : IsRiskAnalysisEnabled ? "Risk analysis — AI panel" : "Risk analysis (no server data — scan or connect first)";

        public string EventSearchTooltip => IsEventSearchEnabled
            ? "Live Event Log search"
            : "Event search (server or cache required)";

        public string FavoriteTooltip => !IsFavoritesEnabled
            ? FavoriteDisabledTooltip
            : IsFavorite
                ? $"{ServerDisplayName} favorilerden çıkar"
                : $"{ServerDisplayName} favorilere ekle";

        public void CloseAllPopups()
        {
            IsClipboardMenuOpen = false;
            IsSessionSettingsOpen = false;
            IsEventSearchOpen = false;
        }

        public void SetFilteredEvents(IEnumerable<TimelineEvent> events)
        {
            FilteredEvents.Clear();
            foreach (var ev in events)
                FilteredEvents.Add(ev);
            OnPropertyChanged(nameof(FilteredEventCount));
            OnPropertyChanged(nameof(HasFilteredEvents));
        }

        public int FilteredEventCount => FilteredEvents.Count;
        public bool HasFilteredEvents => FilteredEvents.Count > 0;

        private void NotifyToolStates()
        {
            OnPropertyChanged(nameof(IsClipboardEnabled));
            OnPropertyChanged(nameof(IsCtrlAltDelEnabled));
            OnPropertyChanged(nameof(IsFocusModeEnabled));
            OnPropertyChanged(nameof(IsSessionSettingsEnabled));
            OnPropertyChanged(nameof(IsRiskAnalysisEnabled));
            OnPropertyChanged(nameof(IsEventSearchEnabled));
            OnPropertyChanged(nameof(IsFavoritesEnabled));
            OnPropertyChanged(nameof(ClipboardTooltip));
            OnPropertyChanged(nameof(CtrlAltDelTooltip));
            OnPropertyChanged(nameof(FocusModeTooltip));
            OnPropertyChanged(nameof(SessionSettingsTooltip));
            OnPropertyChanged(nameof(RiskAnalysisTooltip));
            OnPropertyChanged(nameof(EventSearchTooltip));
            OnPropertyChanged(nameof(FavoriteTooltip));
            (SendCtrlAltDelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ClipboardCopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClipboardPasteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClipboardFileTransferCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleFocusModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleSessionSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleEventSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenRiskAnalysisCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleFavoriteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ToggleExclusivePopup(ref bool field, string propertyName)
        {
            var next = !field;
            CloseAllPopups();
            field = next;
            OnPropertyChanged(propertyName);
        }

        private void ToggleEventSearch()
        {
            var next = !IsEventSearchOpen;
            CloseAllPopups();
            IsEventSearchOpen = next;
            if (next)
                _ = RunEventSearchAsync();
        }

        private async Task RunEventSearchAsync()
        {
            if (RequestEventSearch == null) return;
            try
            {
                var results = await RequestEventSearch(EventSearchQuery);
                SetFilteredEvents(results);
            }
            catch (Exception ex)
            {
                _ = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.FireAndForget,
                    "RDP.EventSearch",
                    notifyUser: false);

                FilteredEvents.Clear();
                OnPropertyChanged(nameof(FilteredEventCount));
                OnPropertyChanged(nameof(HasFilteredEvents));
            }
        }

        private async Task SendCadAsync()
        {
            if (RequestSendCtrlAltDel == null) return;
            IsSendingCad = true;
            try
            {
                await RequestSendCtrlAltDel();
                await Task.Delay(900);
            }
            finally
            {
                IsSendingCad = false;
            }
        }

        private void ApplyDisplayMode(RdpDisplayMode mode)
        {
            DisplayMode = mode;
            RequestDisplayMode?.Invoke(mode);
        }

        private void ApplyPerformance(RdpPerformanceLevel level)
        {
            PerformanceLevel = level;
            RequestPerformanceLevel?.Invoke(level);
        }

        private void ApplyResolution(int scale)
        {
            ResolutionScale = scale;
            RequestResolutionScale?.Invoke(scale);
        }

        private void ToggleClipboardSync()
        {
            ClipboardSyncEnabled = !ClipboardSyncEnabled;
            RequestClipboardSync?.Invoke(ClipboardSyncEnabled);
        }

        private void ToggleCaptureKeyboard()
        {
            CaptureKeyboard = !CaptureKeyboard;
            RequestCaptureKeyboard?.Invoke(CaptureKeyboard);
        }

        private void ToggleCaptureMouse()
        {
            CaptureMouse = !CaptureMouse;
            RequestCaptureMouse?.Invoke(CaptureMouse);
        }

        private static RdpDisplayMode ParseDisplayMode(object? parameter)
        {
            if (parameter is RdpDisplayMode mode) return mode;
            if (parameter is string s && Enum.TryParse<RdpDisplayMode>(s, true, out var parsed)) return parsed;
            return RdpDisplayMode.SmartSizing;
        }

        private static RdpPerformanceLevel ParsePerformance(object? parameter)
        {
            if (parameter is RdpPerformanceLevel level) return level;
            if (parameter is string s && Enum.TryParse<RdpPerformanceLevel>(s, true, out var parsed)) return parsed;
            return RdpPerformanceLevel.Balanced;
        }

        private static int ParseInt(object? parameter, int fallback)
        {
            if (parameter is int i) return i;
            if (parameter is string s && int.TryParse(s, out var parsed)) return parsed;
            return fallback;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
