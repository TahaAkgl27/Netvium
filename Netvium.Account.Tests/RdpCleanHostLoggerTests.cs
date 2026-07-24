
using System.Diagnostics;
using Netvium.Rdp.Clean;
using Xunit;

namespace Netvium.Account.Tests;

[Collection("RdpLogger")]
public sealed class RdpCleanHostLoggerTests
{
    [Fact]
    public async Task ConcurrentLogsAreAcceptedAndSessionIdsSeparateTabs()
    {
        using var sandbox = new LoggerSandbox();
        const int total = 400;
        Parallel.For(0, total, i =>
            RdpCleanHostLogger.Log($"event-{i}", $"session-{i % 2}"));

        Assert.True(await RdpCleanHostLogger.FlushAsync(TimeSpan.FromSeconds(3)));
        var text = File.ReadAllText(sandbox.Path);
        Assert.Contains("[session=session-0]", text);
        Assert.Contains("[session=session-1]", text);
        Assert.True(text.Split("event-", StringSplitOptions.None).Length - 1 <= total);
    }

    [Fact]
    public async Task SingleProducerOrderIsPreserved()
    {
        using var sandbox = new LoggerSandbox();
        for (var i = 0; i < 100; i++)
            RdpCleanHostLogger.Log($"ordered-{i}", "ordered-session");

        Assert.True(await RdpCleanHostLogger.FlushAsync(TimeSpan.FromSeconds(3)));
        var lines = File.ReadAllLines(sandbox.Path);
        var ordered = lines
            .Where(line => line.Contains("] ordered-", StringComparison.Ordinal))
            .Select(line => int.Parse(System.Text.RegularExpressions.Regex.Match(line, @"ordered-(\d+)").Groups[1].Value))
            .ToArray();

        Assert.Equal(Enumerable.Range(0, 100), ordered);
    }

    [Fact]
    public async Task SensitiveValuesAreRedactedBeforeDiskWrite()
    {
        using var sandbox = new LoggerSandbox();
        RdpCleanHostLogger.Log(
            "password=secret-pass token=secret-token api_key=secret-key server=10.0.0.1 " +
            "connectionString=Server=db;User Id=alice;Password=sql-pass;",
            "secure-session");

        Assert.True(await RdpCleanHostLogger.FlushAsync(TimeSpan.FromSeconds(3)));
        var text = File.ReadAllText(sandbox.Path);

        Assert.DoesNotContain("secret-pass", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sql-pass", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.1", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotationKeepsBoundedFiles()
    {
        using var sandbox = new LoggerSandbox(maxFileBytes: 180, maxRetainedFiles: 2);
        for (var i = 0; i < 40; i++)
            RdpCleanHostLogger.Log(new string('x', 40) + $"-{i}", "rotation-session");

        Assert.True(await RdpCleanHostLogger.FlushAsync(TimeSpan.FromSeconds(3)));
        Assert.True(File.Exists(sandbox.Path + ".1"));
        Assert.True(File.Exists(sandbox.Path));
    }

    [Fact]
    public async Task DiskWriteFailureDoesNotEscapeLoggerOrBlockFlush()
    {
        var directory = Directory.CreateTempSubdirectory("netvium-rdp-log-error-");
        try
        {
            RdpCleanHostLogger.ConfigureForTests(directory.FullName);
            RdpCleanHostLogger.Log("write-failure", "failure-session");

            var stopwatch = Stopwatch.StartNew();
            var flushed = await RdpCleanHostLogger.FlushAsync(TimeSpan.FromSeconds(2));
            stopwatch.Stop();

            Assert.True(flushed);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            RdpCleanHostLogger.Dispose(TimeSpan.FromSeconds(1));
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task FlushTimeoutIsBoundedWhenBudgetIsExhausted()
    {
        using var sandbox = new LoggerSandbox();
        for (var i = 0; i < 1000; i++)
            RdpCleanHostLogger.Log($"timeout-{i}", "timeout-session");

        var stopwatch = Stopwatch.StartNew();
        _ = await RdpCleanHostLogger.FlushAsync(TimeSpan.FromTicks(1));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DisposeIsBoundedAndIdempotentWithPendingEntries()
    {
        using var sandbox = new LoggerSandbox();
        for (var i = 0; i < 500; i++)
            RdpCleanHostLogger.Log($"pending-{i}", "shutdown-session");

        var stopwatch = Stopwatch.StartNew();
        RdpCleanHostLogger.Dispose(TimeSpan.FromSeconds(2));
        RdpCleanHostLogger.Dispose(TimeSpan.FromSeconds(2));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void EnqueueDoesNotPerformSynchronousDiskWork()
    {
        using var sandbox = new LoggerSandbox();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 2000; i++)
            RdpCleanHostLogger.Log($"ui-{i}", "ui-session");
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    private sealed class LoggerSandbox : IDisposable
    {
        private readonly string _directory;

        public LoggerSandbox(int maxFileBytes = 5 * 1024 * 1024, int maxRetainedFiles = 3)
        {
            _directory = Directory.CreateTempSubdirectory("netvium-rdp-log-").FullName;
            Path = System.IO.Path.Combine(_directory, "rdp.log");
            RdpCleanHostLogger.ConfigureForTests(Path, maxFileBytes, maxRetainedFiles);
        }

        public string Path { get; }

        public void Dispose()
        {
            RdpCleanHostLogger.Dispose(TimeSpan.FromSeconds(2));
            try { Directory.Delete(_directory, true); } catch { }
        }
    }
}