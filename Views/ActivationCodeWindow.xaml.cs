using System.Windows;
using Netvium.Services;

namespace Netvium.Views;

public partial class ActivationCodeWindow : Window
{
    public string Code { get; }

    public ActivationCodeWindow(string code)
    {
        Code = code;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            CodeBox.Focus();
            CodeBox.SelectAll();
        };
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(Code);
        CopyStatusText.Text = UiLocalizationService.T("Code copied to clipboard.", "Kod panoya kopyalandı.");
        CopyStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
