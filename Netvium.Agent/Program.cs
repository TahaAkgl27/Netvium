using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Netvium.Agent;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task Main(string[] args)
    {
        var serviceMode = WindowsServiceHelpers.IsWindowsService()
            || args.Any(a => a.Equals("--service", StringComparison.OrdinalIgnoreCase));
        var options = await AgentRuntimeOptions.ParseAsync(args, serviceMode, CancellationToken.None).ConfigureAwait(false);

        if (!options.IsValid)
        {
            Console.Error.WriteLine("Netvium.Agent yapılandırması geçersiz veya servis anahtarı çözülemedi.");
            Environment.ExitCode = 1;
            return;
        }

        if (!TryResolveBindAddress(options.BindAddress, out var listenAddress, out var bindDisplay))
        {
            Console.Error.WriteLine("Netvium.Agent bind adresi geçersiz.");
            Environment.ExitCode = 1;
            return;
        }

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.ConfigureKestrel(o => o.Listen(listenAddress, options.Port));
        if (options.ServiceMode)
        {
            builder.Host.UseWindowsService(serviceOptions =>
                serviceOptions.ServiceName = "Netvium Agent");
        }
        else
        {
            builder.Host.UseConsoleLifetime();
        }

        var app = builder.Build();
        var state = new AgentRuntimeState(options);
        app.Lifetime.ApplicationStopping.Register(state.BeginShutdown);
        MapEndpoints(app, state, options);

        Console.WriteLine($"Netvium.Agent listening on {bindDisplay}:{options.Port}");
        app.Run();
        state.Dispose();
    }

    private static void MapEndpoints(WebApplication app, AgentRuntimeState state, AgentRuntimeOptions options)
    {
        app.MapGet("/health", (HttpRequest request) =>
        {
            if (!IsAuthorized(request, options.SharedKey))
                return Results.Unauthorized();

            return Results.Json(state.GetHealth(), JsonOpts);
        });

        app.MapGet("/metrics", async (HttpContext context) =>
        {
            if (!IsAuthorized(context.Request, options.SharedKey))
                return Results.Unauthorized();
            if (state.IsStopping)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            try
            {
                var result = await state.CollectMetricsAsync(CollectSnapshot, context.RequestAborted);
                if (!result.Accepted || result.Snapshot == null)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                return Results.Json(result.Snapshot, JsonOpts);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Agent] Metrics collector failed: {ex.GetType().Name}");
                return Results.Json(new { connected = false, error = "Agent metrics unavailable" }, JsonOpts,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }

    private static bool IsAuthorized(HttpRequest request, string sharedKey)
    {
        if (!request.Headers.TryGetValue("X-Netvium-Key", out var header))
            return false;
        var supplied = Encoding.UTF8.GetBytes(header.ToString());
        var expected = Encoding.UTF8.GetBytes(sharedKey);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
    private static bool TryResolveBindAddress(string bindAddress, out IPAddress listenAddress, out string bindDisplay)
    {
        bindDisplay = bindAddress.Trim();
        listenAddress = IPAddress.Loopback;

        if (bindDisplay.Equals("*", StringComparison.Ordinal) ||
            bindDisplay.Equals("0.0.0.0", StringComparison.Ordinal))
        {
            listenAddress = IPAddress.Any;
            bindDisplay = "0.0.0.0";
            return true;
        }

        if (bindDisplay.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            listenAddress = IPAddress.Loopback;
            bindDisplay = "127.0.0.1";
            return true;
        }

        return IPAddress.TryParse(bindDisplay, out listenAddress!);
    }

    private static object CollectSnapshot()
    {
        // Disk C:
        double? diskUsedPct = null;
        double diskFreeGb = 0;
        try
        {
            var d = new DriveInfo("C");
            if (d.IsReady && d.TotalSize > 0)
            {
                diskFreeGb = d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                diskUsedPct = (1.0 - (d.AvailableFreeSpace / (double)d.TotalSize)) * 100.0;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Agent] Operation failed: {ex.GetType().Name}"); }

        // CPU (no PerfCounter; hardened environments)
        int? cpuPct = TryGetCpuPercent();

        // RAM (pure Win32)
        var mem = TryGetMemory();

        // Uptime
        var uptimeSeconds = TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds;

        // Services
        var services = BuildServiceStates();

        // Sessions
        var (sessions, activeUsers) = TryWtsSessionsLocal();

        return new
        {
            connected = true,
            error = (string?)null,
            agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            dataSource = "Agent",
            diskUsedPct,
            diskFreeGb,
            cpuPct,
            ramUsedPct = mem.usedPct,
            ramFreeGb = mem.freeGb,
            uptimeSeconds,
            activeUsers,
            services,
            sessions
        };
    }

    private static (double? usedPct, double freeGb) TryGetMemory()
    {
        try
        {
            var ms = new MEMORYSTATUSEX();
            ms.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            if (!GlobalMemoryStatusEx(ref ms)) return (null, 0);
            if (ms.ullTotalPhys == 0) return (null, 0);
            var usedPct = (1.0 - (ms.ullAvailPhys / (double)ms.ullTotalPhys)) * 100.0;
            var freeGb = ms.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            return (usedPct, freeGb);
        }
        catch
        {
            return (null, 0);
        }
    }

    private static int? TryGetCpuPercent()
    {
        try
        {
            if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return null;
            Thread.Sleep(200);
            if (!GetSystemTimes(out var idle2, out var kernel2, out var user2)) return null;

            var idle = FileTimeToUlong(idle2) - FileTimeToUlong(idle1);
            var kernel = FileTimeToUlong(kernel2) - FileTimeToUlong(kernel1);
            var user = FileTimeToUlong(user2) - FileTimeToUlong(user1);
            var total = kernel + user;
            if (total == 0) return null;
            var busy = total - idle;
            var pct = (int)Math.Round((busy * 100.0) / total);
            return Math.Clamp(pct, 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private static ulong FileTimeToUlong(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    private static object[] BuildServiceStates()
    {
        var targets = new[]
        {
            ("TermService", "Remote Desktop Services"),
            ("Spooler", "Print Spooler"),
            ("MSSQLSERVER", "SQL Server"),
            ("W32Time", "Windows Time")
        };

        return targets.Select(s => new
        {
            name = s.Item1,
            display = s.Item2,
            state = QueryServiceState(s.Item1)
        }).ToArray();
    }

    private static string QueryServiceState(string serviceName)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return "-";
        try
        {
            var svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return "-";
            try
            {
                if (!QueryServiceStatus(svc, out var status)) return "-";
                return status.dwCurrentState switch
                {
                    SERVICE_RUNNING => "Running",
                    SERVICE_STOPPED => "Stopped",
                    SERVICE_START_PENDING => "StartPending",
                    SERVICE_STOP_PENDING => "StopPending",
                    SERVICE_PAUSED => "Paused",
                    SERVICE_PAUSE_PENDING => "PausePending",
                    SERVICE_CONTINUE_PENDING => "ContinuePending",
                    _ => status.dwCurrentState.ToString()
                };
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    private static (object[] sessions, int? activeUsers) TryWtsSessionsLocal()
    {
        var list = new List<object>();
        try
        {
            var serverHandle = WTSOpenServer(Environment.MachineName);
            try
            {
                if (WTSEnumerateSessions(serverHandle, 0, 1, out var pp, out var count) && pp != IntPtr.Zero)
                {
                    var size = Marshal.SizeOf<WTS_SESSION_INFO>();
                    for (var i = 0; i < count; i++)
                    {
                        var p = IntPtr.Add(pp, i * size);
                        var si = Marshal.PtrToStructure<WTS_SESSION_INFO>(p);
                        var user = QueryWtsString(serverHandle, si.SessionID, WTS_INFO_CLASS.WTSUserName);
                        var dom = QueryWtsString(serverHandle, si.SessionID, WTS_INFO_CLASS.WTSDomainName);
                        var name = string.IsNullOrWhiteSpace(dom) ? user : $"{dom}\\{user}";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        list.Add(new { user = name, state = si.State.ToString(), idle = "-", logon = "-" });
                    }
                    WTSFreeMemory(pp);
                }
            }
            finally
            {
                WTSCloseServer(serverHandle);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Agent] Operation failed: {ex.GetType().Name}"); }

        var au = list.Select(x => ((dynamic)x).user as string)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return (list.ToArray(), au);
    }

    private enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7
    }

    private enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionID;
        public IntPtr pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr WTSOpenServer(string pServerName);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSCloseServer(IntPtr hServer);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr hServer, int Reserved, int Version, out IntPtr ppSessionInfo, out int pCount);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

    private static string QueryWtsString(IntPtr hServer, int sessionId, WTS_INFO_CLASS cls)
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (WTSQuerySessionInformation(hServer, sessionId, cls, out buffer, out var bytes) && buffer != IntPtr.Zero && bytes > 2)
                return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Agent] Operation failed: {ex.GetType().Name}"); }
        finally
        {
            if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
        }
        return string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;

    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_START_PENDING = 0x00000002;
    private const uint SERVICE_STOP_PENDING = 0x00000003;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_CONTINUE_PENDING = 0x00000005;
    private const uint SERVICE_PAUSE_PENDING = 0x00000006;
    private const uint SERVICE_PAUSED = 0x00000007;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint dwAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);
}

