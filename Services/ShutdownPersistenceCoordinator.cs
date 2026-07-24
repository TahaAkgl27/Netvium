using System;
using System.Threading.Tasks;

namespace Netvium.Services
{
    internal enum ShutdownPersistenceStatus
    {
        Succeeded,
        TimedOut,
        Failed
    }

    internal sealed record ShutdownPersistenceResult(
        ShutdownPersistenceStatus Status,
        Exception? Exception = null);

    internal sealed class ShutdownPersistenceCoordinator
    {
        private readonly Func<Task> _operation;
        private readonly TimeSpan _timeout;
        private readonly object _gate = new();
        private Task<ShutdownPersistenceResult>? _completion;

        public ShutdownPersistenceCoordinator(Func<Task> operation, TimeSpan timeout)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            _timeout = timeout;
        }

        public Task<ShutdownPersistenceResult> RunOnceAsync()
        {
            lock (_gate)
                return _completion ??= RunCoreAsync();
        }

        private async Task<ShutdownPersistenceResult> RunCoreAsync()
        {
            Task operationTask;
            try
            {
                operationTask = _operation();
            }
            catch (Exception ex)
            {
                return new ShutdownPersistenceResult(ShutdownPersistenceStatus.Failed, ex);
            }

            try
            {
                await operationTask.WaitAsync(_timeout).ConfigureAwait(false);
                return new ShutdownPersistenceResult(ShutdownPersistenceStatus.Succeeded);
            }
            catch (TimeoutException ex)
            {
                _ = operationTask.ContinueWith(
                    static task => _ = task.Exception,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                return new ShutdownPersistenceResult(ShutdownPersistenceStatus.TimedOut, ex);
            }
            catch (Exception ex)
            {
                return new ShutdownPersistenceResult(ShutdownPersistenceStatus.Failed, ex);
            }
        }
    }
}
