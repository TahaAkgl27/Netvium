using Netvium.Helpers;
using Netvium.Models;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class InsightEngineTests
{
    [Fact] public void HealthySystem_HasNoWarningOrCriticalOperationalFinding()
    {
        var ctx = Build(Healthy());
        Assert.DoesNotContain(ctx.RuleFindings, f => f.Category != "Data" && f.Severity is InsightSeverity.Warning or InsightSeverity.Critical);
        Assert.True(ctx.DataQuality.ConfidencePercent >= 80);
    }

    [Fact] public void SustainedHighCpu_IsWarning()
    {
        var now = DateTime.Now; var snap = Healthy(now, cpu: 88);
        var ctx = Build(snap, History(now, cpu: 86, ram: 40, disk: 40));
        AssertFinding(ctx, "cpu_sustained", InsightSeverity.Warning, "örne");
    }

    [Fact] public void HighRam_IsWarningWhenSustained()
    {
        var now = DateTime.Now; var snap = Healthy(now, ram: 89);
        var ctx = Build(snap, History(now, cpu: 20, ram: 87, disk: 40));
        AssertFinding(ctx, "ram_sustained", InsightSeverity.Warning, "warning");
    }

    [Fact] public void CriticalRam_IsCriticalWhenSustained()
    {
        var now = DateTime.Now; var snap = Healthy(now, ram: 99);
        var ctx = Build(snap, History(now, cpu: 20, ram: 97, disk: 40));
        AssertFinding(ctx, "ram_sustained", InsightSeverity.Critical, "critical");
    }

    [Fact] public void CriticalDisk_IsCritical()
    {
        var now = DateTime.Now; var snap = Healthy(now, disk: 96);
        var ctx = Build(snap, History(now, cpu: 20, ram: 40, disk: 94));
        AssertFinding(ctx, "disk_sustained", InsightSeverity.Critical, "critical");
    }

    [Fact] public void DefenderOffWithoutHealthyAv_IsReported()
    {
        var snap = Healthy(); snap.DefenderRealTimeProtectionEnabled = false;
        var ctx = Build(snap);
        AssertFinding(ctx, "av_missing", InsightSeverity.Warning, "Defender");
    }

    [Fact] public void DefenderOffWithUnhealthyThirdPartyAv_IsCritical()
    {
        var snap = Healthy(); snap.DefenderRealTimeProtectionEnabled = false;
        snap.SystemStatusComponents.Add(new("Contoso Endpoint Security", "Security", "Pasif", ""));
        AssertFinding(Build(snap), "av_missing", InsightSeverity.Critical, "Contoso");
    }

    [Fact] public void DefenderOffWithHealthyThirdPartyAv_IsInfo()
    {
        var snap = Healthy(); snap.DefenderRealTimeProtectionEnabled = false;
        snap.SystemStatusComponents.Add(new("Contoso Endpoint Security", "Security", "Aktif", ""));
        var ctx = Build(snap);
        AssertFinding(ctx, "defender_off_other_av", InsightSeverity.Info, "Contoso");
    }

    [Fact] public void SingleFirewallProfileOff_IsWarning()
    {
        var snap = Healthy(); snap.FirewallPublicEnabled = false;
        var ctx = Build(snap);
        AssertFinding(ctx, "firewall_profile_off", InsightSeverity.Warning, "Public");
    }

    [Fact] public void PendingRestart_IsWarningWithSignal()
    {
        var snap = Healthy(); snap.WindowsUpdateRebootRequired = true;
        var ctx = Build(snap);
        AssertFinding(ctx, "pending_restart", InsightSeverity.Warning, "WindowsUpdateRebootRequired");
    }

    [Fact] public void PendingRestart_UsesExistingSystemStatusProbeFallback()
    {
        var snap = Healthy(); snap.WindowsUpdateRebootRequired = null;
        snap.SystemStatusComponents.Add(new("Pending Restart", "Maintenance", "Yeniden başlatma gerekli", ""));
        var ctx = Build(snap);
        Assert.True(ctx.SystemHealth.PendingRestart);
        AssertFinding(ctx, "pending_restart", InsightSeverity.Warning, "SystemStatusProbe");
    }

    [Fact] public void DnsProblem_IsWarning()
    {
        var snap = Healthy(); ReplaceComponent(snap, "DNS", "Pasif");
        AssertFinding(Build(snap), "dns_unhealthy", InsightSeverity.Warning, "Pasif");
    }

    [Fact] public void TimeSyncProblem_IsWarning()
    {
        var snap = Healthy(); ReplaceComponent(snap, "Time Sync", "Uyarı");
        AssertFinding(Build(snap), "time_sync_unhealthy", InsightSeverity.Warning, "Uyarı");
    }

    [Fact] public void StoppedCriticalService_IsCritical()
    {
        var snap = Healthy(); snap.Services.Add(new("MSSQLSERVER", "SQL Server", "Stopped", true));
        AssertFinding(Build(snap), "svc_SQL Server", InsightSeverity.Critical, "Stopped");
    }

    [Fact] public void ManyCriticalEvents_AreCriticalFromStructuredSeverity()
    {
        var snap = Healthy();
        snap.Timeline = Enumerable.Range(0, 4).Select(i => new TimelineEvent(DateTime.Now, "System", "Critical", $"Synthetic {i}", "")).ToList();
        var ctx = Build(snap);
        Assert.Equal(4, ctx.EventSummary.CriticalCount);
        AssertFinding(ctx, "events_critical_many", InsightSeverity.Critical, "Critical=4");
    }

    [Fact] public void AgentSnapshot_IsIdentifiedWithoutStartingWmiComparison()
    {
        var snap = Healthy(); snap.DataSource = SnapshotDataSources.Agent;
        var ctx = Build(snap);
        Assert.Equal(SnapshotDataSources.Agent, ctx.DataQuality.Source);
        Assert.False(ctx.DataQuality.SourcesCompared);
        AssertFinding(ctx, "data_source", InsightSeverity.Info, "yapılmadı");
    }

    [Fact] public void AllMissingData_IsNotTreatedAsZeroOrHealthy()
    {
        var snap = new WmiSnapshot { Connected = false, CollectedAt = DateTime.Now, DataSource = SnapshotDataSources.Wmi };
        var ctx = Build(snap);
        AssertFinding(ctx, "wmi_missing", InsightSeverity.Warning, "doğrulanamadı");
        Assert.Contains("CPU", ctx.DataQuality.UnavailableComponents);
        Assert.True(ctx.DataQuality.ConfidencePercent < 80);
    }

    [Fact] public void AgentWmiConflict_LowersConfidenceAndNamesFields()
    {
        var primary = Healthy(); primary.DataSource = SnapshotDataSources.Agent; primary.CpuPct = 90; primary.RamUsedPct = 80;
        var other = Healthy(); other.DataSource = SnapshotDataSources.Wmi; other.CpuPct = 20; other.RamUsedPct = 40;
        var ctx = Build(primary, comparison: other);
        Assert.True(ctx.DataQuality.HasSourceConflict);
        Assert.Contains("CPU", ctx.DataQuality.Conflicts);
        AssertFinding(ctx, "source_conflict", InsightSeverity.Warning, "CPU");
        Assert.True(ctx.DataQuality.ConfidencePercent < 80);
    }

    [Fact] public void OldCacheSnapshot_IsExplicitAndStale()
    {
        var snap = Healthy(DateTime.Now.AddMinutes(-10)); snap.DataSource = SnapshotDataSources.Cache;
        var ctx = Build(snap);
        Assert.True(ctx.DataQuality.IsCache); Assert.True(ctx.DataQuality.IsStale);
        AssertFinding(ctx, "data_stale", InsightSeverity.Warning, "Cache");
    }

    [Fact] public void ShortCpuSpike_IsNotSustainedButStillVisible()
    {
        var now = DateTime.Now; var snap = Healthy(now, cpu: 99);
        var history = History(now, cpu: 25, ram: 40, disk: 40);
        history.Add(snap); // Production history may already contain the current snapshot.
        var ctx = Build(snap, history);
        AssertFinding(ctx, "cpu_spike", InsightSeverity.Critical, "critical");
        Assert.DoesNotContain(ctx.RuleFindings, f => f.Code == "cpu_sustained");
    }

    [Fact] public void MultipleSimultaneousFaults_AreAllPreserved()
    {
        var snap = Healthy(); snap.DefenderRealTimeProtectionEnabled = false; snap.WindowsUpdateRebootRequired = true; snap.FirewallDomainEnabled = false;
        var ctx = Build(snap);
        Assert.Contains(ctx.RuleFindings, f => f.Code == "av_missing");
        Assert.Contains(ctx.RuleFindings, f => f.Code == "pending_restart");
        Assert.Contains(ctx.RuleFindings, f => f.Code == "firewall_profile_off");
    }

    [Fact] public void HealthyBoundaryValues_RemainNormal()
    {
        var now = DateTime.Now; var snap = Healthy(now, cpu: 79, ram: 84.9, disk: 79.9);
        var ctx = Build(snap, History(now, cpu: 79, ram: 84.9, disk: 79.9));
        Assert.Equal(MetricTrendState.Normal, ctx.CpuTrendAssessment.State);
        Assert.Equal(MetricTrendState.Normal, ctx.RamTrendAssessment.State);
        Assert.Equal(MetricTrendState.Normal, ctx.DiskTrendAssessment.State);
    }

    [Fact] public void NullAccessDeniedTimeout_StayUnknown()
    {
        var snap = new WmiSnapshot { Connected = false, Error = "Access denied / timeout", CollectedAt = DateTime.Now };
        var ctx = Build(snap);
        Assert.Equal(MetricTrendState.Unknown, ctx.CpuTrendAssessment.State);
        AssertFinding(ctx, "firewall_unknown", InsightSeverity.Info, "okunamadı");
        AssertFinding(ctx, "av_unknown", InsightSeverity.Info, "okunamadı");
    }

    [Fact] public void InsufficientHistory_IsExplicit()
    {
        var snap = Healthy(DateTime.Now, cpu: 88);
        var ctx = Build(snap);
        Assert.Equal(MetricTrendState.InsufficientHistory, ctx.CpuTrendAssessment.State);
        AssertFinding(ctx, "cpu_history_insufficient", InsightSeverity.Warning, "en az 3");
    }

    private static ServerAnalysisContext Build(WmiSnapshot snap, List<WmiSnapshot>? history = null, WmiSnapshot? comparison = null)
    {
        var server = new ServerInfo("Synthetic", "localhost", string.Empty, string.Empty, string.Empty);
        return ServerAnalysisPipeline.Build(snap, server, "synthetic", snap.PingMs, true, history, 100, comparisonSnapshot: comparison);
    }

    private static WmiSnapshot Healthy(DateTime? at = null, int cpu = 20, double ram = 40, double disk = 40) => new()
    {
        Connected = true, CollectedAt = at ?? DateTime.Now, DataSource = SnapshotDataSources.Wmi,
        CpuPct = cpu, RamUsedPct = ram, DiskUsedPct = disk, PingMs = 5,
        DefenderRealTimeProtectionEnabled = true,
        FirewallDomainEnabled = true, FirewallPrivateEnabled = true, FirewallPublicEnabled = true,
        WindowsUpdateRebootRequired = false,
        SystemStatusComponents = new()
        {
            new("DNS", "Network", "Aktif", ""),
            new("Time Sync", "Service", "Senkron", "")
        }
    };

    private static List<WmiSnapshot> History(DateTime now, int cpu, double ram, double disk) => new()
    {
        Healthy(now.AddMinutes(-10), cpu, ram, disk),
        Healthy(now.AddMinutes(-5), cpu, ram, disk)
    };

    private static void ReplaceComponent(WmiSnapshot snap, string name, string status)
    {
        snap.SystemStatusComponents.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        snap.SystemStatusComponents.Add(new(name, "Probe", status, ""));
    }

    private static void AssertFinding(ServerAnalysisContext ctx, string code, InsightSeverity severity, string evidence)
    {
        var finding = Assert.Single(ctx.RuleFindings, f => f.Code == code);
        Assert.Equal(severity, finding.Severity);
        Assert.Contains(evidence, finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(finding.Title));
        Assert.InRange(ctx.DataQuality.ConfidencePercent, 0, 100);
    }
}
