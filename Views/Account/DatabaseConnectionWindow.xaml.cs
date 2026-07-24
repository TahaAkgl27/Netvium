using System.Windows;
using System.Windows.Media;
using Netvium.Services.Account.DirectSql;

namespace Netvium.Views.Account;

public partial class DatabaseConnectionWindow : Window
{
    private readonly IdentitySqlConnectionStore _store;
    private bool _busy;

    public IdentitySqlConnectionOptions? SavedOptions { get; private set; }

    public DatabaseConnectionWindow(IdentitySqlConnectionStore store, IdentitySqlConnectionOptions? current = null)
    {
        _store = store;
        InitializeComponent();
        if (current is not null)
        {
            ServerBox.Text = current.Server;
            DatabaseBox.Text = current.Database;
            WindowsAuthCheckBox.IsChecked = current.UseWindowsAuthentication;
            UserNameBox.Text = current.UserName;
            SqlPasswordBox.Password = current.Password;
            TrustCertificateCheckBox.IsChecked = current.TrustServerCertificate;
        }
        UpdateAuthenticationFields();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnAuthenticationChanged(object sender, RoutedEventArgs e) => UpdateAuthenticationFields();

    private void UpdateAuthenticationFields()
    {
        if (SqlCredentialsPanel is null) return;
        SqlCredentialsPanel.IsEnabled = WindowsAuthCheckBox.IsChecked != true;
        SqlCredentialsPanel.Opacity = SqlCredentialsPanel.IsEnabled ? 1 : 0.45;
    }

    private IdentitySqlConnectionOptions ReadOptions() => new(
        ServerBox.Text.Trim(),
        DatabaseBox.Text.Trim(),
        WindowsAuthCheckBox.IsChecked == true,
        UserNameBox.Text.Trim(),
        SqlPasswordBox.Password,
        TrustCertificateCheckBox.IsChecked == true);

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await TestAsync(saveOnSuccess: false);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await TestAsync(saveOnSuccess: true);
    }

    private async Task TestAsync(bool saveOnSuccess)
    {
        IdentitySqlConnectionOptions options;
        try { options = ReadOptions(); _ = options.BuildConnectionString(); }
        catch (InvalidOperationException ex) { ShowStatus(ex.Message, false); return; }

        SetBusy(true);
        ShowStatus("Sunucu bağlantısı kontrol ediliyor...", true);
        try
        {
            var result = await IdentitySqlConnectionTester.TestAsync(options);
            ShowStatus(result.Message, result.Succeeded);
            if (!result.Succeeded || !saveOnSuccess) return;
            await _store.WriteAsync(options);
            SavedOptions = options;
            DialogResult = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        TestButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
    }

    private void ShowStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(success ? "#86EFAC" : "#FCA5A5"));
        StatusPanel.Background = new SolidColorBrush((System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(success ? "#10251E" : "#2A151D"));
        StatusPanel.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(success ? "#24543F" : "#5D2634"));
    }
}
