using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Netvium.Helpers;
using Netvium.Models.Dashboard;

namespace Netvium.Views.Dashboard
{
    public partial class AddWidgetPanel : System.Windows.Controls.UserControl
    {
        private DashboardBuilderController? _controller;

        public AddWidgetPanel()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshCatalog();
        }

        public void Attach(DashboardBuilderController controller) => _controller = controller;

        public void ShowPanel()
        {
            Visibility = Visibility.Visible;
            RefreshCatalog();
        }

        public void HidePanel() => Visibility = Visibility.Collapsed;

        private void RefreshCatalog(string? filter = null)
        {
            var q = (filter ?? string.Empty).Trim();
            var groups = DashboardWidgetRegistry.Categories
                .Select(category => new CatalogGroup
                {
                    Category = category,
                    Items = DashboardWidgetRegistry.GetByCategory(category)
                        .Where(d => string.IsNullOrEmpty(q)
                                    || d.Title.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                                    || d.Description.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                                    || d.Type.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                        .ToList()
                })
                .Where(g => g.Items.Count > 0)
                .ToList();

            CatalogItems.ItemsSource = groups;
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e) =>
            RefreshCatalog(SearchBox.Text);

        private void OnWidgetPickClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string type)
                return;
            _controller?.AddWidget(type);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => HidePanel();

        private sealed class CatalogGroup
        {
            public string Category { get; set; } = string.Empty;
            public List<DashboardWidgetDefinition> Items { get; set; } = new();
        }
    }
}
