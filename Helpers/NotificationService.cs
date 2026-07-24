using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class NotificationService
    {
        public enum AlertLevel { Info, Warning, Critical }
        private static readonly object DesktopGate = new();
        private static readonly HashSet<NotifyIcon> ActiveDesktopIcons = new();


        public static bool ShouldShow(AlertLevel level, AppSettings settings)
        {
            if (!settings.EnableNotifications)
                return false;

            return level switch
            {
                AlertLevel.Critical => settings.CriticalAlerts,
                AlertLevel.Warning => settings.WarningAlerts,
                _ => settings.InformationAlerts
            };
        }

        public static void ShowDesktop(string title, string message, AppSettings settings, AlertLevel level = AlertLevel.Info)
        {
            if (!settings.DesktopNotifications || !settings.EnableNotifications)
                return;

            NotifyIcon? icon = null;
            try
            {
                icon = new NotifyIcon
                {
                    Visible = true,
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                        System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!)
                };
                lock (DesktopGate)
                    ActiveDesktopIcons.Add(icon);

                var timeoutMs = Math.Clamp(settings.NotificationDurationMs, 1000, 15000);
                var desktopIcon = level switch
                {
                    AlertLevel.Critical => ToolTipIcon.Error,
                    AlertLevel.Warning => ToolTipIcon.Warning,
                    _ => ToolTipIcon.Info
                };
                icon.ShowBalloonTip(timeoutMs, title, message, desktopIcon);
                _ = ReleaseDesktopIconAfterDelayAsync(icon, timeoutMs);
            }
            catch (Exception ex)
            {
                ReleaseDesktopIcon(icon);
                AppLogger.Warning($"Desktop notification failed: {ex.Message}");
            }
        }

        private static async Task ReleaseDesktopIconAfterDelayAsync(NotifyIcon icon, int timeoutMs)
        {
            await Task.Delay(timeoutMs + 1000).ConfigureAwait(false);
            ReleaseDesktopIcon(icon);
        }

        private static void ReleaseDesktopIcon(NotifyIcon? icon)
        {
            if (icon == null)
                return;

            lock (DesktopGate)
                ActiveDesktopIcons.Remove(icon);

            try
            {
                icon.Visible = false;
                icon.Dispose();
            }
            catch
            {
                // Best effort cleanup for a native notification icon.
            }
        }

        public static void Test(AppSettings settings, Action<string> inAppToast)
        {
            inAppToast(LocalizationService.T("Test notification delivered.", "Test bildirimi gönderildi."));
        }
    }
}
