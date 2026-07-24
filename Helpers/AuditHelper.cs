using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    public static class AuditHelper
    {
        public static void Rdp(
            string action,
            string result,
            ServerInfo? server = null,
            string? sessionId = null,
            long? durationMs = null,
            string? errorMessage = null,
            string level = Logging.LogLevels.Information)
        {
            AuditLogService.Log(
                action,
                AuditCategories.Rdp,
                result,
                level,
                server?.Name,
                server?.Ip,
                sessionId,
                durationMs,
                errorMessage);
        }

        public static void RemoteAdmin(
            string action,
            string result,
            ServerInfo? server = null,
            long? durationMs = null,
            string? errorMessage = null,
            string level = Logging.LogLevels.Security)
        {
            AuditLogService.Log(
                action,
                AuditCategories.RemoteAdmin,
                result,
                level,
                server?.Name,
                server?.Ip,
                null,
                durationMs,
                errorMessage);
        }

        public static void Configuration(string action, string result, string? errorMessage = null)
        {
            AuditLogService.Log(
                action,
                AuditCategories.Configuration,
                result,
                Logging.LogLevels.Information,
                errorMessage: errorMessage);
        }

        public static void Analysis(string action, string result, ServerInfo? server = null, string? errorMessage = null)
        {
            AuditLogService.Log(
                action,
                AuditCategories.Analysis,
                result,
                Logging.LogLevels.Information,
                server?.Name,
                server?.Ip,
                errorMessage: errorMessage);
        }

        public static void DataTransfer(string action, string result, string? errorMessage = null)
        {
            AuditLogService.Log(
                action,
                AuditCategories.Data,
                result,
                Logging.LogLevels.Security,
                errorMessage: errorMessage);
        }
    }
}
