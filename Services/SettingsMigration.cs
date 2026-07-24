using Netvium.Models;

namespace Netvium.Services
{
    public static class SettingsMigration
    {
        public const int CurrentVersion = 6;

        public static bool Migrate(AppSettings settings)
        {
            var changed = false;
            if (settings.SettingsVersion < 1)
            {
                if (settings.MaximumLogSizeMb > 20)
                {
                    settings.MaximumLogSizeMb = 20;
                    changed = true;
                }

                if (settings.MaximumLogSizeMb < 1)
                {
                    settings.MaximumLogSizeMb = 20;
                    changed = true;
                }

                settings.SettingsVersion = 1;
                changed = true;
            }

            if (settings.SettingsVersion < 2)
            {
                if (settings.AgentPort <= 0)
                {
                    settings.AgentPort = 9841;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(settings.AgentScheme))
                {
                    settings.AgentScheme = "http";
                    changed = true;
                }

                settings.SettingsVersion = 2;
                changed = true;
            }

            if (settings.SettingsVersion < 3)
            {
                if (!string.Equals(settings.Theme, "dark", StringComparison.OrdinalIgnoreCase))
                {
                    settings.Theme = "dark";
                    changed = true;
                }

                settings.SettingsVersion = 3;
                changed = true;
            }

            if (settings.SettingsVersion < 4)
            {
                if (string.IsNullOrWhiteSpace(settings.VisionProvider))
                {
                    settings.VisionProvider = settings.AiProvider;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(settings.VisionModel))
                {
                    settings.VisionModel = "gpt-4o-mini";
                    changed = true;
                }

                if (settings.VisionTimeoutSeconds <= 0)
                {
                    settings.VisionTimeoutSeconds = 30;
                    changed = true;
                }

                settings.SettingsVersion = 4;
                changed = true;
            }

            if (settings.SettingsVersion < 5)
            {
                settings.DevelopmentMode = true;
                settings.EnableAIGuideVision = true;
                settings.SettingsVersion = 5;
                changed = true;
            }

            if (settings.SettingsVersion < 6)
            {
                settings.EnableAIGuideCaptureDiagnostics = false;
                settings.SettingsVersion = 6;
                changed = true;
            }

            return changed;
        }
    }
}
