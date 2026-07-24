using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Netvium;

public enum ConfirmActionKind
{
    Restart,
    Shutdown,
    CtrlAltDel,
    LogoffSession,
    ServiceAction
}

public partial class ConfirmActionDialog : Window
{
    public bool Confirmed { get; private set; }

    private readonly string? _detail;

    private ConfirmActionDialog(ConfirmActionKind kind, string serverName, string serverIp, string? detail = null)
    {
        InitializeComponent();

        ServerNameText.Text = serverName;
        ServerIpText.Text = serverIp;
        _detail = detail;

        ApplyTheme(kind);
    }

    public static bool Show(Window owner, ConfirmActionKind kind, string serverName, string serverIp, string? detail = null)
    {
        var dialog = new ConfirmActionDialog(kind, serverName, serverIp, detail)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    private void ApplyTheme(ConfirmActionKind kind)
    {
        if (kind == ConfirmActionKind.Restart)
        {
            TitleText.Text = "Yeniden Başlat";
            TitleIconText.Text = "\uE72C";
            TitleIconBadge.Background = Brush("#1E3A5F");
            ServerIconBadge.Background = Brush("#122A4A");
            RootBorder.BorderBrush = Brush("#334155");
            WarningBanner.Background = Brush("#101A2E");
            WarningBanner.BorderBrush = Brush("#223B82F6");
            WarningIconText.Foreground = Brush("#60A5FA");
            MessageText.Text = $"{ServerNameText.Text} ({ServerIpText.Text}) yeniden başlatılacak. Devam etmek istiyor musunuz?";
            ConfirmButton.Content = "Yeniden Başlat";
            SetConfirmGradient("#3B82F6", "#7C3AED");
        }
        else if (kind == ConfirmActionKind.Shutdown)
        {
            TitleText.Text = "Kapat";
            TitleIconText.Text = "\uE7E8";
            TitleIconBadge.Background = Brush("#3F1515");
            ServerIconBadge.Background = Brush("#2A1212");
            RootBorder.BorderBrush = Brush("#4C1D1D");
            WarningBanner.Background = Brush("#1A1010");
            WarningBanner.BorderBrush = Brush("#44EF4444");
            WarningIconText.Foreground = Brush("#F87171");
            MessageText.Text = $"{ServerNameText.Text} ({ServerIpText.Text}) kapatılacak. Devam etmek istiyor musunuz?";
            ConfirmButton.Content = "Kapat";
            SetConfirmGradient("#EF4444", "#F97316");
        }
        else if (kind == ConfirmActionKind.CtrlAltDel)
        {
            TitleText.Text = "Ctrl+Alt+Del Gönder";
            TitleIconText.Text = "\uE765";
            TitleIconBadge.Background = Brush("#1E3A5F");
            ServerIconBadge.Background = Brush("#122A4A");
            RootBorder.BorderBrush = Brush("#334155");
            WarningBanner.Background = Brush("#101A2E");
            WarningBanner.BorderBrush = Brush("#223B82F6");
            WarningIconText.Foreground = Brush("#60A5FA");
            MessageText.Text = $"{ServerNameText.Text} ({ServerIpText.Text}) oturumuna Ctrl+Alt+Del gönderilecek. Devam etmek istiyor musunuz?";
            ConfirmButton.Content = "Gönder";
            SetConfirmGradient("#3B82F6", "#7C3AED");
        }
        else if (kind == ConfirmActionKind.LogoffSession)
        {
            TitleText.Text = "Oturumu Kapat";
            TitleIconText.Text = "\uE7E8";
            TitleIconBadge.Background = Brush("#3F1515");
            ServerIconBadge.Background = Brush("#2A1212");
            RootBorder.BorderBrush = Brush("#4C1D1D");
            WarningBanner.Background = Brush("#1A1010");
            WarningBanner.BorderBrush = Brush("#44EF4444");
            WarningIconText.Foreground = Brush("#F87171");
            var sessionLabel = string.IsNullOrWhiteSpace(_detail) ? "seçili oturum" : _detail;
            MessageText.Text = $"{ServerNameText.Text} ({ServerIpText.Text}) üzerinde {sessionLabel} kapatılacak. Devam etmek istiyor musunuz?";
            ConfirmButton.Content = "Oturumu Kapat";
            SetConfirmGradient("#EF4444", "#F97316");
        }
        else
        {
            TitleText.Text = "Servis İşlemi";
            TitleIconText.Text = "\uE90F";
            TitleIconBadge.Background = Brush("#3F1515");
            ServerIconBadge.Background = Brush("#2A1212");
            RootBorder.BorderBrush = Brush("#4C1D1D");
            WarningBanner.Background = Brush("#1A1010");
            WarningBanner.BorderBrush = Brush("#44EF4444");
            WarningIconText.Foreground = Brush("#F87171");
            var actionLabel = string.IsNullOrWhiteSpace(_detail) ? "işlem" : _detail;
            MessageText.Text = $"{ServerNameText.Text} ({ServerIpText.Text}) üzerinde {actionLabel} yapılacak. Devam etmek istiyor musunuz?";
            ConfirmButton.Content = "Onayla";
            SetConfirmGradient("#EF4444", "#F97316");
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    private void SetConfirmGradient(string from, string to)
    {
        ConfirmButton.Background = new LinearGradientBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(from)!,
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(to)!,
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 0));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
