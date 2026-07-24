using Xunit;
using System.Management;
using System.Runtime.InteropServices;
using Netvium.Helpers;

namespace Netvium.Account.Tests;

public sealed class WmiResilienceTests
{
    [Fact]
    public void WmiSearchersUseTheCentralTimedFactory()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Helpers", "WmiCollector.cs"));
        var direct = System.Text.RegularExpressions.Regex.Matches(source, @"new\s+ManagementObjectSearcher\s*\(").Count;
        Assert.Equal(1, direct);
        Assert.InRange(WmiCollector.MaxConcurrentWmiOperations, 1, 50);
    }

    [Theory]
    [InlineData(0x80070005, "Access denied")]
    [InlineData(0x800706BA, "RPC server unavailable")]
    [InlineData(0x8004100E, "Invalid WMI namespace")]
    [InlineData(0x80041010, "Invalid WMI class")]
    public void WmiFailuresAreClassified(uint hresult, string expected)
    {
        var error = new COMException("provider detail", unchecked((int)hresult));
        Assert.Contains(expected, WmiCollector.ClassifyWmiFailure(error));
    }

    [Fact]
    public void AgentAuthenticationFailureIsNotTreatedAsUnavailable()
    {
        var authFailure = new Netvium.Models.WmiSnapshot { Connected = false, DataSource = Netvium.Models.SnapshotDataSources.Agent, Error = "Agent authentication failed." };
        var unavailable = new Netvium.Models.WmiSnapshot { Connected = false, DataSource = Netvium.Models.SnapshotDataSources.Agent };
        Assert.True(MainWindow.IsAgentAuthenticationFailureForTests(authFailure));
        Assert.False(MainWindow.IsAgentAuthenticationFailureForTests(unavailable));
    }

    [Fact]
    public void CancellationIsNotClassifiedAsProviderFailure()
    {
        var text = WmiCollector.ClassifyWmiFailure(new OperationCanceledException());
        Assert.Equal("WMI operation cancelled.", text);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Netvium.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}