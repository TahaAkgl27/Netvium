using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Models;
using Netvium.Helpers;

namespace Netvium.Services
{
    public sealed class SettingsService
    {
        public static SettingsService Instance { get; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private AppSettings _current = AppSettings.CreateDefault();
        private readonly SemaphoreSlim _saveGate = new(1, 1);

        public event EventHandler<AppSettings>? SettingsChanged;

        public AppSettings Current => _current;

        private SettingsService() { }

        public static string GetSettingsDirectory()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Netvium");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetSettingsPath() => Path.Combine(GetSettingsDirectory(), "settings.json");

        public static string GetBackupPath() => Path.Combine(GetSettingsDirectory(), "settings.backup.json");

        public static string GetLogsDirectory() => AppLogger.GetApplicationLogDirectory();

        public async Task LoadAsync()
        {
            try
            {
                var path = GetSettingsPath();
                if (!File.Exists(path))
                {
                    _current = AppSettings.CreateDefault();
                    Validate(_current);
                    SettingsMigration.Migrate(_current);
                    await SaveAsync();
                    return;
                }

                var json = await File.ReadAllTextAsync(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                _current = loaded ?? AppSettings.CreateDefault();
                var migrated = SettingsMigration.Migrate(_current);
                Validate(_current);
                if (migrated)
                    await SaveAsync();
                Helpers.SettingsRuntime.Sync(_current);
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.Error("Settings file corrupt — restoring defaults.", ex);
                try
                {
                    if (File.Exists(GetSettingsPath()))
                        File.Copy(GetSettingsPath(), GetBackupPath() + ".corrupt", true);
                }
                catch (Exception backupEx)
                {
                    Helpers.AppLogger.Warning("Could not backup corrupt settings file.", backupEx,
                        category: "Settings", action: "BackupCorrupt");
                }

                _current = AppSettings.CreateDefault();
                Validate(_current);
                await SaveAsync();
            }
        }

        public List<string> Validate(AppSettings? settings = null)
        {
            var s = settings ?? _current;
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(s.ApplicationName))
                s.ApplicationName = "Netvium";

            if (string.IsNullOrWhiteSpace(s.Language))
                s.Language = "en";

            if (string.IsNullOrWhiteSpace(s.Theme))
                s.Theme = "dark";
            s.Theme = "dark";

            if (string.IsNullOrWhiteSpace(s.AccentColor))
                s.AccentColor = "#3B82F6";

            if (string.IsNullOrWhiteSpace(s.AiProvider))
                s.AiProvider = "OpenAI";

            if (string.IsNullOrWhiteSpace(s.AiModel))
                s.AiModel = "gpt-4o-mini";

            if (string.IsNullOrWhiteSpace(s.AnalysisDepth))
                s.AnalysisDepth = "standard";

            s.SettingsVersion = Math.Max(s.SettingsVersion, SettingsMigration.CurrentVersion);

            s.DefaultRefreshIntervalSeconds = Clamp(s.DefaultRefreshIntervalSeconds, 5, 300);
            s.PingIntervalSeconds = Clamp(s.PingIntervalSeconds, 5, 600);
            s.DefaultConnectionTimeoutSeconds = Clamp(s.DefaultConnectionTimeoutSeconds, 5, 300);
            s.MaximumRetryCount = Clamp(s.MaximumRetryCount, 0, 20);
            s.MaxConcurrentConnections = Clamp(s.MaxConcurrentConnections, 1, 50);
            s.MaximumGraphHistory = Clamp(s.MaximumGraphHistory, 30, 1000);
            s.MonitoringTimeoutSeconds = Clamp(s.MonitoringTimeoutSeconds, 5, 180);
            s.NotificationDurationMs = Clamp(s.NotificationDurationMs, 1000, 30000);
            s.CacheSizeMb = Clamp(s.CacheSizeMb, 64, 4096);
            s.MaximumLogSizeMb = Clamp(s.MaximumLogSizeMb, 1, 1024);
            s.SessionTimeoutMinutes = Clamp(s.SessionTimeoutMinutes, 0, 1440);
            s.CardRadius = Clamp(s.CardRadius, 6, 32);
            s.SidebarWidth = Math.Clamp(s.SidebarWidth, 260, 520);
            s.AiTokenLimit = Clamp(s.AiTokenLimit, 100, 4000);
            s.AiTimeoutSeconds = Clamp(s.AiTimeoutSeconds <= 0 ? 60 : s.AiTimeoutSeconds, 5, 120);
            s.AgentPort = Clamp(s.AgentPort <= 0 ? 9841 : s.AgentPort, 1024, 65535);
            s.AgentScheme = string.Equals(s.AgentScheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
            s.VisionTimeoutSeconds = Clamp(s.VisionTimeoutSeconds <= 0 ? 30 : s.VisionTimeoutSeconds, 5, 120);
            if (string.IsNullOrWhiteSpace(s.VisionProvider))
                s.VisionProvider = s.AiProvider;
            if (string.IsNullOrWhiteSpace(s.VisionModel))
                s.VisionModel = "gpt-4o-mini";

            if (settings == null)
                _current = s;

            return errors;
        }

        public async Task SaveAsync(AppSettings? settings = null)
        {
            await _saveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (settings != null)
                    _current = settings.Clone();

                var errors = Validate(_current);
                if (errors.Count > 0)
                    throw new InvalidOperationException(string.Join(" ", errors));

                await BackupAsync().ConfigureAwait(false);
                var savedSettings = _current.Clone();
                var json = JsonSerializer.Serialize(savedSettings, JsonOptions);
                var path = GetSettingsPath();
                await File.WriteAllTextAsync(path + ".tmp", json).ConfigureAwait(false);
                if (File.Exists(path))
                    File.Replace(path + ".tmp", path, GetBackupPath(), true);
                else
                    File.Move(path + ".tmp", path);

                Helpers.SettingsRuntime.Sync(savedSettings);
                SettingsChanged?.Invoke(this, savedSettings.Clone());
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.Error("Ayarlar kaydedilemedi.", ex);
                throw;
            }
            finally
            {
                _saveGate.Release();
            }
        }

        public Task BackupAsync()
        {
            if (!File.Exists(GetSettingsPath()))
                return Task.CompletedTask;

            File.Copy(GetSettingsPath(), GetBackupPath(), true);
            return Task.CompletedTask;
        }

        public async Task RestoreFromBackupAsync()
        {
            if (!File.Exists(GetBackupPath()))
                throw new FileNotFoundException("No settings backup found.");

            File.Copy(GetBackupPath(), GetSettingsPath(), true);
            await LoadAsync();
            await SaveAsync();
        }

        public void Replace(AppSettings settings)
        {
            _current = settings.Clone();
            Validate(_current);
            Helpers.SettingsRuntime.Sync(_current);
            SettingsChanged?.Invoke(this, _current.Clone());
        }

        public AppSettings Reset()
        {
            _current = AppSettings.CreateDefault();
            Validate(_current);
            return _current.Clone();
        }

        public async Task ExportAsync(string targetPath)
        {
            var export = new
            {
                ExportedAt = DateTime.UtcNow,
                Settings = _current
            };
            var json = JsonSerializer.Serialize(export, JsonOptions);
            await File.WriteAllTextAsync(targetPath, json);
        }

        public async Task ImportAsync(string sourcePath, bool merge)
        {
            var rollback = _current.Clone();
            try
            {
                var json = await File.ReadAllTextAsync(sourcePath);
                using var doc = JsonDocument.Parse(json);
                AppSettings? imported = null;

                if (doc.RootElement.TryGetProperty("settings", out var settingsEl))
                    imported = JsonSerializer.Deserialize<AppSettings>(settingsEl.GetRawText(), JsonOptions);
                else
                    imported = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

                if (imported == null)
                    throw new InvalidOperationException("Invalid settings file.");

                if (merge)
                {
                    var merged = _current.Clone();
                    CopyNonDefault(imported, merged);
                    _current = merged;
                }
                else
                {
                    _current = imported;
                }

                Validate(_current);
                SettingsMigration.Migrate(_current);
                await SaveAsync();
            }
            catch
            {
                _current = rollback;
                throw;
            }
        }

        private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

        private static void CopyNonDefault(AppSettings source, AppSettings target)
        {
            var defaults = AppSettings.CreateDefault();
            foreach (var prop in typeof(AppSettings).GetProperties())
            {
                if (!prop.CanRead || !prop.CanWrite)
                    continue;

                var value = prop.GetValue(source);
                var defaultValue = prop.GetValue(defaults);
                if (!Equals(value, defaultValue))
                    prop.SetValue(target, value);
            }
        }
    }
}
