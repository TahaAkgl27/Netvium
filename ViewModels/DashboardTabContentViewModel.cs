namespace Netvium.ViewModels
{
    /// <summary>
    /// Marker for dashboard workspace DataTemplate selection.
    /// Actual dashboard UI remains in SelectedServerPanel.
    /// </summary>
    public sealed class DashboardTabContentViewModel : ViewModelBase
    {
        public static DashboardTabContentViewModel Instance { get; } = new();

        private DashboardTabContentViewModel()
        {
        }
    }
}
