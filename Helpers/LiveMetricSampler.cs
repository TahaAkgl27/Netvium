using System;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Helpers.Logging;
using Netvium.Models;

namespace Netvium.Helpers
{
    /// <summary>
    /// Post-refresh live metric polling for chart trends and network delta (5 x 2s).
    /// </summary>
    public sealed class LiveMetricSampler
    {
        public const int SampleCount = 5;
        public const int IntervalMs = 2000;

        private CancellationTokenSource? _cts;

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
        }

        public void Start(LiveMetricSamplingContext context)
        {
            Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = RunAsync(context, token);
        }

        private static async Task RunAsync(LiveMetricSamplingContext context, CancellationToken ct)
        {
            for (var i = 0; i < SampleCount && !ct.IsCancellationRequested; i++)
            {
                if (!context.IsStillActive())
                    return;

                try
                {
                    var snap = await WmiCollector.GetLightweightMetricsAsync(
                        context.Server.Ip,
                        context.Server.Username,
                        context.Server.Password,
                        ct).ConfigureAwait(false);

                    if (snap != null && context.IsStillActive())
                        await context.OnSampleAsync(snap, context.LatencyMs).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Warning("Live metric sample failed.", ex, category: "Dashboard", action: "LiveSample");
                }

                if (i < SampleCount - 1 && !ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(IntervalMs, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }

    public sealed class LiveMetricSamplingContext
    {
        public required ServerInfo Server { get; init; }
        public required string ServerKey { get; init; }
        public long? LatencyMs { get; init; }
        public required Func<bool> IsStillActive { get; init; }
        public required Func<WmiSnapshot, long?, Task> OnSampleAsync { get; init; }
    }
}
