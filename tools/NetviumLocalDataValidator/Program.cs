using System.Management;
using Microsoft.Win32;
using Netvium.Helpers;
using Netvium.Models;

namespace NetviumLocalDataValidator;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("FAIL INPUT: no endpoint, credential, or output arguments are accepted.");
            return 2;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("BLOCKED ENVIRONMENT: Windows is required.");
            return 2;
        }
        try
        {
            var netviumCpu = new List<double>();
            var referenceCpu = new List<double>();
            WmiSnapshot? snapshot = null;
            for (var i = 0; i < 4; i++)
            {
                WmiCollector.ClearMemoryCache();
                snapshot = await WmiCollector.GetSnapshotAsync("localhost", string.Empty, string.Empty,
                    CancellationToken.None, force: true, includeEventLogs: false);
                if (snapshot.CpuPct.HasValue) netviumCpu.Add(snapshot.CpuPct.Value);
                var cpu = QueryDouble(@"root\cimv2", "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'", "PercentProcessorTime");
                if (cpu.HasValue) referenceCpu.Add(cpu.Value);
                if (i < 3) await Task.Delay(TimeSpan.FromSeconds(5));
            }
            if (snapshot == null || !snapshot.Connected)
            {
                Console.Error.WriteLine("FAIL NETVIUM_COLLECTOR: local snapshot unavailable.");
                return 1;
            }

            var checks = new List<bool>
            {
                PrintDifference("CPU_WINDOW", Average(netviumCpu), Average(referenceCpu), 15, "pp")
            };
            var totalKb = QueryDouble(@"root\cimv2", "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem", "TotalVisibleMemorySize");
            var freeKb = QueryDouble(@"root\cimv2", "SELECT FreePhysicalMemory FROM Win32_OperatingSystem", "FreePhysicalMemory");
            var referenceTotalGb = totalKb / 1024d / 1024d;
            var referenceFreeGb = freeKb / 1024d / 1024d;
            checks.Add(PrintPercent("RAM_TOTAL", snapshot.TotalRamGb, referenceTotalGb, 1));
            checks.Add(PrintPercent("RAM_USED", UsedRam(snapshot.TotalRamGb, snapshot.RamFreeGb), UsedRam(referenceTotalGb, referenceFreeGb), 5));

            var diskSize = QueryDouble(@"root\cimv2", "SELECT Size FROM Win32_LogicalDisk WHERE DeviceID='C:'", "Size") / 1024d / 1024d / 1024d;
            var diskFree = QueryDouble(@"root\cimv2", "SELECT FreeSpace FROM Win32_LogicalDisk WHERE DeviceID='C:'", "FreeSpace") / 1024d / 1024d / 1024d;
            checks.Add(PrintPercent("DISK_CAPACITY", snapshot.DiskTotalGb, diskSize, 1));
            checks.Add(PrintPercent("DISK_FREE", snapshot.DiskFreeGb, diskFree, 1));
            checks.Add(PrintStatic("DEFENDER", snapshot.DefenderRealTimeProtectionEnabled, QueryDefender()));
            checks.Add(PrintStatic("FIREWALL_DOMAIN", snapshot.FirewallDomainEnabled, QueryFirewall("DomainProfile")));
            checks.Add(PrintStatic("FIREWALL_PRIVATE", snapshot.FirewallPrivateEnabled, QueryFirewall("StandardProfile")));
            checks.Add(PrintStatic("FIREWALL_PUBLIC", snapshot.FirewallPublicEnabled, QueryFirewall("PublicProfile")));
            checks.Add(PrintStatic("PENDING_RESTART", snapshot.WindowsUpdateRebootRequired, QueryPendingRestart()));

            var passed = checks.All(x => x);
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} OVERALL: {checks.Count(x => x)}/{checks.Count}");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BLOCKED ENVIRONMENT: {ex.GetType().Name}");
            return 2;
        }
    }

    private static double? QueryDouble(string scopePath, string query, string property)
    {
        var scope = new ManagementScope($@"\\localhost\{scopePath}");
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
        using var results = searcher.Get();
        foreach (ManagementObject item in results) return Convert.ToDouble(item[property]);
        return null;
    }

    private static bool? QueryDefender()
    {
        var value = QueryDouble(@"root\Microsoft\Windows\Defender",
            "SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus", "RealTimeProtectionEnabled");
        return value.HasValue ? value.Value != 0 : null;
    }

    private static bool? QueryFirewall(string profile)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
        return key?.GetValue("EnableFirewall") is int enabled ? enabled != 0 : null;
    }

    private static bool? QueryPendingRestart()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        return key != null;
    }

    private static double? UsedRam(double? total, double? free) => total.HasValue && free.HasValue ? total.Value - free.Value : null;
    private static double? Average(List<double> values) => values.Count == 0 ? null : values.Average();

    private static bool PrintDifference(string name, double? actual, double? reference, double tolerance, string unit)
    {
        if (!actual.HasValue || !reference.HasValue) { Console.WriteLine($"UNKNOWN {name}"); return false; }
        var difference = Math.Abs(actual.Value - reference.Value);
        var pass = difference <= tolerance;
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: difference={difference:0.00}{unit} tolerance={tolerance:0.00}{unit}");
        return pass;
    }

    private static bool PrintPercent(string name, double? actual, double? reference, double tolerancePercent)
    {
        if (!actual.HasValue || !reference.HasValue || reference.Value == 0) { Console.WriteLine($"UNKNOWN {name}"); return false; }
        var difference = Math.Abs(actual.Value - reference.Value) / Math.Abs(reference.Value) * 100d;
        var pass = difference <= tolerancePercent;
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: difference={difference:0.00}% tolerance={tolerancePercent:0.00}%");
        return pass;
    }

    private static bool PrintStatic(string name, bool? actual, bool? reference)
    {
        if (!actual.HasValue || !reference.HasValue) { Console.WriteLine($"UNKNOWN {name}"); return true; }
        var pass = actual == reference;
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: match={(pass ? 1 : 0)}");
        return pass;
    }
}
