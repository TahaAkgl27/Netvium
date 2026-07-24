using System.Windows;
using System.Windows.Controls;
using Netvium.Models;
using Netvium.Models.Account;
using Netvium.Services.Account;

namespace Netvium.Views;

public partial class AdminWindow : Window
{
    private readonly IAccountService _accountService;

    public AdminWindow(IAccountService accountService)
    {
        _accountService = accountService;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var result = await _accountService.GetAdminDashboardAsync();
        if (!result.Succeeded)
        {
            System.Windows.MessageBox.Show(this, result.ErrorMessage, "Admin License Console", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        var data = result.Value!;
        TotalUsersText.Text = data.TotalUsers.ToString();
        ActiveUsersText.Text = data.ActiveUsers.ToString();
        PremiumUsersText.Text = data.ActivePremiumUsers.ToString();
        DevicesText.Text = data.ActiveDevices.ToString();
        UsersGrid.ItemsSource = data.Users;
        TokensGrid.ItemsSource = data.Tokens;
        UsageGrid.ItemsSource = data.UsageEvents;
        AuditGrid.ItemsSource = data.AuditEvents;
    }

    private async void OnIssueTokenClick(object sender, RoutedEventArgs e)
    {
        var plan = PlanBox.SelectedIndex == 1 ? LicensingPlanTier.PremiumPlus : LicensingPlanTier.Premium;
        if (!int.TryParse(DurationDaysBox.Text, out var durationDays) || durationDays is < 1 or > 3650)
        {
            System.Windows.MessageBox.Show(this, "Süre 1 ile 3650 gün arasında olmalı.", "Geçersiz süre",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var note = TokenNoteBox.Text == "Not (isteğe bağlı)" ? null : TokenNoteBox.Text;
        var result = await _accountService.IssueActivationTokenAsync(plan, durationDays, note);
        if (!result.Succeeded)
        {
            System.Windows.MessageBox.Show(this, result.ErrorMessage, "Kod üretilemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        new ActivationCodeWindow(result.Value!.Code) { Owner = this }.ShowDialog();
        await RefreshAsync();
    }

    private void OnUserSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not AdminUserSummary user)
        {
            SelectedUserText.Text = "Bir kullanıcı seçin";
            return;
        }

        SelectedUserText.Text = $"{user.DisplayName} · {user.Email} · {user.RemainingPlanTime}";
        UserPlanBox.SelectedIndex = user.PlanCode switch
        {
            "PREMIUM" => 1,
            "PREMIUM_PLUS" => 2,
            _ => 0
        };
        if (user.LicenseExpiresAtUtc is { } expiresAt)
            UserPlanDurationBox.Text = Math.Max(1, Math.Ceiling((expiresAt - DateTime.UtcNow).TotalDays)).ToString("0");
    }

    private async void OnChangeUserPlanClick(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not AdminUserSummary user)
        {
            System.Windows.MessageBox.Show(this, "Önce planı değiştirilecek kullanıcıyı seçin.", "Kullanıcı seçin",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var plan = UserPlanBox.SelectedIndex switch
        {
            1 => LicensingPlanTier.Premium,
            2 => LicensingPlanTier.PremiumPlus,
            _ => LicensingPlanTier.Free
        };
        int? durationDays = null;
        if (plan != LicensingPlanTier.Free &&
            (!int.TryParse(UserPlanDurationBox.Text, out var parsedDays) || parsedDays is < 1 or > 3650))
        {
            System.Windows.MessageBox.Show(this, "Premium süresi 1 ile 3650 gün arasında olmalı.", "Geçersiz süre",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        else if (plan != LicensingPlanTier.Free)
        {
            durationDays = int.Parse(UserPlanDurationBox.Text);
        }

        var planName = plan switch
        {
            LicensingPlanTier.Premium => "Premium",
            LicensingPlanTier.PremiumPlus => "Premium+",
            _ => "Free"
        };
        if (!PlanChangeConfirmDialog.Show(this, user, planName, durationDays))
            return;

        var result = await _accountService.ChangeUserPlanAsync(user.Id, plan, durationDays);
        if (!result.Succeeded)
        {
            System.Windows.MessageBox.Show(this, result.ErrorMessage, "Plan değiştirilemedi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RefreshAsync();
    }
}
