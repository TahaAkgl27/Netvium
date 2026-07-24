using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netvium.Agent;

internal sealed class AgentRuntimeState : IDisposable
{
    private readonly SemaphoreSlim _metricsGate = new(1, 1);
    private int _stopping;
    private int _metricsCollectorReady;
    private DateTimeOffset? _lastSuccessfulMetricsUtc;
    private int _degraded;

    public AgentRuntimeState(AgentRuntimeOptions options)
    {
        ConfigValid = options.IsValid;
        SharedKeyConfigured = !string.IsNullOrWhiteSpace(options.SharedKey);
        _metricsCollectorReady = ConfigValid && SharedKeyConfigured ? 1 : 0;
    }

    public bool ConfigValid { get; }
    public bool SharedKeyConfigured { get; }
    public bool IsStopping => Volatile.Read(ref _stopping) != 0;
    public bool MetricsCollectorReady => Volatile.Read(ref _metricsCollectorReady) != 0;
    public DateTimeOffset? LastSuccessfulMetricsUtc => _lastSuccessfulMetricsUtc;
    public bool Degraded => Volatile.Read(ref _degraded) != 0;

    public void BeginShutdown() => Interlocked.Exchange(ref _stopping, 1);

    public object GetHealth()
    {
        return new
        {
            processRunning = !IsStopping,
            metricsCollectorReady = MetricsCollectorReady && !IsStopping,
            configValid = ConfigValid,
            sharedKeyConfigured = SharedKeyConfigured,
            lastSuccessfulMetricsUtc = LastSuccessfulMetricsUtc,
            degraded = Degraded || IsStopping
        };
    }

    public async Task<(bool Accepted, object? Snapshot)> CollectMetricsAsync(
        Func<object> collector,
        CancellationToken cancellationToken)
    {
        if (IsStopping || !MetricsCollectorReady)
            return (false, null);

        if (!await _metricsGate.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false))
            return (false, null);

        try
        {
            if (IsStopping)
                return (false, null);

            var snapshot = collector();
            _lastSuccessfulMetricsUtc = DateTimeOffset.UtcNow;
            Volatile.Write(ref _degraded, 0);
            return (true, snapshot);
        }
        catch
        {
            Volatile.Write(ref _degraded, 1);
            throw;
        }
        finally
        {
            _metricsGate.Release();
        }
    }

    public void Dispose() => _metricsGate.Dispose();
}