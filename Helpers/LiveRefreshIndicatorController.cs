using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Netvium.Services;

namespace Netvium.Helpers
{
    public enum LiveRefreshMilestone
    {
        Started = 0,
        FastMetrics = 30,
        Services = 55,
        EventLog = 75,
        SmartAnalysis = 90,
        Saved = 100
    }

    /// <summary>
    /// Non-technical live refresh progress for the analysis toolbar.
    /// Never displays "Güncelleniyor %100" — completion transitions to "Güncel ✓".
    /// </summary>
    public sealed class LiveRefreshIndicatorController
    {
        private readonly FrameworkElement _root;
        private readonly TextBlock _statusText;
        private readonly Border _progressFill;
        private readonly FrameworkElement _progressTrack;
        private readonly Dispatcher _dispatcher;
        private readonly UiUpdateThrottle _textThrottle = new(TimeSpan.FromMilliseconds(120));

        private string? _serverKey;
        private int _targetPercent;
        private int _displayPercent;
        private bool _isTerminalState;
        private DispatcherTimer? _smoothTimer;
        private DispatcherTimer? _hideTimer;

        public LiveRefreshIndicatorController(
            FrameworkElement root,
            TextBlock statusText,
            Border progressFill,
            FrameworkElement progressTrack)
        {
            _root = root;
            _statusText = statusText;
            _progressFill = progressFill;
            _progressTrack = progressTrack;
            _dispatcher = root.Dispatcher;
            _progressTrack.SizeChanged += (_, _) => UpdateProgressFillWidth();
        }

        public void Begin(string serverKey)
        {
            if (string.IsNullOrEmpty(serverKey))
                return;

            _dispatcher.BeginInvoke(() =>
            {
                StopHideTimer();
                StopSmoothTimer();
                _serverKey = serverKey;
                _targetPercent = 0;
                _displayPercent = 0;
                _isTerminalState = false;
                _textThrottle.Reset();
                ResetStatusColor();

                _root.Visibility = Visibility.Visible;
                FadeTo(1, TimeSpan.FromMilliseconds(220));
                ApplyStatusText("Güncelleniyor %0");
                UpdateProgressFillWidth();
                EnsureSmoothTimer();
            });
        }

        public void Report(string serverKey, LiveRefreshMilestone milestone)
        {
            if (!IsActive(serverKey) || _isTerminalState)
                return;

            if (milestone == LiveRefreshMilestone.Saved)
            {
                NotifySnapshotSaved(serverKey);
                return;
            }

            var percent = Math.Min(99, (int)milestone);

            _dispatcher.BeginInvoke(() =>
            {
                if (!IsActive(serverKey) || _isTerminalState)
                    return;

                if (milestone == LiveRefreshMilestone.Started)
                {
                    _targetPercent = 0;
                    _displayPercent = 0;
                }
                else
                {
                    _targetPercent = Math.Max(_targetPercent, percent);
                }

                if (_textThrottle.ShouldUpdate())
                    ApplyStatusText($"Güncelleniyor %{Math.Min(_displayPercent, 99)}");

                EnsureSmoothTimer();
            });
        }

        public void MarkAiSkipped(string serverKey)
        {
            if (!IsActive(serverKey) || _isTerminalState)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                if (!IsActive(serverKey) || _isTerminalState)
                    return;
                _targetPercent = Math.Max(_targetPercent, (int)LiveRefreshMilestone.SmartAnalysis);
                EnsureSmoothTimer();
            });
        }

        public void NotifyAiFinished(string serverKey)
        {
            if (!IsActive(serverKey) || _isTerminalState)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                if (!IsActive(serverKey) || _isTerminalState)
                    return;
                _targetPercent = Math.Max(_targetPercent, (int)LiveRefreshMilestone.SmartAnalysis);
                EnsureSmoothTimer();
            });
        }

        public void NotifySnapshotSaved(string serverKey)
        {
            if (!IsActive(serverKey) || _isTerminalState)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                if (!IsActive(serverKey) || _isTerminalState)
                    return;
                _targetPercent = 99;
                EnsureSmoothTimer();
            });
        }

        public void MarkCompleted(string serverKey)
        {
            if (!IsActive(serverKey))
                return;

            _dispatcher.BeginInvoke(() =>
            {
                if (!IsActive(serverKey))
                    return;
                ShowTerminalState("Güncel ✓", success: true);
            });
        }

        public void MarkFailed(string serverKey)
        {
            if (!IsActive(serverKey))
                return;

            _dispatcher.BeginInvoke(() =>
            {
                if (!IsActive(serverKey))
                    return;
                ShowTerminalState("Son güncelleme tamamlanamadı", success: false);
            });
        }

        public void MarkCancelled(string? serverKey = null)
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (!string.IsNullOrEmpty(serverKey)
                    && !string.Equals(_serverKey, serverKey, StringComparison.OrdinalIgnoreCase))
                    return;

                ResetState();
                StopSmoothTimer();
                StopHideTimer();
                FadeTo(0, TimeSpan.FromMilliseconds(180), () =>
                {
                    _root.Visibility = Visibility.Collapsed;
                });
            });
        }

        private void ShowTerminalState(string message, bool success)
        {
            _isTerminalState = true;
            _targetPercent = 100;
            _displayPercent = 100;
            UpdateProgressFillWidth();
            ApplyStatusText(message);
            _statusText.Foreground = new System.Windows.Media.SolidColorBrush(
                success
                    ? System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80)
                    : System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24));

            StopSmoothTimer();
            StopHideTimer();

            var key = _serverKey;
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideTimer.Tick += (_, _) =>
            {
                StopHideTimer();
                if (!string.IsNullOrEmpty(key))
                    RefreshDebugLog.ProgressHidden(key);
                FadeTo(0, TimeSpan.FromMilliseconds(420), () =>
                {
                    _root.Visibility = Visibility.Collapsed;
                    ResetState();
                });
            };
            _hideTimer.Start();
        }

        private void ResetState()
        {
            _serverKey = null;
            _targetPercent = 0;
            _displayPercent = 0;
            _isTerminalState = false;
            ResetStatusColor();
        }

        private void ResetStatusColor()
        {
            _statusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));
        }

        private void EnsureSmoothTimer()
        {
            if (_smoothTimer != null || _isTerminalState)
                return;

            _smoothTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _smoothTimer.Tick += (_, _) =>
            {
                if (_isTerminalState)
                {
                    StopSmoothTimer();
                    return;
                }

                var cap = 99;
                if (_displayPercent < _targetPercent)
                {
                    var step = Math.Max(1, (_targetPercent - _displayPercent + 3) / 4);
                    _displayPercent = Math.Min(_targetPercent, _displayPercent + step);
                    if (_textThrottle.ShouldUpdate())
                        ApplyStatusText($"Güncelleniyor %{Math.Min(_displayPercent, cap)}");
                    UpdateProgressFillWidth();
                }
                else if (_displayPercent >= _targetPercent)
                {
                    StopSmoothTimer();
                }
            };
            _smoothTimer.Start();
        }

        private void StopSmoothTimer()
        {
            if (_smoothTimer == null)
                return;
            _smoothTimer.Stop();
            _smoothTimer = null;
        }

        private void StopHideTimer()
        {
            if (_hideTimer == null)
                return;
            _hideTimer.Stop();
            _hideTimer = null;
        }

        private void UpdateProgressFillWidth()
        {
            var trackWidth = _progressTrack.ActualWidth;
            if (trackWidth <= 0 || double.IsNaN(trackWidth))
                return;

            _progressFill.Width = Math.Max(0, trackWidth * (_displayPercent / 100.0));
        }

        private void ApplyStatusText(string text)
        {
            if (!string.Equals(_statusText.Text, text, StringComparison.Ordinal))
                _statusText.Text = text;
        }

        private void FadeTo(double opacity, TimeSpan duration, Action? onCompleted = null)
        {
            var animation = new DoubleAnimation
            {
                To = opacity,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            if (onCompleted != null)
                animation.Completed += (_, _) => onCompleted();

            _root.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private bool IsActive(string serverKey) =>
            !string.IsNullOrEmpty(_serverKey)
            && string.Equals(_serverKey, serverKey, StringComparison.OrdinalIgnoreCase);
    }
}
