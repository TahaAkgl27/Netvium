using Netvium.Models.Account;
using System.Windows;
using System.Windows.Media;
using Netvium.Services.Account;

namespace Netvium.Views.Account;

public partial class AccountWindow : Window
{
    private readonly IAccountService _accountService;
    private bool _busy;

    public event EventHandler? Authenticated;
    public event EventHandler? ConnectionSettingsRequested;

    public AccountWindow(IAccountService accountService, string? initialMessage = null)
    {
        _accountService = accountService;
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(initialMessage))
            ShowStatus(initialMessage, false);
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnLoginTabClick(object sender, RoutedEventArgs e) => SetMode(register: false);
    private void OnRegisterTabClick(object sender, RoutedEventArgs e) => SetMode(register: true);

    private void SetMode(bool register)
    {
        LoginPanel.Visibility = register ? Visibility.Collapsed : Visibility.Visible;
        RegisterPanel.Visibility = register ? Visibility.Visible : Visibility.Collapsed;
        LoginTabButton.FontWeight = register ? FontWeights.Normal : FontWeights.SemiBold;
        RegisterTabButton.FontWeight = register ? FontWeights.SemiBold : FontWeights.Normal;
        StatusText.Text = "Hesabınıza giriş yapın veya yeni hesap oluşturun.";
        StatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8492A6"));
        StatusPanel.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#111A28"));
        StatusPanel.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#26354B"));
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(LoginEmailBox.Text) || string.IsNullOrEmpty(LoginPasswordBox.Password))
        {
            ShowStatus("E-posta ve parola alanlarını doldurun.", false);
            return;
        }
        SetBusy(true);
        try
        {
            var result = await _accountService.LoginAsync(LoginEmailBox.Text, LoginPasswordBox.Password);
            LoginPasswordBox.Clear();
            if (!result.Succeeded)
            {
                ShowStatus(result.ErrorMessage ?? "Giriş yapılamadı.", false);
                return;
            }
            Authenticated?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            ShowStatus("Hesap işlemi şu anda tamamlanamadı. Lütfen yeniden deneyin.", false);
        }
        finally { SetBusy(false); }
    }

    private async void OnRegisterClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (RegisterPasswordBox.Password != RegisterPasswordAgainBox.Password)
        {
            ShowStatus("Parolalar eşleşmiyor.", false);
            return;
        }
        SetBusy(true);
        try
        {
            var result = await _accountService.RegisterAsync(RegisterEmailBox.Text, RegisterDisplayNameBox.Text, RegisterPasswordBox.Password);
            RegisterPasswordBox.Clear();
            RegisterPasswordAgainBox.Clear();
            if (!result.Succeeded)
            {
                ShowStatus(result.ErrorMessage ?? "Hesap oluşturulamadı.", false);
                return;
            }
            LoginEmailBox.Text = RegisterEmailBox.Text.Trim();
            SetMode(register: false);
            ShowStatus(result.Status == AccountResultStatus.StagedOffline
                ? "Bağlantı kurulamadığı için kayıt güvenli olarak çevrimdışı sıraya alındı. Bağlantı geldiğinde otomatik eşitlenecek; ardından giriş yapabilirsiniz."
                : "Hesabınız oluşturuldu. Şimdi giriş yapabilirsiniz.", true);
        }
        catch
        {
            ShowStatus("Hesap işlemi şu anda tamamlanamadı. Lütfen yeniden deneyin.", false);
        }
        finally { SetBusy(false); }
    }

    private void OnForgotPasswordClick(object sender, RoutedEventArgs e) =>
        ShowStatus("Parola sıfırlama özelliği yakında kullanıma açılacak.", true);

    private void OnConnectionSettingsClick(object sender, RoutedEventArgs e) =>
        ConnectionSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        LoginButton.IsEnabled = !busy;
        RegisterButton.IsEnabled = !busy;
        if (busy) ShowStatus("Lütfen bekleyin...", true);
    }

    private void ShowStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(success ? "#86EFAC" : "#FCA5A5"));
        StatusPanel.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(success ? "#10251E" : "#2A151D"));
        StatusPanel.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(success ? "#24543F" : "#5D2634"));
    }
}
