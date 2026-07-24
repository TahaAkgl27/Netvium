using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    /// <summary>Runtime snapshot of settings for hot-path reads without async.</summary>
    public static class SettingsRuntime
    {
        private static AppSettings _current = AppSettings.CreateDefault();

        public static AppSettings Current => _current;

        public static void Sync(AppSettings settings)
        {
            _current = settings.Clone();
            WmiCollector.Configure(_current);
            InsightEngine.Configure(_current);
            AppLogger.Configure(_current);
        }

        public static void SyncFromService() => Sync(SettingsService.Instance.Current);
    }
}
