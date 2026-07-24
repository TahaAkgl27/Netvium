using Xunit;
using Netvium.Agent;

namespace Netvium.Agent.Tests;

public sealed class AgentRuntimeTests
{
    [Fact]
    public void ConsoleConfigurationIsValidWithoutServiceSecretFile()
    {
        var options = new AgentRuntimeOptions(18081, "127.0.0.1", "unit-test-key", false, null);
        Assert.True(options.IsValid);
        Assert.False(options.ServiceMode);
        Assert.Equal("127.0.0.1", options.BindAddress);
    }

    [Fact]
    public void PlaintextKeyArgumentIsRejectedWithoutRetainingSecret()
    {
        const string sentinel = "FAZ6D_SECRET With Spaces!_DoNotLeak";
        var options = AgentRuntimeOptions.Parse(new[] { "--console", "--key", sentinel }, false);
        Assert.False(options.IsValid);
        Assert.DoesNotContain(sentinel, options.ConfigurationError ?? string.Empty);
        Assert.DoesNotContain(sentinel, System.Text.Json.JsonSerializer.Serialize(options));
    }
    [Fact]
    public void ServiceConfigurationFailsClosedWhenProtectedKeyIsMissing()
    {
        var options = AgentRuntimeOptions.Parse(new[] { "--service", "--key-file", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dpapi") }, true);
        Assert.False(options.IsValid);
        Assert.False(options.SharedKey.Length > 0);
    }

    [Fact]
    public void HealthContainsOnlySafeOperationalFields()
    {
        var options = new AgentRuntimeOptions(9841, "127.0.0.1", "secret-value", false, null);
        Assert.True(options.IsValid);
        using var state = new AgentRuntimeState(options);
        var json = System.Text.Json.JsonSerializer.Serialize(state.GetHealth());
        Assert.Contains("processRunning", json);
        Assert.Contains("metricsCollectorReady", json);
        Assert.DoesNotContain("secret-value", json);
        Assert.DoesNotContain("keyFilePath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MetricsCollectionIsObservedAndShutdownStopsNewWork()
    {
        var options = new AgentRuntimeOptions(9841, "127.0.0.1", "secret-value", false, null);
        Assert.True(options.IsValid);
        using var state = new AgentRuntimeState(options);
        var result = await state.CollectMetricsAsync(() => new { connected = true }, CancellationToken.None);
        Assert.True(result.Accepted);
        Assert.NotNull(state.LastSuccessfulMetricsUtc);
        state.BeginShutdown();
        var stopped = await state.CollectMetricsAsync(() => new { connected = true }, CancellationToken.None);
        Assert.False(stopped.Accepted);
        var health = System.Text.Json.JsonSerializer.Serialize(state.GetHealth());
        Assert.Contains("degraded", health);
    }

    [Fact]
    public async Task ConcurrentMetricsAreBounded()
    {
        var options = new AgentRuntimeOptions(9841, "127.0.0.1", "secret-value", false, null);
        Assert.True(options.IsValid);
        using var state = new AgentRuntimeState(options);
        using var entered = new ManualResetEventSlim();
        var first = Task.Run(() => state.CollectMetricsAsync(() => { entered.Set(); Thread.Sleep(1500); return new { ok = true }; }, CancellationToken.None));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
        var second = await state.CollectMetricsAsync(() => new { ok = true }, CancellationToken.None);
        Assert.False(second.Accepted);
        await first;
    }

    [Fact]
    public async Task CancellationIsBoundedAndObserved()
    {
        var options = new AgentRuntimeOptions(9841, "127.0.0.1", "secret-value", false, null);
        using var state = new AgentRuntimeState(options);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.CollectMetricsAsync(() => new { ok = true }, cts.Token));
    }

    [Fact]
    public void HostSupportsConsoleAndWindowsServiceLifetimes()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Netvium.Agent", "Program.cs"));
        Assert.Contains("UseWindowsService", source);
        Assert.Contains("UseConsoleLifetime", source);
        Assert.Contains("Netvium Agent", source);
    }

    [Fact]
    public void InstallAndUninstallScriptsAreGuarded()
    {
        var root = FindRepositoryRoot();
        var install = File.ReadAllText(Path.Combine(root, "scripts", "Install-NetviumAgentService.ps1"));
        var uninstall = File.ReadAllText(Path.Combine(root, "scripts", "Uninstall-NetviumAgentService.ps1"));
        Assert.Contains("Netvium Agent", install);
        Assert.Contains("delayed-auto", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart/5000/restart/15000/restart/60000", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--key-file", install);
        Assert.DoesNotContain("--key ", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RemoveData", uninstall, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Netvium Agent", uninstall);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Netvium.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Netvium solution root not found");
    }
}