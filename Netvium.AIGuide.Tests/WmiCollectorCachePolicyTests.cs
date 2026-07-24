using Netvium.Helpers;
using Netvium.Models;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class WmiCollectorCachePolicyTests
{
    [Fact]
    public void ReusableDiskCache_RejectsStaleSnapshot()
    {
        var now = DateTime.UtcNow;
        var snapshot = DisplayableSnapshot(now.AddMinutes(-10));

        Assert.False(WmiCollector.IsReusableDiskCache(snapshot, now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ReusableDiskCache_AcceptsFreshDisplayableSnapshot()
    {
        var now = DateTime.UtcNow;
        var snapshot = DisplayableSnapshot(now.AddSeconds(-5));

        Assert.True(WmiCollector.IsReusableDiskCache(snapshot, now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ReusableDiskCache_RejectsConnectedButEmptySnapshot()
    {
        var now = DateTime.UtcNow;
        var snapshot = new WmiSnapshot { Connected = true, CollectedAt = now };

        Assert.False(WmiCollector.IsReusableDiskCache(snapshot, now, TimeSpan.FromSeconds(30)));
    }

    private static WmiSnapshot DisplayableSnapshot(DateTime collectedAt) => new()
    {
        Connected = true,
        CollectedAt = collectedAt,
        CpuPct = 25
    };
}
