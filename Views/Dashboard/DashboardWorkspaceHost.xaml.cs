using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Netvium.Helpers;
using Netvium.ViewModels.Dashboard;

namespace Netvium.Views.Dashboard
{
    public partial class DashboardWorkspaceHost : System.Windows.Controls.UserControl
    {
        private DashboardBuilderController? _controller;
        private DashboardWorkspaceViewModel? _vm;
        private readonly Dictionary<string, DashboardWidgetFrame> _frames = new();
        private readonly DispatcherTimer _reorderTimer;
        private readonly Dictionary<string, LayoutSnapshot> _dragSnapshot = new();
        private DashboardWidgetFrame? _activeDragFrame;
        private DashboardWidgetFrame? _activeResizeFrame;
        private GridCandidate? _currentDrop;
        private GridCandidate? _pendingDrop;
        private GridCandidate? _lastResizeCandidate;
        private DateTime _pendingDropSinceUtc;
        private const int ReorderStabilityMs = 150;
        private const int MaxEmptyRows = 1;

        public DashboardWorkspaceHost()
        {
            InitializeComponent();
            _reorderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _reorderTimer.Tick += (_, _) => ApplyPendingDropIfStable();
            SizeChanged += (_, _) => RelayoutAll();
        }

        public void Attach(DashboardBuilderController controller, DashboardWorkspaceViewModel vm)
        {
            _controller = controller;
            _vm = vm;
        }

        public void ClearWidgets()
        {
            WidgetLayer.Children.Clear();
            _frames.Clear();
        }

        public void AddWidgetFrame(DashboardWidgetFrame frame)
        {
            frame.DragStarted += OnFrameDragStarted;
            frame.DragCancelled += OnFrameDragCancelled;
            frame.ResizeStarted += OnFrameResizeStarted;
            frame.ResizeCancelled += OnFrameResizeCancelled;
            frame.LayoutPreviewChanged += OnFrameLayoutPreviewChanged;
            frame.LayoutChanged += OnFrameLayoutChanged;
            WidgetLayer.Children.Add(frame);
            _frames[frame.Widget.Id] = frame;
        }

        public void SetEditMode(bool enabled, bool showGrid)
        {
            GridGuideLayer.Visibility = showGrid ? Visibility.Visible : Visibility.Collapsed;
            foreach (var frame in _frames.Values)
                frame.SetEditMode(enabled);
            if (showGrid)
                DrawGridGuides();
        }

        public void RelayoutAll()
        {
            if (_vm == null || ActualWidth <= 0)
                return;

            var colWidth = GetColumnWidth();
            var rowUnit = GetRowUnit();
            var colGap = DashboardWidgetRegistry.ColumnGap;
            var rowGap = DashboardWidgetRegistry.RowGap;
            UpdateWorkspaceHeight(rowUnit, rowGap);

            foreach (var widget in _vm.Widgets.OrderBy(w => w.DisplayIndex))
            {
                if (!_frames.TryGetValue(widget.Id, out var frame))
                    continue;

                if (ReferenceEquals(frame, _activeDragFrame) || ReferenceEquals(frame, _activeResizeFrame))
                    continue;

                var left = widget.X * (colWidth + colGap);
                var top = widget.Y * (rowUnit + rowGap);
                var width = widget.W * colWidth + (widget.W - 1) * colGap;
                var height = widget.H * rowUnit + (widget.H - 1) * rowGap;

                AnimateFrame(frame, left, top, width, height);
            }

            if (GridGuideLayer.Visibility == Visibility.Visible)
                DrawGridGuides();
        }

        private void OnFrameLayoutChanged(DashboardWidgetFrame frame)
        {
            if (_controller == null)
                return;

            if (ReferenceEquals(_activeResizeFrame, frame))
            {
                var resizeCandidate = CalculateCandidate(frame, forceFromPointer: true);
                if (resizeCandidate == null)
                    return;

                _controller.ReflowWidget(frame.Widget.Id, resizeCandidate.Value.X, resizeCandidate.Value.Y, resizeCandidate.Value.W, resizeCandidate.Value.H, persist: true);
                HideDropPlaceholder();
                System.Windows.Controls.Panel.SetZIndex(frame, 0);
                _activeResizeFrame = null;
                _lastResizeCandidate = null;
                _dragSnapshot.Clear();
                RelayoutAll();
                return;
            }

            var candidate = _pendingDrop ?? _currentDrop ?? CalculateCandidate(frame, forceFromPointer: true);
            if (candidate == null)
                return;

            _activeDragFrame = frame;
            _controller.ReflowWidget(frame.Widget.Id, candidate.Value.X, candidate.Value.Y, candidate.Value.W, candidate.Value.H, persist: true);
            HideDropPlaceholder();
            System.Windows.Controls.Panel.SetZIndex(frame, 0);
            _activeDragFrame = null;
            _currentDrop = null;
            _pendingDrop = null;
            _reorderTimer.Stop();
            _dragSnapshot.Clear();
            RelayoutAll();
        }

        private void OnFrameLayoutPreviewChanged(DashboardWidgetFrame frame)
        {
            if (_activeDragFrame == null)
            {
                if (ReferenceEquals(_activeResizeFrame, frame))
                {
                    ApplyStableResizePreview(frame);
                    return;
                }

                ApplyFrameLayout(frame, persist: false);
                return;
            }

            ApplyStableDragPreview(frame);
        }

        private void OnFrameDragStarted(DashboardWidgetFrame frame)
        {
            if (_vm == null)
                return;

            _dragSnapshot.Clear();
            foreach (var widget in _vm.Widgets)
                _dragSnapshot[widget.Id] = new LayoutSnapshot(widget.X, widget.Y, widget.W, widget.H);

            _activeDragFrame = frame;
            _currentDrop = new GridCandidate(frame.Widget.X, frame.Widget.Y, frame.Widget.W, frame.Widget.H);
            _pendingDrop = null;
            System.Windows.Controls.Panel.SetZIndex(frame, 1000);
            ShowDropPlaceholder(frame.Widget);
        }

        private void OnFrameDragCancelled(DashboardWidgetFrame frame)
        {
            if (_vm != null)
            {
                foreach (var widget in _vm.Widgets)
                {
                    if (!_dragSnapshot.TryGetValue(widget.Id, out var snapshot))
                        continue;
                    widget.X = snapshot.X;
                    widget.Y = snapshot.Y;
                    widget.W = snapshot.W;
                    widget.H = snapshot.H;
                }
            }

            _activeDragFrame = null;
            _currentDrop = null;
            _pendingDrop = null;
            _reorderTimer.Stop();
            _dragSnapshot.Clear();
            HideDropPlaceholder();
            System.Windows.Controls.Panel.SetZIndex(frame, 0);
            RelayoutAll();
        }

        private void OnFrameResizeStarted(DashboardWidgetFrame frame)
        {
            if (_vm == null)
                return;

            _dragSnapshot.Clear();
            foreach (var widget in _vm.Widgets)
                _dragSnapshot[widget.Id] = new LayoutSnapshot(widget.X, widget.Y, widget.W, widget.H);

            _activeResizeFrame = frame;
            _lastResizeCandidate = new GridCandidate(frame.Widget.X, frame.Widget.Y, frame.Widget.W, frame.Widget.H);
            System.Windows.Controls.Panel.SetZIndex(frame, 1000);
            ShowDropPlaceholder(frame.Widget);
        }

        private void OnFrameResizeCancelled(DashboardWidgetFrame frame)
        {
            RestoreSnapshot();
            _activeResizeFrame = null;
            _lastResizeCandidate = null;
            HideDropPlaceholder();
            System.Windows.Controls.Panel.SetZIndex(frame, 0);
            RelayoutAll();
        }

        private void ApplyFrameLayout(DashboardWidgetFrame frame, bool persist)
        {
            if (_controller == null)
                return;

            var colWidth = GetColumnWidth();
            var rowUnit = GetRowUnit();
            var colGap = DashboardWidgetRegistry.ColumnGap;
            var rowGap = DashboardWidgetRegistry.RowGap;
            var left = Canvas.GetLeft(frame);
            var top = Canvas.GetTop(frame);
            if (double.IsNaN(left))
                left = 0;
            if (double.IsNaN(top))
                top = 0;
            var x = (int)Math.Round(left / (colWidth + colGap));
            var y = ClampDropRow((int)Math.Round(top / (rowUnit + rowGap)));

            var widthPx = frame.ActualWidth > 0 ? frame.ActualWidth : frame.Width;
            var heightPx = frame.ActualHeight > 0 ? frame.ActualHeight : frame.Height;
            var w = (int)Math.Max(frame.Widget.Model.MinW, Math.Round((widthPx + colGap) / (colWidth + colGap)));
            var h = (int)Math.Max(frame.Widget.Model.MinH, Math.Round((heightPx + rowGap) / (rowUnit + rowGap)));

            Debug.WriteLine($"[DashboardDrag] drag over grid: {frame.Widget.Title} x={x} y={y} w={w} h={h} persist={persist}");
            _controller.ReflowWidget(frame.Widget.Id, x, y, w, h, persist);
            ShowDropPlaceholder(frame.Widget);
            RelayoutAll();
        }

        private void ApplyStableDragPreview(DashboardWidgetFrame frame)
        {
            if (_controller == null)
                return;

            var candidate = CalculateCandidate(frame, forceFromPointer: false);
            if (candidate == null)
                return;

            if (_currentDrop.HasValue && candidate.Value.Equals(_currentDrop.Value))
            {
                _pendingDrop = null;
                _reorderTimer.Stop();
                ShowDropPlaceholder(frame.Widget);
                return;
            }

            if (!_pendingDrop.HasValue || !candidate.Value.Equals(_pendingDrop.Value))
            {
                _pendingDrop = candidate;
                _pendingDropSinceUtc = DateTime.UtcNow;
                _reorderTimer.Start();
                Debug.WriteLine($"[DashboardDrag] pending drop: {frame.Widget.Title} x={candidate.Value.X} y={candidate.Value.Y}");
                return;
            }

            ApplyPendingDropIfStable();
        }

        private void ApplyStableResizePreview(DashboardWidgetFrame frame)
        {
            if (_controller == null)
                return;

            var candidate = CalculateCandidate(frame, forceFromPointer: true);
            if (candidate == null)
                return;

            ShowDropPlaceholderCandidate(candidate.Value);
            if (_lastResizeCandidate.HasValue && candidate.Value.Equals(_lastResizeCandidate.Value))
                return;

            _lastResizeCandidate = candidate;
            Debug.WriteLine($"[DashboardResize] snapped resize: {frame.Widget.Title} x={candidate.Value.X} y={candidate.Value.Y} w={candidate.Value.W} h={candidate.Value.H}");
            _controller.ReflowWidget(frame.Widget.Id, candidate.Value.X, candidate.Value.Y, candidate.Value.W, candidate.Value.H, persist: false);
            RelayoutAll();
        }

        private void ApplyPendingDropIfStable()
        {
            if (_controller == null || _activeDragFrame == null || !_pendingDrop.HasValue)
                return;

            if ((DateTime.UtcNow - _pendingDropSinceUtc).TotalMilliseconds < ReorderStabilityMs)
                return;

            var candidate = _pendingDrop.Value;
            _pendingDrop = null;
            _currentDrop = candidate;
            _reorderTimer.Stop();
            Debug.WriteLine($"[DashboardDrag] stable reflow: {_activeDragFrame.Widget.Title} x={candidate.X} y={candidate.Y}");
            _controller.ReflowWidget(_activeDragFrame.Widget.Id, candidate.X, candidate.Y, candidate.W, candidate.H, persist: false);
            ShowDropPlaceholder(_activeDragFrame.Widget);
            RelayoutAll();
        }

        private GridCandidate? CalculateCandidate(DashboardWidgetFrame frame, bool forceFromPointer)
        {
            var colWidth = GetColumnWidth();
            var rowUnit = GetRowUnit();
            var colGap = DashboardWidgetRegistry.ColumnGap;
            var rowGap = DashboardWidgetRegistry.RowGap;
            var colStep = colWidth + colGap;
            var rowStep = rowUnit + rowGap;
            var left = Canvas.GetLeft(frame);
            var top = Canvas.GetTop(frame);
            if (double.IsNaN(left) || double.IsInfinity(left))
                left = frame.Widget.X * colStep;
            if (double.IsNaN(top) || double.IsInfinity(top))
                top = frame.Widget.Y * rowStep;

            var widthPx = frame.ActualWidth > 0 ? frame.ActualWidth : frame.Width;
            var heightPx = frame.ActualHeight > 0 ? frame.ActualHeight : frame.Height;
            var w = (int)Math.Max(frame.Widget.Model.MinW, Math.Round((widthPx + colGap) / colStep));
            var h = (int)Math.Max(frame.Widget.Model.MinH, Math.Round((heightPx + rowGap) / rowStep));
            w = Math.Clamp(w, frame.Widget.Model.MinW, frame.Widget.Model.MaxW ?? DashboardWidgetRegistry.ColumnCount);
            w = Math.Min(w, DashboardWidgetRegistry.ColumnCount);
            h = Math.Max(frame.Widget.Model.MinH, h);

            if (forceFromPointer || !_currentDrop.HasValue)
            {
                var x = Math.Clamp((int)Math.Round(left / colStep), 0, DashboardWidgetRegistry.ColumnCount - w);
                var y = ClampDropRow((int)Math.Round(top / rowStep));
                return new GridCandidate(x, y, w, h);
            }

            var current = _currentDrop.Value;
            var threshold = frame.Widget.W <= 2 && frame.Widget.H <= 2 ? 0.60 : 0.50;
            var nextX = AdvanceWithHysteresis(current.X, left, colStep, threshold);
            var nextY = AdvanceWithHysteresis(current.Y, top, rowStep, threshold);
            nextX = Math.Clamp(nextX, 0, DashboardWidgetRegistry.ColumnCount - w);
            nextY = ClampDropRow(nextY);

            return new GridCandidate(nextX, nextY, w, h);
        }

        private static int AdvanceWithHysteresis(int current, double pixel, double step, double threshold)
        {
            var next = current;
            var offset = pixel - current * step;
            while (offset > step * threshold)
            {
                next++;
                offset -= step;
            }
            while (offset < -step * threshold)
            {
                next--;
                offset += step;
            }
            return next;
        }

        private double GetColumnWidth()
        {
            var gaps = (DashboardWidgetRegistry.ColumnCount - 1) * DashboardWidgetRegistry.ColumnGap;
            return Math.Max(24, (ActualWidth - gaps) / DashboardWidgetRegistry.ColumnCount);
        }

        private double GetRowUnit() => DashboardWidgetRegistry.RowUnitHeight;

        private int GetOccupiedRowCount()
        {
            if (_vm == null)
                return 0;

            var max = 0;
            foreach (var widget in _vm.Widgets.Where(w => w.IsVisible && !w.IsCollapsed))
                max = Math.Max(max, widget.Y + widget.H);
            return max;
        }

        private int ClampDropRow(int y)
        {
            var occupiedRows = GetOccupiedRowCount();
            var visibleRows = GetVisibleRowCapacity();
            var maxDropRow = Math.Max(0, Math.Min(occupiedRows + MaxEmptyRows, visibleRows + MaxEmptyRows));
            return Math.Clamp(y, 0, maxDropRow);
        }

        private int GetVisibleRowCapacity()
        {
            var rowStep = DashboardWidgetRegistry.RowUnitHeight + DashboardWidgetRegistry.RowGap;
            var viewportHeight = FindParentScrollViewer()?.ViewportHeight ?? 0;
            if (viewportHeight <= 0 && Parent is FrameworkElement parent && parent.ActualHeight > 0)
                viewportHeight = parent.ActualHeight;
            if (viewportHeight <= 0)
                viewportHeight = ActualHeight;

            if (viewportHeight <= 0 || rowStep <= 0)
                return GetOccupiedRowCount() + MaxEmptyRows;

            return Math.Max(1, (int)Math.Ceiling(viewportHeight / rowStep));
        }

        private ScrollViewer? FindParentScrollViewer()
        {
            DependencyObject? current = this;
            while (current != null)
            {
                if (current is ScrollViewer scrollViewer)
                    return scrollViewer;
                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void UpdateWorkspaceHeight(double rowUnit, double rowGap)
        {
            var rows = Math.Max(1, GetOccupiedRowCount());
            var visibleRows = GetVisibleRowCapacity();
            rows = Math.Min(rows + MaxEmptyRows, Math.Max(rows, visibleRows + MaxEmptyRows));
            var height = rows * rowUnit + Math.Max(0, rows - 1) * rowGap;
            Height = height;
            GridCanvas.Height = height;
            GridGuideLayer.Height = height;
            WidgetLayer.Height = height;
        }

        private void DrawGridGuides()
        {
            GridGuideLayer.Children.Clear();
            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var colWidth = GetColumnWidth();
            var rowUnit = GetRowUnit();
            var colGap = DashboardWidgetRegistry.ColumnGap;
            var rowGap = DashboardWidgetRegistry.RowGap;
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(28, 79, 140, 255));

            for (var c = 0; c <= DashboardWidgetRegistry.ColumnCount; c++)
            {
                var x = c * (colWidth + colGap);
                GridGuideLayer.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = ActualHeight,
                    Stroke = brush,
                    StrokeThickness = 1
                });
            }

            var rows = Math.Max(1, GetOccupiedRowCount() + MaxEmptyRows);
            for (var r = 0; r <= rows; r++)
            {
                var y = r * (rowUnit + rowGap);
                GridGuideLayer.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = ActualWidth,
                    Y1 = y,
                    Y2 = y,
                    Stroke = brush,
                    StrokeThickness = 1
                });
            }
        }

        private void ShowDropPlaceholder(DashboardWidgetViewModel widget)
        {
            if (ActualWidth <= 0)
                return;

            var colWidth = GetColumnWidth();
            var rowUnit = GetRowUnit();
            var colGap = DashboardWidgetRegistry.ColumnGap;
            var rowGap = DashboardWidgetRegistry.RowGap;
            var left = widget.X * (colWidth + colGap);
            var top = widget.Y * (rowUnit + rowGap);
            var width = widget.W * colWidth + (widget.W - 1) * colGap;
            var height = widget.H * rowUnit + (widget.H - 1) * rowGap;

            DropPlaceholder.Visibility = Visibility.Visible;
            AnimateElement(DropPlaceholder, left, top, width, height);
        }

        private void ShowDropPlaceholderCandidate(GridCandidate candidate)
        {
            if (ActualWidth <= 0)
                return;

            var colWidth = GetColumnWidth();
            var rowUnit = GetRowUnit();
            var colGap = DashboardWidgetRegistry.ColumnGap;
            var rowGap = DashboardWidgetRegistry.RowGap;
            var left = candidate.X * (colWidth + colGap);
            var top = candidate.Y * (rowUnit + rowGap);
            var width = candidate.W * colWidth + (candidate.W - 1) * colGap;
            var height = candidate.H * rowUnit + (candidate.H - 1) * rowGap;

            DropPlaceholder.Visibility = Visibility.Visible;
            AnimateElement(DropPlaceholder, left, top, width, height);
        }

        private void HideDropPlaceholder() => DropPlaceholder.Visibility = Visibility.Collapsed;

        private void RestoreSnapshot()
        {
            if (_vm == null)
                return;

            foreach (var widget in _vm.Widgets)
            {
                if (!_dragSnapshot.TryGetValue(widget.Id, out var snapshot))
                    continue;
                widget.X = snapshot.X;
                widget.Y = snapshot.Y;
                widget.W = snapshot.W;
                widget.H = snapshot.H;
            }

            _dragSnapshot.Clear();
        }

        private static void AnimateFrame(FrameworkElement element, double left, double top, double width, double height)
        {
            AnimateElement(element, left, top, width, height);
        }

        private static void AnimateElement(FrameworkElement element, double left, double top, double width, double height)
        {
            if (double.IsNaN(left) || double.IsInfinity(left)
                || double.IsNaN(top) || double.IsInfinity(top)
                || double.IsNaN(width) || double.IsInfinity(width)
                || double.IsNaN(height) || double.IsInfinity(height))
                return;

            var currentLeft = Canvas.GetLeft(element);
            var currentTop = Canvas.GetTop(element);
            var currentWidth = element.Width;
            var currentHeight = element.Height;

            var canAnimate =
                !double.IsNaN(currentLeft) && !double.IsInfinity(currentLeft) &&
                !double.IsNaN(currentTop) && !double.IsInfinity(currentTop) &&
                !double.IsNaN(currentWidth) && !double.IsInfinity(currentWidth) && currentWidth > 0 &&
                !double.IsNaN(currentHeight) && !double.IsInfinity(currentHeight) && currentHeight > 0 &&
                element.IsLoaded;

            // Keep the dependency property base values valid. WPF cannot animate
            // Canvas.Left/Top when the underlying value is still NaN.
            element.BeginAnimation(Canvas.LeftProperty, null);
            element.BeginAnimation(Canvas.TopProperty, null);
            element.BeginAnimation(WidthProperty, null);
            element.BeginAnimation(HeightProperty, null);
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            element.Width = width;
            element.Height = height;

            if (!canAnimate)
                return;

            var duration = TimeSpan.FromMilliseconds(180);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(currentLeft, left, duration) { EasingFunction = easing });
            element.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(currentTop, top, duration) { EasingFunction = easing });
            element.BeginAnimation(WidthProperty, new DoubleAnimation(currentWidth, width, duration) { EasingFunction = easing });
            element.BeginAnimation(HeightProperty, new DoubleAnimation(currentHeight, height, duration) { EasingFunction = easing });
        }

        private readonly struct GridCandidate : IEquatable<GridCandidate>
        {
            public GridCandidate(int x, int y, int w, int h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public int X { get; }
            public int Y { get; }
            public int W { get; }
            public int H { get; }

            public bool Equals(GridCandidate other) => X == other.X && Y == other.Y && W == other.W && H == other.H;
            public override bool Equals(object? obj) => obj is GridCandidate other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y, W, H);
        }

        private readonly struct LayoutSnapshot
        {
            public LayoutSnapshot(int x, int y, int w, int h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public int X { get; }
            public int Y { get; }
            public int W { get; }
            public int H { get; }
        }
    }
}
