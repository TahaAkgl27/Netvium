using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    public static class SettingsApplicator
    {
        public static void ApplyAll(Window? window, AppSettings settings)
        {
            SettingsRuntime.Sync(settings);
            StartupHelper.SetEnabled(settings.StartWithWindows);
            AppLogger.Configure(settings);
            AuditLogService.Configure(settings.AuditLogging);

            if (window != null)
            {
                ThemeService.Apply(window, settings);
                LocalizationService.Apply(window, settings);
                ApplyHardwareAcceleration(window, settings);
                ApplyAdministratorWarning(window, settings);
            }

            InsightEngine.Configure(settings);
            AppLogger.Info("Settings applied.");
        }

        public static void ApplyHardwareAcceleration(Window window, AppSettings settings)
        {
            try
            {
                if (!settings.HardwareAcceleration || !settings.GpuRendering)
                {
                    RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                    return;
                }

                RenderOptions.ProcessRenderMode = RenderMode.Default;
                var source = PresentationSource.FromVisual(window) as HwndSource;
                if (source?.CompositionTarget != null)
                    source.CompositionTarget.RenderMode = RenderMode.Default;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        public static void ApplyAdministratorWarning(Window window, AppSettings settings)
        {
            if (!settings.RequireAdministratorPrivileges)
                return;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                    return;

                System.Windows.MessageBox.Show(
                    window,
                    "Netvium is not running with administrator privileges. Some remote operations may fail.",
                    "Administrator Privileges",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        public static (string Username, string Password) ResolveCredentials(ServerInfo server, AppSettings settings)
        {
            if (settings.WindowsAuthentication)
            {
                return (Environment.UserName, string.Empty);
            }

            if (settings.CredentialManagerEnabled)
            {
                var stored = WindowsCredentialHelper.Load(server.Ip, server.Port);
                if (stored != null)
                    return (stored.Value.Username, stored.Value.Password);
            }

            return (server.Username, server.Password);
        }

        public static void PersistServerCredentials(ServerInfo server, AppSettings settings)
        {
            if (!settings.CredentialManagerEnabled)
                return;

            WindowsCredentialHelper.Save(server.Ip, server.Port, server.Username, server.Password);
        }

        public static void OptimizeMemoryIfEnabled(AppSettings settings)
        {
            if (!settings.MemoryOptimization)
                return;

            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        public static void EnforceCacheSizeLimit(AppSettings settings)
        {
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Netvium", "cache");
            if (!Directory.Exists(cacheRoot))
                return;

            var files = Directory.GetFiles(cacheRoot, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            long totalMb = files.Sum(f => f.Length) / (1024 * 1024);
            while (totalMb > settings.CacheSizeMb && files.Count > 0)
            {
                var oldest = files[^1];
                try
                {
                    totalMb -= oldest.Length / (1024 * 1024);
                    oldest.Delete();
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                files.RemoveAt(files.Count - 1);
            }
        }

        public static void PlayNotificationSound(AppSettings settings)
        {
            if (!settings.SoundAlerts || !settings.PlaySound || settings.MuteAll)
                return;

            try { SystemSounds.Asterisk.Play(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        public static (string Version, string Build, string Runtime, string Os, string Machine) GetAboutInfo()
        {
            var asm = Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version;
            var versionText = version == null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            var build = version?.Revision > 0 ? version.Revision.ToString() : File.GetLastWriteTime(asm.Location).ToString("yyyyMMdd");
            return (versionText, build, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, Environment.MachineName);
        }

        public static void ClearCache(AppSettings settings)
        {
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Netvium", "cache");
            if (Directory.Exists(cacheRoot))
            {
                foreach (var file in Directory.GetFiles(cacheRoot, "*.json"))
                {
                    try { File.Delete(file); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                }
            }

            WmiCollector.ClearMemoryCache();
            AppLogger.Info($"Cache cleared (limit {settings.CacheSizeMb} MB).");
        }

        public static void ClearAiCache()
        {
            InsightEngine.ClearAllAiCooldowns();
            AppLogger.Info("AI cache cleared.");
        }

        public static void OpenLogFolder()
        {
            Process.Start(new ProcessStartInfo { FileName = SettingsService.GetLogsDirectory(), UseShellExecute = true });
        }

        public static void ExportLogs(string targetPath)
        {
            if (File.Exists(AppLogger.LogFilePath))
                File.Copy(AppLogger.LogFilePath, targetPath, true);
        }

        public static void DeleteLogs(bool secure, AppSettings settings)
        {
            foreach (var file in Directory.GetFiles(SettingsService.GetLogsDirectory()))
            {
                if (secure) CredentialProtector.SecureDeleteFile(file);
                else File.Delete(file);
            }
            AppLogger.Info(secure ? "Logs securely deleted." : "Logs deleted.");
        }

        public static void OpenCertificates()
        {
            Process.Start(new ProcessStartInfo { FileName = "certmgr.msc", UseShellExecute = true });
        }

        public static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        public static string? ResolveAiApiKey(AppSettings settings)
        {
            if (settings.AiProvider == "Azure OpenAI")
            {
                var azureKey = Environment.GetEnvironmentVariable("NETVIUM_AZURE_OPENAI_KEY");
                if (!string.IsNullOrWhiteSpace(azureKey))
                    return azureKey.Trim();
            }
            else
            {
                var openAiKey = Environment.GetEnvironmentVariable("NETVIUM_OPENAI_KEY");
                if (!string.IsNullOrWhiteSpace(openAiKey))
                    return openAiKey.Trim();
            }

            if (string.IsNullOrWhiteSpace(settings.AiApiKeyProtected))
                return null;

            return settings.EnableEncryption
                ? CredentialProtector.Unprotect(settings.AiApiKeyProtected)
                : settings.AiApiKeyProtected;
        }

        public static string? ResolveVisionApiKey(AppSettings settings)
        {
            var envKey = Environment.GetEnvironmentVariable("NETVIUM_VISION_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
                return envKey.Trim();

            if (!string.IsNullOrWhiteSpace(settings.VisionApiKeyProtected))
            {
                return settings.EnableEncryption
                    ? CredentialProtector.Unprotect(settings.VisionApiKeyProtected)
                    : settings.VisionApiKeyProtected;
            }

            return ResolveAiApiKey(settings);
        }

        public static string? ResolveAgentMetricsKey(AppSettings settings)
        {
            var envKey = Environment.GetEnvironmentVariable("NETVIUM_AGENT_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
                return envKey.Trim();

            if (string.IsNullOrWhiteSpace(settings.AgentMetricsKeyProtected))
                return null;

            return settings.EnableEncryption
                ? CredentialProtector.Unprotect(settings.AgentMetricsKeyProtected)
                : settings.AgentMetricsKeyProtected;
        }

        public static string ProtectSensitiveValue(string value, bool enableEncryption)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return enableEncryption ? CredentialProtector.Protect(value) : value;
        }
    }
}
