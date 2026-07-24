using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class SnapshotTrendAnalyzer
    {
        public static SnapshotTrendData Analyze(List<WmiSnapshot>? history, WmiSnapshot? current, int? healthScore = null)
        {
            var samples = (history ?? new List<WmiSnapshot>())
                .Where(h => h.CollectedAt != default)
                .OrderBy(h => h.CollectedAt)
                .ToList();

            if (current != null)
                samples.Add(current);

            var data = new SnapshotTrendData { SampleCount = samples.Count };

            if (samples.Count < 2)
            {
                data.CpuTrend = "Yetersiz geçmiş";
                data.RamTrend = "Yetersiz geçmiş";
                data.DiskTrend = "Yetersiz geçmiş";
                data.EventTrend = "Yetersiz geçmiş";
                data.RiskTrend = "Yetersiz geçmiş";
                data.HealthTrend = healthScore.HasValue ? $"Skor: {healthScore}/100" : "Yetersiz geçmiş";
                return data;
            }

            data.CpuTrend = DescribeTrend(samples.Select(s => s.CpuPct).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList(), "%", "CPU");
            data.RamTrend = DescribeTrend(samples.Select(s => s.RamUsedPct).Where(v => v.HasValue).Select(v => v!.Value).ToList(), "%", "RAM");
            data.DiskTrend = DescribeTrend(samples.Select(s => s.DiskUsedPct).Where(v => v.HasValue).Select(v => v!.Value).ToList(), "%", "Disk");

            var eventCounts = samples.Select(s => s.Timeline?.Count(t =>
                t.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)
                || t.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase)) ?? 0).ToList();
            data.EventTrend = DescribeCountTrend(eventCounts, "uyarı/hata");

            var riskScores = samples.Select(s => EstimateRisk(s)).ToList();
            data.RiskTrend = DescribeTrend(riskScores, "", "Risk", invertGood: true);

            if (healthScore.HasValue)
            {
                var delta = samples.Count >= 2
                    ? healthScore.Value - EstimateHealth(samples[^2])
                    : 0;
                data.HealthTrend = delta switch
                {
                    > 5 => $"İyileşiyor ({healthScore}/100)",
                    < -5 => $"Düşüyor ({healthScore}/100)",
                    _ => $"Stabil ({healthScore}/100)"
                };
            }
            else
            {
                data.HealthTrend = "Skor yok";
            }

            return data;
        }

        private static string DescribeTrend(List<double> values, string unit, string label, bool invertGood = false)
        {
            if (values.Count < 2)
                return "Yetersiz veri";

            var delta = values[^1] - values[0];
            var direction = Math.Abs(delta) < 1 ? "stabil" : delta > 0 ? "yükseliyor" : "düşüyor";
            if (invertGood)
                direction = Math.Abs(delta) < 1 ? "stabil" : delta > 0 ? "artıyor" : "azalıyor";

            return $"{label} {direction} ({values[0]:0}{unit} → {values[^1]:0}{unit})";
        }

        private static string DescribeCountTrend(List<int> counts, string label)
        {
            if (counts.Count < 2)
                return "Yetersiz veri";

            var delta = counts[^1] - counts[0];
            if (delta == 0)
                return $"{label} stabil ({counts[^1]})";
            return delta > 0
                ? $"{label} artıyor ({counts[0]} → {counts[^1]})"
                : $"{label} azalıyor ({counts[0]} → {counts[^1]})";
        }

        private static double EstimateRisk(WmiSnapshot snap)
        {
            double risk = 0;
            if (snap.CpuPct > 85) risk += 25;
            if (snap.RamUsedPct > 85) risk += 25;
            if (snap.DiskUsedPct > 85) risk += 30;
            if (snap.Services?.Any(s => s.IsCritical && s.State != "Running") == true) risk += 20;
            return risk;
        }

        private static int EstimateHealth(WmiSnapshot snap) =>
            Math.Max(0, 100 - (int)EstimateRisk(snap));
    }
}
