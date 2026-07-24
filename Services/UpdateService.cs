using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Netvium.Helpers;

namespace Netvium.Services
{
    public static class UpdateService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
        static UpdateService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Netvium-Desktop/1.0");
        }


        public static async Task<(bool UpdateAvailable, string Message)> CheckForUpdatesAsync(bool respectAutoUpdateSetting = false)
        {
            var local = Assembly.GetExecutingAssembly().GetName().Version;
            var localText = local == null ? "1.0.0" : $"{local.Major}.{local.Minor}.{local.Build}";

            if (respectAutoUpdateSetting && !SettingsService.Instance.Current.AutoUpdate)
            {
                return (false, $"Netvium {localText}\nAuto-update is disabled in settings.");
            }

            try
            {
                var response = await Http.GetStringAsync("https://api.github.com/repos/netvium/netvium/releases/latest");
                using var doc = JsonDocument.Parse(response);
                var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? localText;

                if (Version.TryParse(tag, out var remote) && local != null &&
                    remote > new Version(local.Major, local.Minor, local.Build))
                {
                    return (true, $"Update available: {tag} (current {localText})");
                }

                return (false, $"You are on the latest release ({localText}).");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Update check failed: {ex.Message}");
                return (false, $"Netvium {localText}\nCould not reach update server. You are on the installed build.");
            }
        }
    }
}
