namespace Netvium.Services.AIGuide
{
    internal static class AIGuideStageTimeouts
    {
        public static readonly TimeSpan Capture = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan SmartSummary = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan MentorDashboardBuild = TimeSpan.FromSeconds(2);

        public static async Task<T> RunAsync<T>(
            Func<CancellationToken, Task<T>> action,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string stageName)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                return await action(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{stageName} timed out after {timeout.TotalSeconds:0} seconds.");
            }
        }
    }
}
