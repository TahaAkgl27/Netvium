using System.Windows;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class LocalizationService
    {
        public static void Apply(Window window, AppSettings settings)
        {
            if (window is not MainWindow main)
                return;

            var isTr = settings.Language == "tr";

            main.Title = settings.ApplicationName;

            try
            {
                if (main.FindName("ConnectionStatusText") is System.Windows.Controls.TextBlock status)
                {
                    if (status.Text.Contains("RDP") || status.Text.Contains("Bağlantı") || status.Text.Contains("Connection"))
                        status.Text = isTr ? "RDP bağlantısı yok." : "No RDP connection.";
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            System.Windows.Application.Current.Resources["NetviumLanguage"] = settings.Language;
        }

        public static string T(string english, string turkish)
        {
            return SettingsRuntime.Current.Language == "tr" ? turkish : english;
        }
    }
}
