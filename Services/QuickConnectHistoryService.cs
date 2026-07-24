using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Netvium.Helpers;

namespace Netvium.Services
{
    public static class QuickConnectHistoryService
    {
        private const int MaxItems = 10;

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private static string GetPath() =>
            Path.Combine(SettingsService.GetSettingsDirectory(), "quickconnect-history.json");

        public static async Task<List<string>> LoadAsync()
        {
            try
            {
                var path = GetPath();
                if (!File.Exists(path))
                    return new List<string>();

                var json = await File.ReadAllTextAsync(path);
                var items = JsonSerializer.Deserialize<List<string>>(json);
                return items?
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxItems)
                    .ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load quick connect history.", ex, category: "Storage", action: "QuickConnectHistory");
                return new List<string>();
            }
        }

        public static async Task RecordHostAsync(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            var trimmed = host.Trim();
            var items = await LoadAsync();
            items.RemoveAll(h => string.Equals(h, trimmed, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, trimmed);
            if (items.Count > MaxItems)
                items = items.Take(MaxItems).ToList();

            try
            {
                var json = JsonSerializer.Serialize(items, JsonOpts);
                await File.WriteAllTextAsync(GetPath(), json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save quick connect history.", ex, category: "Storage", action: "QuickConnectHistory");
            }
        }
    }
}
