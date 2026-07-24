using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;
using Netvium.Helpers.Logging;
using Netvium.Services;

namespace Netvium.Helpers
{
    public enum PerfChartTimeRange
    {
        /// <summary>Last 60 seconds of live session.</summary>
        OneMinute = 60,
        /// <summary>Last 5 minutes of live session.</summary>
        FiveMinutes = 300,
        /// <summary>Last 15 minutes of live session.</summary>
        FifteenMinutes = 900,
        /// <summary>Last 1 hour of live session.</summary>
        OneHour = 3600
    }

    /// <summary>
    /// Live-only metric samples for dashboard charts (never fed from snapshot history).
    /// </summary>
    public sealed class LiveMetricChartSession
    {
        private readonly List<MetricSample> _samples = new();
        private string? _serverKey;
        private bool _isActive;
        private PerfChartTimeRange _timeRange = PerfChartTimeRange.OneMinute;

        public int SampleCount => GetFilteredSamples().Count;
        public bool IsActive => _isActive;
        public PerfChartTimeRange TimeRange => _timeRange;
        public IReadOnlyList<MetricSample> Samples => GetFilteredSamples();

        public void SetTimeRange(PerfChartTimeRange range) => _timeRange = range;

        public void Reset(string? serverKey)
        {
            _samples.Clear();
            _serverKey = serverKey;
            _isActive = false;
        }

        public bool TryAddLiveSample(LiveDashboardMetrics metrics, string activeServerKey, string chartServerKey)
        {
            if (!metrics.IsLive)
                return false;
            if (!string.Equals(activeServerKey, chartServerKey, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(_serverKey, chartServerKey, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!metrics.HasCpu && !metrics.HasRam && !metrics.HasDisk && !metrics.HasNetwork)
                return false;

            var sample = new MetricSample
            {
                TimestampUtc = metrics.TimestampUtc,
                CpuPercent = metrics.CpuPercent,
                RamPercent = metrics.RamPercent,
                DiskPercent = metrics.DiskPercent,
                NetworkInBps = metrics.NetworkInBps,
                NetworkOutBps = metrics.NetworkOutBps
            };

            if (_samples.Count > 0)
            {
                var last = _samples[^1];
                var elapsedMs = (sample.TimestampUtc - last.TimestampUtc).TotalMilliseconds;
                if (elapsedMs < 100)
                    return false;
            }

            _samples.Add(sample);
            _isActive = true;

            var max = Math.Clamp(SettingsService.Instance.Current.MaximumGraphHistory, 30, 500);
            while (_samples.Count > max)
                _samples.RemoveAt(0);

            LogSampleState(chartServerKey, metrics);
            return true;
        }

        public IReadOnlyList<double> GetCpuSeries() =>
            GetFilteredSamples().Where(s => s.CpuPercent.HasValue).Select(s => (double)s.CpuPercent!.Value).ToList();

        public IReadOnlyList<double> GetRamSeries() =>
            GetFilteredSamples().Where(s => s.RamPercent.HasValue).Select(s => s.RamPercent!.Value).ToList();

        public IReadOnlyList<double> GetDiskSeries() =>
            GetFilteredSamples().Where(s => s.DiskPercent.HasValue).Select(s => s.DiskPercent!.Value).ToList();

        public IReadOnlyList<double> GetNetworkSeries()
        {
            var filtered = GetFilteredSamples();
            var combined = filtered
                .Select(s => (s.NetworkInBps ?? 0) + (s.NetworkOutBps ?? 0))
                .Where(v => v > 0)
                .ToList();
            if (combined.Count < 2)
                return combined;

            var max = combined.Max();
            if (max <= 0)
                return combined;
            return combined.Select(v => v / max * 100.0).ToList();
        }

        public IReadOnlyList<MetricSample> GetFilteredSamples()
        {
            if (_samples.Count == 0)
                return _samples;

            var cutoff = DateTime.UtcNow.AddSeconds(-(int)_timeRange);
            var filtered = _samples.Where(s => s.TimestampUtc >= cutoff).ToList();
            return filtered.Count > 0 ? filtered : _samples;
        }

        private void LogSampleState(string serverKey, LiveDashboardMetrics metrics)
        {
            var filtered = GetFilteredSamples();
            RefreshDebugLog.ChartSampleAdded(
                serverKey,
                metrics.HasCpu,
                metrics.HasRam,
                metrics.HasDisk,
                filtered.Count);
        }
    }
}
