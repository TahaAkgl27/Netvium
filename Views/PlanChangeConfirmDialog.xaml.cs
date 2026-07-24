using System.Windows;
using System.Windows.Input;
using Netvium.Models.Account;

namespace Netvium.Views;

public partial class PlanChangeConfirmDialog : Window
{
    private PlanChangeConfirmDialog(AdminUserSummary user, string newPlanName, int? durationDays)
    {
        InitializeComponent();
        EmailText.Text = user.Email;
        CurrentPlanText.Text = $"Mevcut plan: {user.PlanDisplayName} · {user.RemainingPlanTime}";
        NewPlanText.Text = newPlanName;
        DurationText.Text = durationDays is { } days
            ? $"{days} gün Premium erişim"
            : "Süresiz Free erişim";
    }

    public static bool Show(Window owner, AdminUserSummary user, string newPlanName, int? durationDays)
    {
        var dialog = new PlanChangeConfirmDialog(user, newPlanName, durationDays) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
