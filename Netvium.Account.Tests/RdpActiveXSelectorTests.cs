using Netvium.Rdp;
using Netvium.Rdp.Clean;
using Xunit;

namespace Netvium.Account.Tests;

public sealed class RdpActiveXSelectorTests
{
    [Fact]
    public void V11Success_StopsWithoutTryingLowerVersions()
    {
        var (candidates, instances) = CreateCandidates();
        var logs = new List<string>();

        var selection = RdpActiveXSelector.Select(candidates, _ => { }, candidate => candidate.Dispose(), logs.Add);

        Assert.Equal("MsRdpClient11", selection.Version.Label);
        Assert.Single(instances);
        Assert.False(instances[0].IsDisposed);
        Assert.DoesNotContain(logs, line => line.Contains("MsRdpClient10", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, line => line.Contains("MsRdpClient9", StringComparison.Ordinal));
    }

    [Fact]
    public void V11Fails_V10Succeeds_CleansFailedInstanceAndStops()
    {
        var (candidates, instances) = CreateCandidates();
        var logs = new List<string>();

        var selection = RdpActiveXSelector.Select(
            candidates,
            candidate =>
            {
                if (candidate.Label == "MsRdpClient11")
                    throw new TestComException();
            },
            candidate => candidate.Dispose(),
            logs.Add);

        Assert.Equal("MsRdpClient10", selection.Version.Label);
        Assert.Equal(2, instances.Count);
        Assert.True(instances[0].IsDisposed);
        Assert.False(instances[1].IsDisposed);
        Assert.Contains(logs, line => line.Contains("MsRdpClient11", StringComparison.Ordinal)
            && line.Contains(nameof(TestComException), StringComparison.Ordinal)
            && line.Contains("hresult=0x80040154", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, line => line.Contains("MsRdpClient9", StringComparison.Ordinal));
    }

    [Fact]
    public void V11AndV10Fail_V9Succeeds_CleansBothFailedInstances()
    {
        var (candidates, instances) = CreateCandidates();
        var logs = new List<string>();

        var selection = RdpActiveXSelector.Select(
            candidates,
            candidate =>
            {
                if (candidate.Label is "MsRdpClient11" or "MsRdpClient10")
                    throw new InvalidOperationException("simulated activation failure");
            },
            candidate => candidate.Dispose(),
            logs.Add);

        Assert.Equal("MsRdpClient9", selection.Version.Label);
        Assert.Equal(3, instances.Count);
        Assert.True(instances[0].IsDisposed);
        Assert.True(instances[1].IsDisposed);
        Assert.False(instances[2].IsDisposed);
        Assert.Equal(1, logs.Count(line => line.Contains("attempt succeeded", StringComparison.Ordinal)));
    }

    [Fact]
    public void AllVersionsFail_ReturnsSafeTurkishErrorAndCleansEveryInstance()
    {
        var (candidates, instances) = CreateCandidates();
        var logs = new List<string>();

        var exception = Assert.Throws<RdpActiveXUnavailableException>(() =>
            RdpActiveXSelector.Select(
                candidates,
                _ => throw new InvalidOperationException("COM detail must not reach user"),
                candidate => candidate.Dispose(),
                logs.Add));

        Assert.Contains("Uzak Masaüstü", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("COM detail", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, instances.Count);
        Assert.All(instances, instance => Assert.True(instance.IsDisposed));
        Assert.Equal(3, logs.Count(line => line.Contains("attempt started", StringComparison.Ordinal)));
        Assert.Equal(3, logs.Count(line => line.Contains("attempt cleanup completed", StringComparison.Ordinal)));
    }

    [Fact]
    public void CleanHostOrderAndDiagnosticsLabelUse11Then10Then9()
    {
        Assert.Equal(
            ["MsRdpClient11", "MsRdpClient10", "MsRdpClient9"],
            RdpActiveXVersions.CleanHostOrder.Select(version => version.Label).ToArray());
        Assert.Equal("MsRdpClient11", RdpActiveXVersions.TryGetLabel(RdpActiveXVersions.CleanHostOrder[0].Clsid));
        Assert.Equal("MsRdpClient10", RdpActiveXVersions.TryGetLabel(RdpActiveXVersions.CleanHostOrder[1].Clsid));
        Assert.Equal("MsRdpClient9", RdpActiveXVersions.TryGetLabel(RdpActiveXVersions.CleanHostOrder[2].Clsid));
    }

    private static (IReadOnlyList<RdpActiveXCandidate<FakeActiveX>> Candidates, List<FakeActiveX> Instances)
        CreateCandidates()
    {
        var instances = new List<FakeActiveX>();
        var candidates = RdpActiveXVersions.CleanHostOrder
            .Select(version => new RdpActiveXCandidate<FakeActiveX>(
                version,
                () =>
                {
                    var instance = new FakeActiveX(version.Label);
                    instances.Add(instance);
                    return instance;
                }))
            .ToArray();

        return (candidates, instances);
    }

    private sealed class FakeActiveX
    {
        public FakeActiveX(string label) => Label = label;
        public string Label { get; }
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class TestComException : Exception
    {
        public TestComException() => HResult = unchecked((int)0x80040154);
    }
}