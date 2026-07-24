using System.Collections.ObjectModel;
using System.Linq;
using Netvium.Models.Dashboard;
using Netvium.ViewModels;

namespace Netvium.ViewModels.Dashboard
{
    public sealed class DashboardWorkspaceViewModel : ViewModelBase
    {
        private bool _isEditMode;
        private bool _isLayoutLocked;
        private DashboardWorkspace? _activeWorkspace;

        public ObservableCollection<DashboardWorkspace> Workspaces { get; } = new();
        public ObservableCollection<DashboardWidgetViewModel> Widgets { get; } = new();

        public DashboardWorkspace? ActiveWorkspace
        {
            get => _activeWorkspace;
            set
            {
                if (!SetProperty(ref _activeWorkspace, value))
                    return;
                OnPropertyChanged(nameof(ActiveWorkspaceName));
                ReloadWidgetsFromWorkspace();
            }
        }

        public string ActiveWorkspaceName => ActiveWorkspace?.Name ?? "Workspace";

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (SetProperty(ref _isEditMode, value))
                    OnPropertyChanged(nameof(IsMonitoringMode));
            }
        }

        public bool IsLayoutLocked
        {
            get => _isLayoutLocked;
            set
            {
                if (SetProperty(ref _isLayoutLocked, value))
                {
                    OnPropertyChanged(nameof(CanEditWidgets));
                    OnPropertyChanged(nameof(IsMonitoringMode));
                }
            }
        }

        public bool CanEditWidgets => !IsLayoutLocked;
        public bool IsMonitoringMode => !IsEditMode || IsLayoutLocked;

        public void LoadDocument(DashboardLayoutDocument document)
        {
            Workspaces.Clear();
            foreach (var ws in document.Workspaces)
                Workspaces.Add(ws);

            IsLayoutLocked = document.IsLayoutLocked;
            ActiveWorkspace = Workspaces.FirstOrDefault(w => w.Id == document.ActiveWorkspaceId)
                              ?? Workspaces.FirstOrDefault();
        }

        public DashboardLayoutDocument ToDocument()
        {
            SyncActiveWorkspaceWidgets();
            return new DashboardLayoutDocument
            {
                Version = Helpers.DashboardWidgetRegistry.CurrentSchemaVersion,
                ActiveWorkspaceId = ActiveWorkspace?.Id ?? string.Empty,
                IsLayoutLocked = IsLayoutLocked,
                Workspaces = Workspaces.ToList()
            };
        }

        public void ReloadWidgetsFromWorkspace()
        {
            Widgets.Clear();
            if (ActiveWorkspace == null)
                return;

            foreach (var w in ActiveWorkspace.Widgets.Where(x => x.IsVisible))
                Widgets.Add(new DashboardWidgetViewModel(w));
        }

        public void SyncActiveWorkspaceWidgets()
        {
            if (ActiveWorkspace == null)
                return;

            ActiveWorkspace.Widgets = Widgets.Select(w => w.Model).ToList();
        }

        public DashboardWidgetViewModel? FindWidget(string id) =>
            Widgets.FirstOrDefault(w => w.Id == id);
    }
}
