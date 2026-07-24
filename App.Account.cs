using System.Windows;
using Netvium.Helpers;
using Netvium.Services.Account;
using Netvium.Services.Account.DirectSql;
using Netvium.Views.Account;

namespace Netvium;

public partial class App
{
    private IAccountService? _accountService;
    private AccountWindow? _accountWindow;
    private readonly IdentitySqlConnectionStore _sqlConnectionStore = new();
    private IdentitySqlConnectionOptions? _sqlConnectionOptions;
    private bool _changingConnection;

    private async Task StartAccountFlowAsync()
    {
        _sqlConnectionOptions = await _sqlConnectionStore.ReadAsync();
        try
        {
            _accountService = _sqlConnectionOptions is null
                ? AccountComposition.InitializeOffline()
                : AccountComposition.Initialize(_sqlConnectionOptions);
            var restore = await _accountService.TryRestoreSessionAsync();
            AppLogger.Info(
                restore.Succeeded
                    ? "Stored account session restored."
                    : $"Stored account session was not restored ({restore.ErrorCode ?? "UNKNOWN"}).",
                category: "Identity", action: "RestoreSession");
        }
        catch (Exception ex)
        {
            // A broken SQL configuration must not disable encrypted local account access.
            AppLogger.Warning($"Central account session could not be restored: {ex.Message}",
                category: "Identity", action: "Restore");
            _accountService = AccountComposition.InitializeOffline();
            var offlineRestore = await _accountService.TryRestoreSessionAsync();
            AppLogger.Info(
                offlineRestore.Succeeded
                    ? "Stored account session restored in offline mode."
                    : $"Offline account session was not restored ({offlineRestore.ErrorCode ?? "UNKNOWN"}).",
                category: "Identity", action: "RestoreSession");
        }

        OpenMainWindow();
    }

    private void ShowAccountWindow(string? message = null)
    {
        if (_accountService is null)
        {
            RequestApplicationShutdown(-1);
            return;
        }

        var window = new AccountWindow(_accountService, message);
        _accountWindow = window;
        window.Authenticated += OnAccountAuthenticated;
        window.ConnectionSettingsRequested += OnConnectionSettingsRequested;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_accountWindow, window)) _accountWindow = null;
            if (!_changingConnection && _accountService.CurrentSession is null &&
                (MainWindow is null || !MainWindow.IsVisible))
                RequestApplicationShutdown();
        };
        MainWindow = window;
        window.Show();
    }

    private void OnAccountAuthenticated(object? sender, EventArgs e)
    {
        OpenMainWindow();
        _accountWindow?.Close();
    }

    private void OpenMainWindow()
    {
        var window = new Netvium.MainWindow(_accountService);
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Requests authentication only for account-bound actions such as plan changes.
    /// Free-mode users can keep using the main application without an account.
    /// </summary>
    public bool EnsureSignedInForAccountAction(Window owner)
    {
        if (_accountService?.CurrentSession is not null)
            return true;

        if (_accountService is null)
        {
            var setup = new DatabaseConnectionWindow(_sqlConnectionStore, _sqlConnectionOptions)
            {
                Owner = owner
            };

            if (setup.ShowDialog() != true || setup.SavedOptions is null)
                return false;

            try
            {
                _sqlConnectionOptions = setup.SavedOptions;
                _accountService = AccountComposition.Initialize(_sqlConnectionOptions);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Account service initialization failed.", ex, category: "Identity", action: "Configure");
                System.Windows.MessageBox.Show(owner,
                    "Hesap bağlantısı yapılandırılamadı. Sunucu ayarlarını kontrol edin.",
                    "Netvium Hesap",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        var accountWindow = new AccountWindow(
            _accountService,
            "Plan işlemlerine devam etmek için önce giriş yapın.")
        {
            Owner = owner
        };
        accountWindow.Authenticated += (_, _) => accountWindow.DialogResult = true;
        accountWindow.ConnectionSettingsRequested += (_, _) => UpdateAccountConnectionForGuestFlow(owner);

        var signedIn = accountWindow.ShowDialog() == true && _accountService.CurrentSession is not null;
        if (signedIn && MainWindow is Netvium.MainWindow mainWindow)
            mainWindow.SetAccountService(_accountService);

        return signedIn;
    }

    private void UpdateAccountConnectionForGuestFlow(Window owner)
    {
        var setup = new DatabaseConnectionWindow(_sqlConnectionStore, _sqlConnectionOptions)
        {
            Owner = owner
        };
        if (setup.ShowDialog() != true || setup.SavedOptions is null)
            return;

        try
        {
            _sqlConnectionOptions = setup.SavedOptions;
            _accountService = AccountComposition.Initialize(_sqlConnectionOptions);
            System.Windows.MessageBox.Show(owner,
                "Bağlantı ayarları güncellendi. Giriş yapmak için plan işlemine yeniden devam edin.",
                "Netvium Hesap",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Account service initialization failed.", ex, category: "Identity", action: "Configure");
        }
    }

    private async void OnConnectionSettingsRequested(object? sender, EventArgs e)
    {
        if (_changingConnection) return;
        var setup = new DatabaseConnectionWindow(_sqlConnectionStore, _sqlConnectionOptions)
        {
            Owner = _accountWindow
        };
        if (setup.ShowDialog() != true || setup.SavedOptions is null) return;

        _changingConnection = true;
        try
        {
            _sqlConnectionOptions = setup.SavedOptions;
            _accountService = AccountComposition.Initialize(_sqlConnectionOptions);
            var oldWindow = _accountWindow;
            oldWindow?.Close();
            await Task.Yield();
            ShowAccountWindow("Sunucu bağlantısı güncellendi. Giriş yapabilirsiniz.");
        }
        finally
        {
            _changingConnection = false;
        }
    }

    public void ShowLoginAfterLogout(Netvium.MainWindow source)
    {
        source.SetAccountService(_accountService);
    }
}
