using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Netvium.Models;

namespace Netvium.Services
{
    public sealed class AlertStateStore
    {
        private readonly object _gate = new();
        private readonly string _alertDir;
        private readonly string _activePath;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public AlertStateStore()
        {
            _alertDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Netvium",
                "alerts");
            Directory.CreateDirectory(_alertDir);
            _activePath = Path.Combine(_alertDir, "active-alerts.json");
        }

        public string AlertDirectory => _alertDir;
        public string ActiveAlertsPath => _activePath;

        public List<AlertState> LoadActive()
        {
            try
            {
                if (!File.Exists(_activePath))
                    return new List<AlertState>();

                var json = File.ReadAllText(_activePath, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<AlertState>>(json, _jsonOptions) ?? new List<AlertState>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Alerts] Failed to load active alerts: {ex.Message}");
                return new List<AlertState>();
            }
        }

        public void SaveActive(IReadOnlyCollection<AlertState> alerts)
        {
            _ = SaveActiveAsync(alerts);
        }

        public Task SaveActiveAsync(IReadOnlyCollection<AlertState> alerts)
        {
            return Task.Run(() =>
            {
                try
                {
                    var active = alerts
                        .Where(a => string.Equals(a.Status, AlertStatuses.Active, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(a => a.LastSeen)
                        .ToList();
                    var json = JsonSerializer.Serialize(active, _jsonOptions);
                    lock (_gate)
                    {
                        File.WriteAllText(_activePath, json, Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Alerts] Failed to save active alerts: {ex.Message}");
                }
            });
        }

        public void AppendHistory(AlertState alert)
        {
            _ = AppendHistoryAsync(alert);
        }

        public Task AppendHistoryAsync(AlertState alert)
        {
            return Task.Run(() =>
            {
                try
                {
                    var path = Path.Combine(_alertDir, $"alert-history-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
                    var line = JsonSerializer.Serialize(alert, _jsonOptions) + Environment.NewLine;
                    lock (_gate)
                    {
                        File.AppendAllText(path, line, Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Alerts] Failed to append alert history: {ex.Message}");
                }
            });
        }
    }
}
