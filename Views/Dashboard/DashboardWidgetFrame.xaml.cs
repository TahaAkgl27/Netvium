using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Netvium.Helpers;
using Netvium.ViewModels.Dashboard;

namespace Netvium.Views.Dashboard
{
    public partial class DashboardWidgetFrame : System.Windows.Controls.UserControl
    {
        private FrameworkElement? _content;
        private const double DragStartThreshold = 10.0;
        private bool _isEditMode;
        private bool _isDragging;
        private bool _isPointerDown;
        private bool _isResizing;
        private bool _isSelected;
        private System.Windows.Point _dragStart;
        private System.Windows.Point _lastDragPosition;
        private string _resizeDirection = string.Empty;

        public DashboardWidgetViewModel Widget { get; }

        public event Action<DashboardWidgetFrame>? SettingsRequested;
        public event Action<DashboardWidgetFrame>? DuplicateRequested;
        public event Action<DashboardWidgetFrame>? RemoveRequested;
        public event Action<DashboardWidgetFrame>? ExpandRequested;
        public event Action<DashboardWidgetFrame>? DragStarted;
        public event Action<DashboardWidgetFrame>? DragCancelled;
        public event Action<DashboardWidgetFrame>? ResizeStarted;
        public event Action<DashboardWidgetFrame>? ResizeCancelled;
        public event Action<DashboardWidgetFrame>? LayoutPreviewChanged;
        public event Action<DashboardWidgetFrame>? LayoutChanged;

        public DashboardWidgetFrame(DashboardWidgetViewModel widget, FrameworkElement content)
        {
            InitializeComponent();
            Widget = widget;
            AttachContent(content);
            ApplySettings();
        }

        public void AttachContent(FrameworkElement content)
        {
            _content = content;
            ContentHost.Children.Clear();
            ContentHost.Children.Add(content);
        }

        public void ApplySettings()
        {
            TitleText.Text = Widget.Title;
            TitleIcon.Text = string.IsNullOrWhiteSpace(Widget.Settings.Icon) ? "\uE9D2" : Widget.Settings.Icon;
            HeaderRow.Height = Widget.Settings.ShowHeader ? new GridLength(30) : new GridLength(0);
            HeaderBar.Visibility = Widget.Settings.ShowHeader ? Visibility.Visible : Visibility.Collapsed;
            ChromeRoot.BorderThickness = Widget.Settings.ShowBorder ? new Thickness(1) : new Thickness(0);
            Opacity = Math.Clamp(Widget.Settings.Transparency, 0.35, 1.0);

            if (!string.IsNullOrWhiteSpace(Widget.Settings.AccentColor))
            {
                try
                {
                    var brush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(Widget.Settings.AccentColor)!;
                    TitleIcon.Foreground = brush;
                }
                catch
                {
                }
            }
        }

        public void SetEditMode(bool enabled)
        {
            _isEditMode = enabled;
            EditActionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeHandlesLayer.Visibility = enabled && !Widget.IsLocked ? Visibility.Visible : Visibility.Collapsed;
            EditOutline.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            DragGlyph.Visibility = enabled && !Widget.IsLocked ? Visibility.Visible : Visibility.Collapsed;
            HeaderRow.Height = Widget.Settings.ShowHeader
                ? new GridLength(enabled ? 44 : 30)
                : new GridLength(0);
            HeaderBar.Cursor = enabled && !Widget.IsLocked ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;

            if (enabled)
            {
                ChromeRoot.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0x4F, 0x8C, 0xFF));
                ChromeRoot.BorderThickness = new Thickness(1);
                HeaderBar.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x22, 0x36));
                HeaderBar.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x31, 0x42, 0x5F));
            }
            else
            {
                _isSelected = false;
                ChromeRoot.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x2A, 0x3F));
                ChromeRoot.BorderThickness = Widget.Settings.ShowBorder ? new Thickness(1) : new Thickness(0);
                ChromeRoot.Effect = null;
                HeaderBar.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x14, 0x1D, 0x30));
                HeaderBar.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x2A, 0x3F));
            }
        }

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsActionButtonSource(e.OriginalSource as DependencyObject))
                return;

            Debug.WriteLine($"[DashboardDrag] header down: {Widget.Title}");

            if (e.ClickCount == 2)
            {
                Debug.WriteLine($"[DashboardDrag] header double click expand: {Widget.Title}");
                ExpandRequested?.Invoke(this);
                e.Handled = true;
                return;
            }

            if (!_isEditMode || Widget.IsLocked)
                return;

            _isPointerDown = true;
            _isDragging = false;
            _dragStart = e.GetPosition(Parent as UIElement);
            _lastDragPosition = _dragStart;
            HeaderBar.CaptureMouse();
            e.Handled = true;
        }

        private void HeaderBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isPointerDown || e.LeftButton != MouseButtonState.Pressed)
                return;

            var parent = Parent as UIElement;
            if (parent == null)
                return;

            var pos = e.GetPosition(parent);
            if (!_isDragging)
            {
                var totalDx = pos.X - _dragStart.X;
                var totalDy = pos.Y - _dragStart.Y;
                if (Math.Sqrt(totalDx * totalDx + totalDy * totalDy) < DragStartThreshold)
                    return;

                _isDragging = true;
                Debug.WriteLine($"[DashboardDrag] drag start: {Widget.Title}");
                Focus();
                DragStarted?.Invoke(this);
                BeginAnimation(Canvas.LeftProperty, null);
                BeginAnimation(Canvas.TopProperty, null);
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                HeaderBar.Cursor = System.Windows.Input.Cursors.SizeAll;
                ApplyDragVisualState();
            }

            var dx = pos.X - _lastDragPosition.X;
            var dy = pos.Y - _lastDragPosition.Y;
            var left = Canvas.GetLeft(this);
            var top = Canvas.GetTop(this);
            if (double.IsNaN(left) || double.IsInfinity(left))
                left = 0;
            if (double.IsNaN(top) || double.IsInfinity(top))
                top = 0;
            Canvas.SetLeft(this, Math.Max(0, left + dx));
            Canvas.SetTop(this, Math.Max(0, top + dy));
            _lastDragPosition = pos;
            Debug.WriteLine($"[DashboardDrag] drag move: {Widget.Title} x={Canvas.GetLeft(this):0} y={Canvas.GetTop(this):0}");
            LayoutPreviewChanged?.Invoke(this);
        }

        private void HeaderBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPointerDown)
                return;

            var didDrag = _isDragging;
            _isDragging = false;
            _isPointerDown = false;
            HeaderBar.ReleaseMouseCapture();
            HeaderBar.Cursor = _isEditMode && !Widget.IsLocked
                ? System.Windows.Input.Cursors.Hand
                : System.Windows.Input.Cursors.Arrow;

            if (didDrag)
            {
                Debug.WriteLine($"[DashboardDrag] drop: {Widget.Title}");
                LayoutChanged?.Invoke(this);
                ApplyHoverVisualState(isHovering: HeaderBar.IsMouseOver);
            }
            else
            {
                _isSelected = true;
                ApplySelectedVisualState();
            }
            e.Handled = true;
        }

        private void OnResizeDragStarted(object sender, DragStartedEventArgs e)
        {
            if (!_isEditMode || Widget.IsLocked || sender is not Thumb thumb)
                return;

            _isResizing = true;
            _resizeDirection = thumb.Tag as string ?? "SE";
            Focus();
            BeginAnimation(Canvas.LeftProperty, null);
            BeginAnimation(Canvas.TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            System.Windows.Controls.Panel.SetZIndex(this, 1000);
            ResizeStarted?.Invoke(this);
            ApplyDragVisualState();
            Debug.WriteLine($"[DashboardResize] resize start: {Widget.Title} dir={_resizeDirection}");
        }

        private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_isResizing || ResizeHandlesLayer.Visibility != Visibility.Visible || Widget.IsLocked)
                return;

            var left = Canvas.GetLeft(this);
            var top = Canvas.GetTop(this);
            if (double.IsNaN(left) || double.IsInfinity(left))
                left = 0;
            if (double.IsNaN(top) || double.IsInfinity(top))
                top = 0;
            var width = double.IsNaN(Width) || double.IsInfinity(Width) ? ActualWidth : Width;
            var height = double.IsNaN(Height) || double.IsInfinity(Height) ? ActualHeight : Height;

            if (_resizeDirection.Contains("E", StringComparison.OrdinalIgnoreCase))
                width += e.HorizontalChange;
            if (_resizeDirection.Contains("S", StringComparison.OrdinalIgnoreCase))
                height += e.VerticalChange;
            if (_resizeDirection.Contains("W", StringComparison.OrdinalIgnoreCase))
            {
                left += e.HorizontalChange;
                width -= e.HorizontalChange;
            }
            if (_resizeDirection.Contains("N", StringComparison.OrdinalIgnoreCase))
            {
                top += e.VerticalChange;
                height -= e.VerticalChange;
            }

            Width = Math.Max(64, width);
            Height = Math.Max(64, height);
            Canvas.SetLeft(this, Math.Max(0, left));
            Canvas.SetTop(this, Math.Max(0, top));
            Debug.WriteLine($"[DashboardResize] resize move: {Widget.Title} dir={_resizeDirection} w={Width:0} h={Height:0}");
            LayoutPreviewChanged?.Invoke(this);
        }

        private void OnResizeDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (!_isResizing)
                return;

            _isResizing = false;
            _resizeDirection = string.Empty;
            Opacity = 1.0;
            System.Windows.Controls.Panel.SetZIndex(this, 0);
            Debug.WriteLine($"[DashboardResize] resize drop: {Widget.Title}");
            LayoutChanged?.Invoke(this);
            ApplyHoverVisualState(isHovering: HeaderBar.IsMouseOver);
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            SettingsRequested?.Invoke(this);
        }
        private void OnDuplicateClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            DuplicateRequested?.Invoke(this);
        }
        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            RemoveRequested?.Invoke(this);
        }
        private void OnExpandClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ExpandRequested?.Invoke(this);
        }

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Widget.IsPinned = !Widget.IsPinned;
            PinBtn.Foreground = Widget.IsPinned
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x7A, 0x94));
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            HeaderBar.MouseLeftButtonDown += HeaderBar_MouseLeftButtonDown;
            HeaderBar.MouseMove += HeaderBar_MouseMove;
            HeaderBar.MouseLeftButtonUp += HeaderBar_MouseLeftButtonUp;
            HeaderBar.MouseEnter += (_, _) => ApplyHoverVisualState(isHovering: true);
            HeaderBar.MouseLeave += (_, _) => ApplyHoverVisualState(isHovering: false);
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key != Key.Escape || (!_isDragging && !_isPointerDown && !_isResizing))
                return;

            Debug.WriteLine($"[DashboardDrag] cancel: {Widget.Title}");
            _isDragging = false;
            _isPointerDown = false;
            var wasResizing = _isResizing;
            _isResizing = false;
            _resizeDirection = string.Empty;
            if (HeaderBar.IsMouseCaptured)
                HeaderBar.ReleaseMouseCapture();
            HeaderBar.Cursor = _isEditMode && !Widget.IsLocked
                ? System.Windows.Input.Cursors.Hand
                : System.Windows.Input.Cursors.Arrow;
            Opacity = 1.0;
            if (wasResizing)
                ResizeCancelled?.Invoke(this);
            else
                DragCancelled?.Invoke(this);
            ApplyHoverVisualState(isHovering: HeaderBar.IsMouseOver);
            e.Handled = true;
        }

        private void ApplyHoverVisualState(bool isHovering)
        {
            if (!_isEditMode || Widget.IsLocked || _isDragging)
                return;

            Opacity = 1.0;
            HeaderBar.Background = new SolidColorBrush(isHovering
                ? System.Windows.Media.Color.FromRgb(0x1A, 0x2A, 0x44)
                : System.Windows.Media.Color.FromRgb(0x16, 0x22, 0x36));
            HeaderBar.BorderBrush = new SolidColorBrush(isHovering
                ? System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF)
                : System.Windows.Media.Color.FromRgb(0x31, 0x42, 0x5F));
            ChromeRoot.Effect = isHovering
                ? new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.28,
                    Color = System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF)
                }
                : (_isSelected
                    ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.22, Color = System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF) }
                    : null);
        }

        private void ApplyDragVisualState()
        {
            ChromeRoot.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF));
            HeaderBar.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1D, 0x31, 0x52));
            Opacity = 0.92;
            ChromeRoot.Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.36,
                Color = System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF)
            };
        }

        private void ApplySelectedVisualState()
        {
            Opacity = 1.0;
            ChromeRoot.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF));
            ChromeRoot.BorderThickness = new Thickness(1.5);
            ChromeRoot.Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.22,
                Color = System.Windows.Media.Color.FromRgb(0x4F, 0x8C, 0xFF)
            };
        }

        private static bool IsActionButtonSource(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is System.Windows.Controls.Button)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }
    }
}
