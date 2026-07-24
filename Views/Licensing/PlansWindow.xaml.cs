using System.Windows;
using System.Windows.Input;
using Netvium.Models;
using Netvium.Services;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace Netvium.Views.Licensing
{
    public partial class PlansWindow : Window
    {
        public PlansWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                RefreshPlanBadges();
                if (string.IsNullOrWhiteSpace(ActivationCodeBox.Text))
                    ActivationCodeBox.Text = "Enter Activation Code";
            };
            ActivationCodeBox.GotFocus += (_, _) =>
            {
                if (ActivationCodeBox.Text == "Enter Activation Code")
                {
                    ActivationCodeBox.Text = string.Empty;
                    ActivationCodeBox.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xE2, 0xE8, 0xF0));
                }
            };
            ActivationCodeBox.LostFocus += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(ActivationCodeBox.Text))
                {
                    ActivationCodeBox.Text = "Enter Activation Code";
                    ActivationCodeBox.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0x64, 0x74, 0x8B));
                }
            };
        }

        private void RefreshPlanBadges()
        {
            var isPremium = FeatureEntitlementService.Instance.IsPremiumEnabled;
            var isPremiumPlus = FeatureEntitlementService.Instance.IsAiGuideEnabled;

            FreeCurrentBadge.Visibility = isPremium ? Visibility.Collapsed : Visibility.Visible;
            PremiumCurrentBadge.Visibility = isPremium && !isPremiumPlus ? Visibility.Visible : Visibility.Collapsed;
            PremiumPlusCurrentBadge.Visibility = isPremiumPlus ? Visibility.Visible : Visibility.Collapsed;

            PremiumUpgradeButton.IsEnabled = !isPremium;
            PremiumUpgradeButton.Content = isPremium ? "Included in Your Plan" : "Enter Code to Activate";

            PremiumPlusUpgradeButton.IsEnabled = !isPremiumPlus;
            PremiumPlusUpgradeButton.Content = isPremiumPlus ? "Current Plan" : "Enter Code to Activate";
        }

        private void OnPremiumCardClick(object sender, MouseButtonEventArgs e)
        {
            if (FeatureEntitlementService.Instance.IsPremiumEnabled)
                return;

            if (e.OriginalSource is DependencyObject source && IsDescendantOf(source, PremiumUpgradeButton))
                return;

            PromptForActivationCode();
            e.Handled = true;
        }

        private void OnPremiumUpgradeClick(object sender, MouseButtonEventArgs e)
        {
            if (FeatureEntitlementService.Instance.IsPremiumEnabled)
                return;

            PromptForActivationCode();
            e.Handled = true;
        }

        private void OnPremiumPlusCardClick(object sender, MouseButtonEventArgs e)
        {
            if (FeatureEntitlementService.Instance.IsAiGuideEnabled)
                return;

            if (e.OriginalSource is DependencyObject source && IsDescendantOf(source, PremiumPlusUpgradeButton))
                return;

            PromptForActivationCode();
            e.Handled = true;
        }

        private void OnPremiumPlusUpgradeClick(object sender, MouseButtonEventArgs e)
        {
            if (FeatureEntitlementService.Instance.IsAiGuideEnabled)
                return;

            PromptForActivationCode();
            e.Handled = true;
        }

        private void PromptForActivationCode()
        {
            ActivationPanel?.BringIntoView();
            LicenseMessageDialog.Show(this, LicenseMessageKind.Prompt, "Plan aktivasyonu hesabınızın SQL tabanlı plan kaynağı üzerinden yapılır.");
            FocusActivationCodeBox();
        }

        private void FocusActivationCodeBox()
        {
            ActivationCodeBox.Focus();
            if (ActivationCodeBox.Text == "Enter Activation Code")
            {
                ActivationCodeBox.Text = string.Empty;
                ActivationCodeBox.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xE2, 0xE8, 0xF0));
            }
            else
            {
                ActivationCodeBox.SelectAll();
            }
        }

        private async void OnActivateClick(object sender, RoutedEventArgs e)
        {
            var code = ActivationCodeBox.Text == "Enter Activation Code" ? string.Empty : ActivationCodeBox.Text;
            if (Owner is not Netvium.MainWindow mainWindow)
            {
                LicenseMessageDialog.Show(this, LicenseMessageKind.Warning,
                    "Plan hesabınıza bağlanamadı. Lütfen ana ekrandan tekrar deneyin.");
                return;
            }

            if (sender is not System.Windows.Controls.Button activateButton)
                return;

            activateButton.IsEnabled = false;
            try
            {
                var sync = await mainWindow.RedeemActivationCodeAsync(code);
                if (!sync.Succeeded)
                {
                    LicenseMessageDialog.Show(this, LicenseMessageKind.Warning, sync.Message);
                    return;
                }

                RefreshPlanBadges();
                LicenseMessageDialog.Show(this, LicenseMessageKind.Success, sync.Message);
                DialogResult = true;
                Close();
            }
            finally
            {
                activateButton.IsEnabled = true;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
        {
            while (source != null)
            {
                if (source == ancestor)
                    return true;
                source = WpfVisualTreeHelper.GetParent(source);
            }

            return false;
        }
    }
}
