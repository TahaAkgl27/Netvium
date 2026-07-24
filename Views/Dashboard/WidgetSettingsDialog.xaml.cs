using System.Globalization;
using System.Windows;
using Netvium.ViewModels.Dashboard;

namespace Netvium.Views.Dashboard
{
    public partial class WidgetSettingsDialog : Window
    {
        private readonly DashboardWidgetViewModel _widget;

        public WidgetSettingsDialog(DashboardWidgetViewModel widget)
        {
            InitializeComponent();
            _widget = widget;
            LoadFromWidget();
        }

        private void LoadFromWidget()
        {
            var s = _widget.Settings;
            TitleBox.Text = _widget.Title;
            IconBox.Text = s.Icon;
            AccentBox.Text = s.AccentColor;
            ShowHeaderCheck.IsChecked = s.ShowHeader;
            ShowBorderCheck.IsChecked = s.ShowBorder;
            CompactModeCheck.IsChecked = s.CompactMode;
            AnimationCheck.IsChecked = s.AnimationEnabled;
            TransparencySlider.Value = s.Transparency;
            RefreshBox.Text = s.RefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            ChartTypeCombo.Text = s.ChartType;
            PrecisionBox.Text = s.DecimalPrecision.ToString(CultureInfo.InvariantCulture);
            WarnBox.Text = s.WarningThreshold.ToString(CultureInfo.InvariantCulture);
            CritBox.Text = s.CriticalThreshold.ToString(CultureInfo.InvariantCulture);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var s = _widget.Settings;
            _widget.Title = TitleBox.Text.Trim();
            s.Icon = IconBox.Text.Trim();
            s.AccentColor = AccentBox.Text.Trim();
            s.ShowHeader = ShowHeaderCheck.IsChecked == true;
            s.ShowBorder = ShowBorderCheck.IsChecked == true;
            s.CompactMode = CompactModeCheck.IsChecked == true;
            s.AnimationEnabled = AnimationCheck.IsChecked == true;
            s.Transparency = TransparencySlider.Value;
            if (int.TryParse(RefreshBox.Text, out var refresh))
                s.RefreshIntervalSeconds = refresh;
            s.ChartType = ChartTypeCombo.Text;
            if (int.TryParse(PrecisionBox.Text, out var precision))
                s.DecimalPrecision = precision;
            if (double.TryParse(WarnBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var warn))
                s.WarningThreshold = warn;
            if (double.TryParse(CritBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var crit))
                s.CriticalThreshold = crit;

            DialogResult = true;
            Close();
        }
    }
}
