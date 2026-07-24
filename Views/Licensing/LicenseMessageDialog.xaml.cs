using System.Windows;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Netvium.Views.Licensing
{
    public enum LicenseMessageKind
    {
        Prompt,
        Success,
        Warning
    }

    public partial class LicenseMessageDialog : Window
    {
        public LicenseMessageDialog()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        public static void Show(Window owner, LicenseMessageKind kind, string message, string? title = null)
        {
            var dialog = new LicenseMessageDialog { Owner = owner };
            dialog.ApplyKind(kind, message, title);
            dialog.ShowDialog();
        }

        private void ApplyKind(LicenseMessageKind kind, string message, string? title)
        {
            MessageText.Text = message;
            TitleText.Text = title ?? "Activate License";
            HintPanel.Visibility = kind == LicenseMessageKind.Prompt ? Visibility.Visible : Visibility.Collapsed;

            switch (kind)
            {
                case LicenseMessageKind.Success:
                    SubtitleText.Text = "License activated";
                    SubtitleText.Visibility = Visibility.Visible;
                    IconGlyph.Text = "\uE73E";
                    IconBadge.Background = new SolidColorBrush(WpfColor.FromRgb(0x12, 0x2A, 0x1A));
                    IconBadge.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x16, 0x65, 0x34));
                    IconGlyph.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x22, 0xC5, 0x5E));
                    ActionButton.Content = "Continue";
                    break;

                case LicenseMessageKind.Warning:
                    SubtitleText.Text = "Activation failed";
                    SubtitleText.Visibility = Visibility.Visible;
                    IconGlyph.Text = "\uE783";
                    IconBadge.Background = new SolidColorBrush(WpfColor.FromRgb(0x2A, 0x1A, 0x12));
                    IconBadge.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x92, 0x40, 0x0E));
                    IconGlyph.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xFB, 0xBF, 0x24));
                    ActionButton.Content = "Try again";
                    break;

                default:
                    SubtitleText.Text = "Enter your code below";
                    SubtitleText.Visibility = Visibility.Visible;
                    IconGlyph.Text = "\uE192";
                    IconBadge.Background = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1B, 0x4B));
                    IconBadge.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x43, 0x38, 0xCA));
                    IconGlyph.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x81, 0x8C, 0xF8));
                    ActionButton.Content = "Got it";
                    break;
            }
        }
    }
}
