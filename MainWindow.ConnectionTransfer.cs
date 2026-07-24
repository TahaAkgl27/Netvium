using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Netvium.Models;
using Netvium.Services;
using Netvium.Views;
using Netvium.Helpers;
using MessageBox = System.Windows.MessageBox;

namespace Netvium
{
    public partial class MainWindow
    {
        private async Task ExportConnectionsFromSettingsAsync()
        {
            if (!_servers.Any(s => !s.IsEphemeral))
            {
                ShowToast("Dışarı aktarılacak kayıtlı sunucu yok.", "\uE946");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Sunucuları Dışarı Aktar",
                Filter = "Netvium bağlantı dosyası (*.xml)|*.xml",
                DefaultExt = ".xml",
                AddExtension = true,
                FileName = $"netvium-sunucular-{DateTime.Now:yyyyMMdd-HHmm}.xml"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            var password = ConnectionTransferPasswordDialog.RequestExportPassword(this);
            if (password == null)
                return;

            try
            {
                var exported = await ConnectionTransferService.ExportNetviumAsync(
                    dialog.FileName,
                    _servers,
                    _explicitFolders,
                    _favoriteServerKeys,
                    password);
                ShowToast($"{exported} sunucu güvenli XML dosyasına aktarıldı.", "\uE74E", 5000);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Connection export failed.", ex, category: "Storage", action: "ExportConnections");
                MessageBox.Show(
                    "Dışa aktarma işlemi tamamlanamadı. Dosyayı ve parolayı kontrol edin.",
                    "Dışarı Aktarma Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task ImportConnectionsFromSettingsAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Sunucuları İçeri Aktar",
                Filter = "Bağlantı XML dosyaları (*.xml)|*.xml|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                var result = await ConnectionTransferService.ImportAsync(dialog.FileName);
                if (result.PasswordRequired)
                {
                    var sourceName = result.FileKind == ConnectionTransferFileKind.MRemoteNg
                        ? "mRemoteNG dosyası"
                        : "Netvium bağlantı dosyası";
                    var password = ConnectionTransferPasswordDialog.RequestImportPassword(this, sourceName);
                    if (password == null)
                        return;
                    result = await ConnectionTransferService.ImportAsync(dialog.FileName, password);
                }

                if (!result.Succeeded)
                {
                    MessageBox.Show(
                        "Bağlantı dosyası içeri aktarılamadı. Dosyayı ve parolayı kontrol edin.",
                        "İçe Aktarma Hatası",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                if (result.Servers.Count == 0)
                {
                    var emptyMessage = result.SkippedUnsupportedProtocols > 0
                        ? "Dosyada Netvium tarafından desteklenen RDP bağlantısı bulunamadı."
                        : "Dosyada içeri aktarılabilecek sunucu bulunamadı.";
                    MessageBox.Show(emptyMessage, "İçe Aktarma", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var (added, updated, selectedServer) = ApplyImportedConnections(result);
                await StorageService.SaveConnectionsImmediatelyAsync(
                    _servers,
                    _explicitFolders,
                    _serverHistory,
                    _favoriteServerKeys);

                BuildConnectionTree(selectedServer ?? _selectedServer);
                RefreshFavoritesUi();
                UpdateOverviewDashboard();
                UpdateRdpToolbarState();
                if (_welcomeOverlayVm.IsWelcomeOverlayVisible)
                    _welcomeOverlayVm.UpdateServerCount(_servers.Count);

                var summary = $"İçe aktarma tamamlandı: {added} yeni, {updated} güncellendi";
                if (result.SkippedUnsupportedProtocols > 0)
                    summary += $", {result.SkippedUnsupportedProtocols} desteklenmeyen protokol atlandı";
                if (result.SkippedInvalidEntries > 0)
                    summary += $", {result.SkippedInvalidEntries} geçersiz kayıt atlandı";
                if (result.CredentialsNotImported > 0)
                    summary += $", {result.CredentialsNotImported} parola alınamadı";
                ShowToast(summary + ".", "\uE8B5", 8000);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Connection import failed.", ex, category: "Storage", action: "ImportConnections");
                MessageBox.Show(
                    "İçe aktarma işlemi tamamlanamadı. Dosyayı ve parolayı kontrol edin.",
                    "İçe Aktarma Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private (int Added, int Updated, ServerInfo? SelectedServer) ApplyImportedConnections(
            ConnectionTransferImportResult result)
        {
            var added = 0;
            var updated = 0;
            ServerInfo? selectedServer = null;

            foreach (var imported in result.Servers)
            {
                var endpointKey = ConnectionTransferService.GetEndpointKey(imported.Ip, imported.Port);
                var existing = _servers.FirstOrDefault(server =>
                    ConnectionTransferService.GetEndpointKey(server.Ip, server.Port)
                        .Equals(endpointKey, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    _servers.Add(imported);
                    selectedServer ??= imported;
                    added++;
                    continue;
                }

                existing.Name = imported.Name;
                existing.Ip = imported.Ip;
                existing.Folder = imported.Folder;
                existing.Port = imported.Port;
                existing.Scale = imported.Scale;
                existing.ReadabilityMode = imported.ReadabilityMode;
                existing.ResolutionProfile = imported.ResolutionProfile;
                if (!string.IsNullOrWhiteSpace(imported.Username))
                    existing.Username = imported.Username;
                if (!imported.StoredPasswordUnavailable && !string.IsNullOrEmpty(imported.Password))
                    existing.Password = imported.Password;
                existing.StoredPasswordUnavailable = imported.StoredPasswordUnavailable
                    && string.IsNullOrEmpty(existing.Password);
                selectedServer ??= existing;
                updated++;
            }

            foreach (var folder in result.Folders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                if (!_explicitFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    _explicitFolders.Add(folder);
            }

            foreach (var favoriteKey in result.FavoriteServerKeys)
                _favoriteServerKeys.Add(favoriteKey);

            return (added, updated, selectedServer);
        }
    }
}
