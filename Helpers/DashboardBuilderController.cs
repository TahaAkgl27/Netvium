using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Netvium.Models;
using Netvium.Helpers;
using Netvium.Models.Dashboard;
using Netvium.Services;
using Netvium.ViewModels.Dashboard;
using Netvium.Views.Dashboard;
using Netvium.Helpers.Logging;

namespace Netvium.Helpers
{
    public sealed class DashboardBuilderController
    {
        private readonly MainWindow _window;
        private readonly DashboardLayoutService _layoutService = new();
        private readonly Dictionary<string, FrameworkElement> _panelHosts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DashboardWidgetFrame> _frames = new(StringComparer.OrdinalIgnoreCase);

        private DashboardWorkspaceHost? _host;
        private DashboardWorkspaceViewModel? _vm;
        private string? _currentServerKey;
        private bool _hostReady;
        private readonly Dictionary<string, (int X, int Y, int W, int H)> _lastWidgetGeometry = new(StringComparer.OrdinalIgnoreCase);

        public DashboardBuilderController(MainWindow window) => _window = window;

        public DashboardWorkspaceViewModel ViewModel => _vm ??= new DashboardWorkspaceViewModel();

        public string? CurrentServerKey => _currentServerKey;

        public void RegisterPanel(string key, FrameworkElement panel) => _panelHosts[key] = panel;

        public void EnsureHostReady(DashboardWorkspaceHost host)
        {
            if (_hostReady)
                return;

            _host = host;
            _vm = ViewModel;
            host.Attach(this, _vm);
            _hostReady = true;
        }

        public void LoadServerDashboard(string serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey) || _host == null)
                return;

            _currentServerKey = serverKey;

            var saved = _layoutService.TryLoadSavedLayout(serverKey);
            var doc = DashboardLayoutService.HasRenderableWidgets(saved)
                ? saved!
                : DashboardWidgetRegistry.CreateDefaultDashboardLayout(serverKey);

            _vm!.LoadDocument(doc);
            _vm.IsEditMode = false;
            _vm.IsLayoutLocked = doc.IsLayoutLocked;

            RenderDashboard();
        }

        public void RenderDashboard() => RebuildWidgetFrames();

        public FrameworkElement? ResolvePanelHost(string panelKey)
        {
            if (_panelHosts.TryGetValue(panelKey, out var panel))
                return panel;

            return null;
        }

        public void RebuildWidgetFrames()
        {
            if (_host == null || _vm == null)
                return;

            DetachRegisteredPanels();

            _frames.Clear();
            _host.ClearWidgets();

            var usedPanelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var widget in _vm.Widgets.Where(w => w.IsVisible && !w.IsCollapsed).OrderBy(w => w.DisplayIndex))
            {
                var def = DashboardWidgetRegistry.Get(widget.Type);
                var panelKey = def?.PanelKey ?? widget.Type;

                FrameworkElement content;
                if (usedPanelKeys.Contains(panelKey))
                {
                    content = CreateUnavailablePanel(panelKey, widget.Title, "This widget is already visible in this workspace.");
                }
                else
                {
                    content = ResolvePanelHost(panelKey) ?? CreateUnavailablePanel(panelKey, widget.Title, "This widget is not available in the production dashboard catalog.");
                    usedPanelKeys.Add(panelKey);
                }

                var frame = new DashboardWidgetFrame(widget, content);
                frame.SettingsRequested += OnWidgetSettingsRequested;
                frame.DuplicateRequested += OnWidgetDuplicateRequested;
                frame.RemoveRequested += OnWidgetRemoveRequested;
                frame.ExpandRequested += OnWidgetExpandRequested;
                frame.LayoutChanged += OnWidgetLayoutChanged;

                _frames[widget.Id] = frame;
                _lastWidgetGeometry[widget.Id] = (widget.X, widget.Y, widget.W, widget.H);
                _host.AddWidgetFrame(frame);
            }

            _host.RelayoutAll();
            ApplyChromeState();
            _window.SyncInfoCompactContent();
        }

        public void ToggleEditMode()
        {
            if (_vm == null || _vm.IsLayoutLocked)
                return;
            _vm.IsEditMode = !_vm.IsEditMode;
            ApplyChromeState();
        }

        public void SetLayoutLocked(bool locked)
        {
            if (_vm == null)
                return;
            _vm.IsLayoutLocked = locked;
            if (locked)
                _vm.IsEditMode = false;
            ApplyChromeState();
            PersistLayout();
        }

        public void SaveLayout()
        {
            WriteLayoutAudit(AuditActions.LayoutSaved, "Dashboard layout saved.");
            PersistLayout();
        }

        public void AutoArrangeLayout()
        {
            CompactLayout();
            RenderDashboard();
            PersistLayout();
        }

        public void ResetLayout()
        {
            if (string.IsNullOrWhiteSpace(_currentServerKey))
                return;

            WriteLayoutAudit(AuditActions.LayoutReset, "Dashboard layout reset requested.");
            _layoutService.DeleteServerLayout(_currentServerKey);
            LoadServerDashboard(_currentServerKey);
            PersistLayout();
        }

        public void AddWidget(string type)
        {
            if (_vm?.ActiveWorkspace == null)
                return;

            var inst = DashboardWidgetRegistry.CreateInstance(type);
            var (x, y) = FindNextFreeSlot(inst.W, inst.H);
            inst.X = x;
            inst.Y = y;

            _vm.ActiveWorkspace.Widgets.Add(inst);
            _vm.Widgets.Add(new DashboardWidgetViewModel(inst));
            WriteWidgetAudit(AuditActions.WidgetAdded, inst.Type, inst.Id, "Widget added.");
            RenderDashboard();
            PersistLayout();
        }

        public void ExportLayout()
        {
            if (_vm == null)
                return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Dashboard Layout (*.json)|*.json",
                FileName = "netvium-dashboard.json"
            };
            if (dlg.ShowDialog() != true)
                return;

            System.IO.File.WriteAllText(dlg.FileName, _layoutService.ExportToJson(_vm.ToDocument()));
        }

        public void ImportLayout()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Dashboard Layout (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true)
                return;

            var doc = _layoutService.ImportFromJson(System.IO.File.ReadAllText(dlg.FileName));
            if (!DashboardLayoutService.HasRenderableWidgets(doc))
                doc = DashboardWidgetRegistry.CreateDefaultDashboardLayout(_currentServerKey);

            _vm?.LoadDocument(doc);
            RenderDashboard();
            PersistLayout();
        }

        public void SwitchWorkspace(string workspaceId)
        {
            if (_vm == null)
                return;
            var ws = _vm.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
            if (ws == null)
                return;
            _vm.SyncActiveWorkspaceWidgets();
            _vm.ActiveWorkspace = ws;

            if (!DashboardLayoutService.HasRenderableWidgets(_vm.ToDocument()))
            {
                var defaultWs = DashboardWidgetRegistry.CreateDefaultDashboardLayout(_currentServerKey).Workspaces[0];
                ws.Widgets = CloneWidgets(defaultWs.Widgets);
                _vm.ReloadWidgetsFromWorkspace();
            }

            RenderDashboard();
            PersistLayout();
        }

        public void CreateWorkspace(string name)
        {
            if (_vm == null)
                return;
            _vm.SyncActiveWorkspaceWidgets();

            var ws = new DashboardWorkspace { Name = name };
            if (!name.Equals("Empty Workspace", StringComparison.OrdinalIgnoreCase))
            {
                var defaultWs = DashboardWidgetRegistry.CreateDefaultDashboardLayout(_currentServerKey).Workspaces[0];
                ws.Widgets = CloneWidgets(defaultWs.Widgets);
            }

            _vm.Workspaces.Add(ws);
            _vm.ActiveWorkspace = ws;
            RenderDashboard();
            PersistLayout();
        }

        public void RenameActiveWorkspace(string name)
        {
            if (_vm?.ActiveWorkspace == null || string.IsNullOrWhiteSpace(name))
                return;
            _vm.ActiveWorkspace.Name = name.Trim();
            PersistLayout();
        }

        public void DuplicateActiveWorkspace()
        {
            if (_vm?.ActiveWorkspace == null)
                return;

            _vm.SyncActiveWorkspaceWidgets();
            var json = System.Text.Json.JsonSerializer.Serialize(_vm.ActiveWorkspace);
            var copy = System.Text.Json.JsonSerializer.Deserialize<DashboardWorkspace>(json)!;
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name += " Copy";
            copy.Widgets = CloneWidgets(copy.Widgets);

            _vm.Workspaces.Add(copy);
            _vm.ActiveWorkspace = copy;
            RenderDashboard();
            PersistLayout();
        }

        public void DeleteActiveWorkspace()
        {
            if (_vm?.ActiveWorkspace == null || _vm.Workspaces.Count <= 1)
                return;

            var current = _vm.ActiveWorkspace;
            _vm.Workspaces.Remove(current);
            _vm.ActiveWorkspace = _vm.Workspaces.First();

            if (!DashboardLayoutService.HasRenderableWidgets(_vm.ToDocument()))
            {
                var defaultWs = DashboardWidgetRegistry.CreateDefaultDashboardLayout(_currentServerKey).Workspaces[0];
                _vm.ActiveWorkspace.Widgets = CloneWidgets(defaultWs.Widgets);
                _vm.ReloadWidgetsFromWorkspace();
            }

            RenderDashboard();
            PersistLayout();
        }

        private void ApplyChromeState()
        {
            var edit = _vm != null && _vm.IsEditMode && _vm.CanEditWidgets;
            _host?.SetEditMode(edit, showGrid: edit);
            foreach (var frame in _frames.Values)
                frame.SetEditMode(edit && !frame.Widget.IsLocked);
        }

        private void PersistLayout()
        {
            if (_vm == null || string.IsNullOrWhiteSpace(_currentServerKey))
                return;
            _vm.SyncActiveWorkspaceWidgets();
            _layoutService.ScheduleAutoSave(_currentServerKey, _vm.ToDocument());
        }

        public Task FlushLayoutAsync()
        {
            if (_vm == null || string.IsNullOrWhiteSpace(_currentServerKey))
                return Task.CompletedTask;

            _vm.SyncActiveWorkspaceWidgets();
            return _layoutService.FlushAsync(_currentServerKey, _vm.ToDocument());
        }

        private void OnWidgetLayoutChanged(DashboardWidgetFrame frame)
        {
            var current = (frame.Widget.X, frame.Widget.Y, frame.Widget.W, frame.Widget.H);
            if (_lastWidgetGeometry.TryGetValue(frame.Widget.Id, out var previous))
            {
                if (previous.X != current.X || previous.Y != current.Y)
                    WriteWidgetAudit(AuditActions.WidgetMoved, frame.Widget.Type, frame.Widget.Id, "Widget moved.",
                        new { from = new { previous.X, previous.Y }, to = new { frame.Widget.X, frame.Widget.Y } });
                if (previous.W != current.W || previous.H != current.H)
                    WriteWidgetAudit(AuditActions.WidgetResized, frame.Widget.Type, frame.Widget.Id, "Widget resized.",
                        new { from = new { previous.W, previous.H }, to = new { frame.Widget.W, frame.Widget.H } });
            }
            _lastWidgetGeometry[frame.Widget.Id] = current;
            _host?.RelayoutAll();
            PersistLayout();
        }

        private void OnWidgetSettingsRequested(DashboardWidgetFrame frame)
        {
            var dialog = new WidgetSettingsDialog(frame.Widget);
            dialog.Owner = _window;
            if (dialog.ShowDialog() == true)
            {
                frame.ApplySettings();
                PersistLayout();
            }
        }

        private void OnWidgetDuplicateRequested(DashboardWidgetFrame frame)
        {
            if (_vm?.ActiveWorkspace == null)
                return;

            var copy = frame.Widget.CloneForDuplicate();
            var (x, y) = FindNextFreeSlot(copy.W, copy.H);
            copy.X = x;
            copy.Y = y;
            _vm.ActiveWorkspace.Widgets.Add(copy.Model);
            _vm.Widgets.Add(copy);
            RenderDashboard();
            PersistLayout();
        }

        private void OnWidgetRemoveRequested(DashboardWidgetFrame frame)
        {
            if (_vm?.ActiveWorkspace == null)
                return;

            WriteWidgetAudit(AuditActions.WidgetRemoved, frame.Widget.Type, frame.Widget.Id, "Widget removed.");
            _vm.Widgets.Remove(frame.Widget);
            _vm.ActiveWorkspace.Widgets.RemoveAll(w => w.Id == frame.Widget.Id);
            RenderDashboard();
            PersistLayout();
        }

        private void OnWidgetExpandRequested(DashboardWidgetFrame frame)
        {
            var def = DashboardWidgetRegistry.Get(frame.Widget.Type);
            var panelKey = def?.PanelKey ?? frame.Widget.Type;
            WriteWidgetAudit(AuditActions.WidgetExpanded, frame.Widget.Type, frame.Widget.Id, "Widget expanded.");
            _window.ExpandDashboardWidget(panelKey);
        }

        private void DetachRegisteredPanels()
        {
            foreach (var panel in _panelHosts.Values.Distinct())
                DetachFromParent(panel);
        }

        private static void DetachFromParent(FrameworkElement element)
        {
            if (element.Parent is System.Windows.Controls.Panel panel)
                panel.Children.Remove(element);
        }

        private static List<DashboardWidgetInstance> CloneWidgets(IEnumerable<DashboardWidgetInstance> source)
        {
            var list = new List<DashboardWidgetInstance>();
            foreach (var w in source)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(w);
                var copy = System.Text.Json.JsonSerializer.Deserialize<DashboardWidgetInstance>(json)!;
                copy.Id = Guid.NewGuid().ToString("N");
                list.Add(copy);
            }
            return list;
        }

        private (int x, int y) FindNextFreeSlot(int w, int h)
        {
            if (_vm == null)
                return (0, 0);

            for (var y = 0; y < 200; y++)
            {
                for (var x = 0; x <= DashboardWidgetRegistry.ColumnCount - w; x++)
                {
                    if (!OverlapsAny(x, y, w, h, null))
                        return (x, y);
                }
            }

            var maxY = _vm.Widgets.Select(v => v.Y + v.H).DefaultIfEmpty(0).Max();
            return (0, maxY);
        }

        private bool OverlapsAny(int x, int y, int w, int h, string? ignoreId)
        {
            if (_vm == null)
                return false;

            foreach (var widget in _vm.Widgets)
            {
                if (ignoreId != null && widget.Id == ignoreId)
                    continue;
                if (RectsOverlap(x, y, w, h, widget.X, widget.Y, widget.W, widget.H))
                    return true;
            }

            return false;
        }

        public bool TryMoveWidget(string id, int x, int y)
        {
            var widget = _vm?.FindWidget(id);
            return widget != null && ReflowWidget(id, x, y, widget.W, widget.H, persist: false);
        }

        public bool TryResizeWidget(string id, int w, int h)
        {
            var widget = _vm?.FindWidget(id);
            return widget != null && ReflowWidget(id, widget.X, widget.Y, w, h, persist: false);
        }

        public bool ReflowWidget(string id, int x, int y, int w, int h, bool persist)
        {
            if (_vm == null)
                return false;

            var widget = _vm.FindWidget(id);
            if (widget == null || widget.IsLocked)
                return false;

            w = Math.Clamp(w, widget.Model.MinW, widget.Model.MaxW ?? DashboardWidgetRegistry.ColumnCount);
            h = Math.Max(widget.Model.MinH, h);
            w = Math.Min(w, DashboardWidgetRegistry.ColumnCount);
            x = Math.Clamp(x, 0, DashboardWidgetRegistry.ColumnCount - w);
            y = ClampDropRow(y, widget.Id);

            widget.W = w;
            widget.H = h;
            widget.X = x;
            widget.Y = y;

            ResolveLocalCollisions(widget);

            if (persist)
                PersistLayout();

            return true;
        }

        private void CompactLayout()
        {
            if (_vm == null)
                return;

            var placed = new List<DashboardWidgetViewModel>();
            foreach (var widget in _vm.Widgets
                         .Where(w => w.IsVisible && !w.IsCollapsed)
                         .OrderBy(w => w.DisplayIndex)
                         .ToList())
            {
                var (x, y) = FindFirstFreeSlot(widget.W, widget.H, placed);
                widget.X = x;
                widget.Y = y;
                placed.Add(widget);
            }
        }

        private void ResolveLocalCollisions(DashboardWidgetViewModel anchor)
        {
            if (_vm == null)
                return;

            var queue = new Queue<DashboardWidgetViewModel>();
            var guard = 0;
            queue.Enqueue(anchor);

            while (queue.Count > 0 && guard++ < 200)
            {
                var source = queue.Dequeue();
                var collisions = _vm.Widgets
                    .Where(w => w.Id != source.Id && w.IsVisible && !w.IsCollapsed)
                    .Where(w => RectsOverlap(source.X, source.Y, source.W, source.H, w.X, w.Y, w.W, w.H))
                    .OrderBy(w => Distance(w.X, w.Y, source.X, source.Y))
                    .ThenBy(w => w.DisplayIndex)
                    .ToList();

                foreach (var collided in collisions)
                {
                    var oldX = collided.X;
                    var oldY = collided.Y;
                    var (x, y) = FindNearestFreeSlot(collided, oldX, oldY);
                    if (x == oldX && y == oldY)
                        continue;

                    collided.X = x;
                    collided.Y = y;
                    queue.Enqueue(collided);
                }
            }
        }

        private (int x, int y) FindNearestFreeSlot(DashboardWidgetViewModel moving, int preferredX, int preferredY)
        {
            if (_vm == null)
                return (Math.Max(0, preferredX), Math.Max(0, preferredY));

            var maxX = Math.Max(0, DashboardWidgetRegistry.ColumnCount - moving.W);
            preferredX = Math.Clamp(preferredX, 0, maxX);
            preferredY = Math.Max(0, preferredY);

            if (IsAreaFree(preferredX, preferredY, moving.W, moving.H, moving.Id))
                return (preferredX, preferredY);

            var maxExistingY = _vm.Widgets.Select(w => w.Y + w.H).DefaultIfEmpty(0).Max();
            var searchLimitY = Math.Max(maxExistingY + 30, preferredY + 30);
            (int x, int y)? best = null;
            var bestScore = int.MaxValue;

            for (var y = 0; y <= searchLimitY; y++)
            {
                for (var x = 0; x <= maxX; x++)
                {
                    if (!IsAreaFree(x, y, moving.W, moving.H, moving.Id))
                        continue;

                    var score = Distance(x, y, preferredX, preferredY);
                    if (score >= bestScore)
                        continue;

                    best = (x, y);
                    bestScore = score;
                }
            }

            return best ?? (0, maxExistingY);
        }

        private bool IsAreaFree(int x, int y, int w, int h, string ignoreId)
        {
            if (_vm == null)
                return true;

            if (x < 0 || y < 0 || x + w > DashboardWidgetRegistry.ColumnCount)
                return false;

            return !_vm.Widgets
                .Where(widget => widget.Id != ignoreId && widget.IsVisible && !widget.IsCollapsed)
                .Any(widget => RectsOverlap(x, y, w, h, widget.X, widget.Y, widget.W, widget.H));
        }

        private static int Distance(int x1, int y1, int x2, int y2) =>
            Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

        private int ClampDropRow(int y, string movingId)
        {
            if (_vm == null)
                return Math.Max(0, y);

            const int maxEmptyRows = 1;
            var occupiedRows = _vm.Widgets
                .Where(w => w.Id != movingId && w.IsVisible && !w.IsCollapsed)
                .Select(w => w.Y + w.H)
                .DefaultIfEmpty(0)
                .Max();
            return Math.Clamp(y, 0, occupiedRows + maxEmptyRows);
        }

        private static (int x, int y) FindFirstFreeSlot(int w, int h, IReadOnlyCollection<DashboardWidgetViewModel> placed)
        {
            w = Math.Clamp(w, 1, DashboardWidgetRegistry.ColumnCount);
            h = Math.Max(1, h);

            for (var y = 0; y < 500; y++)
            {
                for (var x = 0; x <= DashboardWidgetRegistry.ColumnCount - w; x++)
                {
                    if (!placed.Any(p => RectsOverlap(x, y, w, h, p.X, p.Y, p.W, p.H)))
                        return (x, y);
                }
            }

            var maxY = placed.Select(v => v.Y + v.H).DefaultIfEmpty(0).Max();
            return (0, maxY);
        }

        private static bool RectsOverlap(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2) =>
            x1 < x2 + w2 && x1 + w1 > x2 && y1 < y2 + h2 && y1 + h1 > y2;

        private void WriteLayoutAudit(string action, string message)
        {
            AuditLogService.Log(
                action,
                AuditCategories.Dashboard,
                AuditResults.Success,
                LogLevels.Information,
                serverIp: _currentServerKey,
                message: message,
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new { serverKey = _currentServerKey }));
        }

        private void WriteWidgetAudit(string action, string widgetType, string widgetId, string message, object? details = null)
        {
            var payload = details ?? new { widgetType, widgetId, serverKey = _currentServerKey };
            AuditLogService.Log(
                action,
                AuditCategories.Widget,
                AuditResults.Success,
                LogLevels.Information,
                serverIp: _currentServerKey,
                message: message,
                detailsJson: System.Text.Json.JsonSerializer.Serialize(payload));
        }

        private FrameworkElement CreateUnavailablePanel(string key, string? title = null, string? message = null)
        {
            var def = DashboardWidgetRegistry.Get(key);
            var displayTitle = title ?? def?.Title ?? key;
            return new Border
            {
                Style = (Style)_window.FindResource("DashPanelCard"),
                Child = new StackPanel
                {
                    Margin = new Thickness(12),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = displayTitle,
                            Style = (Style)_window.FindResource("DashSectionTitle")
                        },
                        new TextBlock
                        {
                            Text = message ?? "This widget is unavailable in the current dashboard build.",
                            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x7A, 0x94)),
                            FontSize = 11,
                            Margin = new Thickness(0, 6, 0, 0),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
        }
    }
}
