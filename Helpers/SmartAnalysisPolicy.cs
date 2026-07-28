using Netvium.Models;

namespace Netvium.Helpers
{
    public static class SmartAnalysisPolicy
    {
        public const int MinimumSamples = 3;
        public static readonly TimeSpan SustainedWindow = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
        public const double CpuWarning = 80;
        public const double CpuCritical = 95;
        public const double RamWarning = 85;
        public const double RamCritical = 95;
        public const double DiskWarning = 80;
        public const double DiskCritical = 90;

        public static MetricTrendAssessment EvaluateCpu(List<WmiSnapshot>? history, WmiSnapshot current) =>
            Evaluate("CPU", history, current, s => s.CpuPct, CpuWarning, CpuCritical);
        public static MetricTrendAssessment EvaluateRam(List<WmiSnapshot>? history, WmiSnapshot current) =>
            Evaluate("RAM", history, current, s => s.RamUsedPct, RamWarning, RamCritical);
        public static MetricTrendAssessment EvaluateDisk(List<WmiSnapshot>? history, WmiSnapshot current) =>
            Evaluate("Disk", history, current, s => s.DiskUsedPct, DiskWarning, DiskCritical);

        private static MetricTrendAssessment Evaluate(string metric, List<WmiSnapshot>? history,
            WmiSnapshot current, Func<WmiSnapshot, double?> selector, double warning, double critical)
        {
            var now = current.CollectedAt == default ? DateTime.Now : current.CollectedAt;
            var currentValue = selector(current);
            var samples = (history ?? new List<WmiSnapshot>()).Append(current)
                .Where(s => s.CollectedAt != default)
                .Where(s => now - s.CollectedAt <= SustainedWindow && now - s.CollectedAt >= TimeSpan.Zero)
                .GroupBy(s => s.CollectedAt)
                .Select(group => group.Last())
                .Select(s => new MetricSample(s.CollectedAt, selector(s)))
                .Where(x => x.Value.HasValue).OrderBy(x => x.CollectedAt).ToList();

            if (!currentValue.HasValue)
                return Result(metric, MetricTrendState.Unknown, null, samples, warning, critical, "Metrik okunamadı.");

            if (samples.Count < MinimumSamples)
            {
                var state = currentValue.Value >= critical ? MetricTrendState.CriticalSpike
                    : currentValue.Value >= warning ? MetricTrendState.InsufficientHistory
                    : MetricTrendState.Normal;
                return Result(metric, state, currentValue, samples, warning, critical,
                    $"Sustained değerlendirme için en az {MinimumSamples} örnek gerekir; {samples.Count} örnek var.");
            }

            var warningCount = samples.Count(s => s.Value >= warning);
            var criticalCount = samples.Count(s => s.Value >= critical);
            var required = (int)Math.Ceiling(samples.Count * 2d / 3d);
            var stateWithHistory = criticalCount >= required ? MetricTrendState.SustainedCritical
                : warningCount >= required ? MetricTrendState.SustainedWarning
                : currentValue.Value >= critical ? MetricTrendState.CriticalSpike
                : MetricTrendState.Normal;
            return Result(metric, stateWithHistory, currentValue, samples, warning, critical,
                $"{samples.Count} örneğin {warningCount} tanesi warning, {criticalCount} tanesi critical eşiğinde.");
        }

        private static MetricTrendAssessment Result(string metric, MetricTrendState state, double? current,
            IReadOnlyList<MetricSample> samples, double warning, double critical, string detail)
        {
            var window = samples.Count >= 2 ? samples[^1].CollectedAt - samples[0].CollectedAt : TimeSpan.Zero;
            return new MetricTrendAssessment
            {
                Metric = metric,
                State = state,
                CurrentValue = current,
                AvailableSamples = samples.Count,
                SamplesAboveWarning = samples.Count(s => s.Value >= warning),
                SamplesAboveCritical = samples.Count(s => s.Value >= critical),
                CoveredWindow = window,
                Evidence = $"{detail} Pencere: {window.TotalMinutes:0.#} dk."
            };
        }

        private sealed record MetricSample(DateTime CollectedAt, double? Value);
    }
}
