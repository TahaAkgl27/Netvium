using System.Windows;

namespace Netvium.Views
{
    public partial class ConnectionTransferPasswordDialog : Window
    {
        private readonly bool _requiresConfirmation;

        private ConnectionTransferPasswordDialog(bool exporting, string sourceName)
        {
            InitializeComponent();
            _requiresConfirmation = exporting;
            ConfirmationPanel.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
            HeadingText.Text = exporting ? "Sunucuları dışarı aktar" : "Bağlantı dosyasını aç";
            DescriptionText.Text = exporting
                ? "Sunucu parolaları bu aktarım parolasıyla AES-256 kullanılarak korunur. Dosyayı içeri aktarırken aynı parola gerekir."
                : $"{sourceName} içindeki korumalı sunucu parolalarını almak için aktarım/ana parolayı girin.";
            ConfirmButton.Content = exporting ? "Dışarı Aktar" : "İçeri Aktar";
            Loaded += (_, _) => PasswordInput.Focus();
        }

        public string TransferPassword => PasswordInput.Password;

        public static string? RequestExportPassword(Window owner)
        {
            var dialog = new ConnectionTransferPasswordDialog(exporting: true, sourceName: "Netvium") { Owner = owner };
            return dialog.ShowDialog() == true ? dialog.TransferPassword : null;
        }

        public static string? RequestImportPassword(Window owner, string sourceName)
        {
            var dialog = new ConnectionTransferPasswordDialog(exporting: false, sourceName) { Owner = owner };
            return dialog.ShowDialog() == true ? dialog.TransferPassword : null;
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            ValidationText.Text = string.Empty;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PasswordInput.Password))
            {
                ValidationText.Text = "Parola boş bırakılamaz.";
                PasswordInput.Focus();
                return;
            }

            if (_requiresConfirmation && PasswordInput.Password.Length < 8)
            {
                ValidationText.Text = "Aktarım parolası en az 8 karakter olmalıdır.";
                PasswordInput.Focus();
                return;
            }

            if (_requiresConfirmation && PasswordInput.Password != ConfirmationInput.Password)
            {
                ValidationText.Text = "Parolalar eşleşmiyor.";
                ConfirmationInput.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}
