using Netvium.Models.Dashboard;
using Netvium.ViewModels;

namespace Netvium.ViewModels.Dashboard
{
    public sealed class DashboardWidgetViewModel : ViewModelBase
    {
        public DashboardWidgetViewModel(DashboardWidgetInstance model)
        {
            Model = model;
        }

        public DashboardWidgetInstance Model { get; }

        public string Id => Model.Id;
        public string Type => Model.Type;

        public string Title
        {
            get => string.IsNullOrWhiteSpace(Model.Settings.Title) ? Model.Type : Model.Settings.Title;
            set
            {
                Model.Settings.Title = value;
                OnPropertyChanged();
            }
        }

        public int X
        {
            get => Model.X;
            set
            {
                if (Model.X == value) return;
                Model.X = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayIndex));
            }
        }

        public int Y
        {
            get => Model.Y;
            set
            {
                if (Model.Y == value) return;
                Model.Y = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayIndex));
            }
        }

        public int W
        {
            get => Model.W;
            set
            {
                if (Model.W == value) return;
                Model.W = value;
                OnPropertyChanged();
            }
        }

        public int H
        {
            get => Model.H;
            set
            {
                if (Model.H == value) return;
                Model.H = value;
                OnPropertyChanged();
            }
        }

        public bool IsPinned
        {
            get => Model.IsPinned;
            set
            {
                if (Model.IsPinned == value) return;
                Model.IsPinned = value;
                OnPropertyChanged();
            }
        }

        public bool IsLocked
        {
            get => Model.IsLocked;
            set
            {
                if (Model.IsLocked == value) return;
                Model.IsLocked = value;
                OnPropertyChanged();
            }
        }

        public bool IsCollapsed
        {
            get => Model.IsCollapsed;
            set
            {
                if (Model.IsCollapsed == value) return;
                Model.IsCollapsed = value;
                OnPropertyChanged();
            }
        }

        public bool IsVisible
        {
            get => Model.IsVisible;
            set
            {
                if (Model.IsVisible == value) return;
                Model.IsVisible = value;
                OnPropertyChanged();
            }
        }

        public DashboardWidgetSettings Settings => Model.Settings;

        public int DisplayIndex => Y * 1000 + X;

        public DashboardWidgetViewModel CloneForDuplicate()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Model);
            var copy = System.Text.Json.JsonSerializer.Deserialize<DashboardWidgetInstance>(json)!;
            copy.Id = System.Guid.NewGuid().ToString("N");
            copy.X += 1;
            return new DashboardWidgetViewModel(copy);
        }
    }
}
