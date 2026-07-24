using Netvium.Services;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class ShutdownPersistenceCoordinatorTests
{
    [Fact]
    public async Task RunOnceAsync_ConcurrentCalls_RunOperationOnlyOnce()
    {
        var invocationCount = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ShutdownPersistenceCoordinator(
            async () =>
            {
                Interlocked.Increment(ref invocationCount);
                await release.Task;
            },
            TimeSpan.FromSeconds(2));

        var first = coordinator.RunOnceAsync();
        var second = coordinator.RunOnceAsync();
        release.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, invocationCount);
        Assert.All(results, result => Assert.Equal(ShutdownPersistenceStatus.Succeeded, result.Status));
        Assert.Same(first, second);
    }

    [Fact]
    public async Task RunOnceAsync_WhenOperationExceedsLimit_ReturnsTimedOut()
    {
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ShutdownPersistenceCoordinator(
            () => neverCompletes.Task,
            TimeSpan.FromMilliseconds(25));

        var result = await coordinator.RunOnceAsync();

        Assert.Equal(ShutdownPersistenceStatus.TimedOut, result.Status);
        Assert.IsType<TimeoutException>(result.Exception);
    }

    [Fact]
    public async Task RunOnceAsync_WhenOperationFails_ReturnsFailure()
    {
        var expected = new InvalidOperationException("save failed");
        var coordinator = new ShutdownPersistenceCoordinator(
            () => Task.FromException(expected),
            TimeSpan.FromSeconds(1));

        var result = await coordinator.RunOnceAsync();

        Assert.Equal(ShutdownPersistenceStatus.Failed, result.Status);
        Assert.Same(expected, result.Exception);
    }
}
