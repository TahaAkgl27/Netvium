using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Helpers;
using Netvium.Models.Dashboard;

namespace Netvium.Services
{
    public sealed class DashboardLayoutService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _dashboardDir;
        private CancellationTokenSource? _saveDebounceCts;

        public DashboardLayoutService()
        {
            _dashboardDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Netvium",
                "dashboard");
            Directory.CreateDirectory(_dashboardDir);
        }

        public static bool HasRenderableWidgets(DashboardLayoutDocument? document)
        {
            if (document?.Workspaces is not { Count: > 0 })
                return false;

            return document.Workspaces.Any(ws => ws.Widgets.Any(w => w.IsVisible));
        }

        public DashboardLayoutDocument? TryLoadSavedLayout(string serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey))
                return null;

            var serverDoc = TryLoadFile(GetServerLayoutPath(serverKey));
            if (HasRenderableWidgets(serverDoc))
                return serverDoc;

            var globalDoc = TryLoadFile(GetGlobalLayoutPath());
            if (HasRenderableWidgets(globalDoc))
                return globalDoc;

            return null;
        }

        public DashboardLayoutDocument LoadOrCreateDefault(string? serverKey = null) =>
            TryLoadSavedLayout(serverKey ?? string.Empty)
            ?? DashboardWidgetRegistry.CreateDefaultDashboardLayout(serverKey);

        public void Save(string serverKey, DashboardLayoutDocument document)
        {
            if (string.IsNullOrWhiteSpace(serverKey))
                return;

            try
            {
                document.ServerKey = serverKey;
                var json = JsonSerializer.Serialize(document, JsonOptions);
                File.WriteAllText(GetServerLayoutPath(serverKey), json);
            }
            catch
            {
                // Non-fatal persistence failure.
            }
        }

        public Task FlushAsync(string serverKey, DashboardLayoutDocument document)
        {
            if (string.IsNullOrWhiteSpace(serverKey))
                return Task.CompletedTask;

            try
            {
                _saveDebounceCts?.Cancel();
                _saveDebounceCts?.Dispose();
                _saveDebounceCts = null;
                Save(serverKey, document);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Dashboard layout flush failed.", ex, category: "Dashboard", action: "Flush");
            }

            return Task.CompletedTask;
        }

        public void ScheduleAutoSave(string serverKey, DashboardLayoutDocument document, int delayMs = 400)
        {
            if (string.IsNullOrWhiteSpace(serverKey))
                return;

            _saveDebounceCts?.Cancel();
            _saveDebounceCts = new CancellationTokenSource();
            var token = _saveDebounceCts.Token;
            var key = serverKey;
            var snapshot = document;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, token);
                    if (!token.IsCancellationRequested)
                        Save(key, snapshot);
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        public string ExportToJson(DashboardLayoutDocument document) =>
            JsonSerializer.Serialize(document, JsonOptions);

        public DashboardLayoutDocument ImportFromJson(string json)
        {
            var doc = JsonSerializer.Deserialize<DashboardLayoutDocument>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Invalid dashboard layout JSON.");
            if (doc.Workspaces is not { Count: > 0 })
                throw new InvalidOperationException("Layout must contain at least one workspace.");
            return doc;
        }

        public void DeleteServerLayout(string serverKey)
        {
            try
            {
                var path = GetServerLayoutPath(serverKey);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        public void SaveGlobalLayout(DashboardLayoutDocument document)
        {
            try
            {
                var json = JsonSerializer.Serialize(document, JsonOptions);
                File.WriteAllText(GetGlobalLayoutPath(), json);
            }
            catch
            {
            }
        }

        private DashboardLayoutDocument? TryLoadFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var json = File.ReadAllText(path);
                var doc = JsonSerializer.Deserialize<DashboardLayoutDocument>(json, JsonOptions);
                if (doc?.Workspaces is not { Count: > 0 })
                    return null;

                // Layouts saved before the current redesign are intentionally
                // discarded so the new default layout takes over automatically.
                if (doc.Version < DashboardWidgetRegistry.CurrentSchemaVersion)
                    return null;

                if (string.IsNullOrWhiteSpace(doc.ActiveWorkspaceId))
                    doc.ActiveWorkspaceId = doc.Workspaces[0].Id;

                return doc;
            }
            catch
            {
                return null;
            }
        }

        private string GetServerLayoutPath(string serverKey)
        {
            var safe = SanitizeKey(serverKey);
            return Path.Combine(_dashboardDir, $"layout-{safe}.json");
        }

        private string GetGlobalLayoutPath() => Path.Combine(_dashboardDir, "layout-global.json");

        private static string SanitizeKey(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return key.Replace(':', '_').Replace('\\', '_').Replace('/', '_');
        }
    }
}
