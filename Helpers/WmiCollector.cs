using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Models;
using Netvium.Native;

namespace Netvium.Helpers
{

    public static class WmiCollector
    {
        private static readonly ConcurrentDictionary<string, (WmiSnapshot Data, DateTime Expiry)> _cache = new();
        private static TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
        private static int _connectionTimeoutMs = 5000;
        private static int _operationTimeoutSec = 15;
        private static readonly SemaphoreSlim WmiConcurrencyGate = new(6, 6);
        internal static int MaxConcurrentWmiOperations => 6;
        internal static int OperationTimeoutSecondsForTests => _operationTimeoutSec;
        private static bool _monitorCpu = true;
        private static bool _monitorRam = true;
        private static bool _monitorDisk = true;
        private static bool _monitorNetwork = true;
        private static bool _monitorServices = true;
        private static bool _monitorEventLogs = true;

        public static void Configure(AppSettings settings)
        {
            CacheDuration = TimeSpan.FromSeconds(Math.Clamp(settings.MonitoringTimeoutSeconds / 3, 5, 60));
            _connectionTimeoutMs = Math.Clamp(settings.MonitoringTimeoutSeconds * 1000, 3000, 120000);
            _operationTimeoutSec = Math.Clamp(settings.MonitoringTimeoutSeconds, 5, 120);
            _monitorCpu = settings.MonitorCpu;
            _monitorRam = settings.MonitorRam;
            _monitorDisk = settings.MonitorDisk;
            _monitorNetwork = settings.MonitorNetwork;
            _monitorServices = settings.MonitorServices;
            _monitorEventLogs = settings.MonitorEventLogs;
        }

        public static void ClearMemoryCache() => _cache.Clear();

        public static async Task<WmiSnapshot> GetSnapshotAsync(string ip, string username, string password, CancellationToken ct, Action<WmiSnapshot>? onProgress = null, bool force = false, bool includeEventLogs = false)
        {
            var cacheKey = $"{ip}|{username}";
            WmiSnapshot? diskData = null;
            
            // 1. Check Memory Cache
            if (!force && _cache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow < cached.Expiry)
                {
                    return cached.Data;
                }
                _cache.TryRemove(cacheKey, out _);
            }

            // 2. Reuse the disk cache only while it is genuinely fresh. Older snapshots
            // are retained as an offline fallback, but must never prevent a live WMI poll.
            if (!force)
            {
                diskData = await CacheManager.LoadAsync<WmiSnapshot>(cacheKey);
                if (IsReusableDiskCache(diskData, DateTime.UtcNow, CacheDuration))
                {
                    diskData!.DataSource = SnapshotDataSources.Cache;
                    _cache[cacheKey] = (diskData, DateTime.UtcNow.Add(CacheDuration));
                    return diskData;
                }
            }

            // 3. Collect from remote
            var result = await CollectInternalAsync(ip, username, password, ct, onProgress, includeEventLogs);

            // 4. Update Memory and Disk Cache only with useful telemetry.
            if (result.Connected && SnapshotMergeHelper.HasDisplayableData(result))
            {
                _cache[cacheKey] = (result, DateTime.UtcNow.Add(CacheDuration));
                _ = CacheManager.SaveAsync(cacheKey, result); // Background save
            }

            // Use stale data only after attempting live collection. This keeps the
            // dashboard useful while ensuring the next refresh still polls the server.
            if (!force
                && !SnapshotMergeHelper.HasDisplayableData(result)
                && SnapshotMergeHelper.HasDisplayableData(diskData))
            {
                diskData!.DataSource = SnapshotDataSources.Cache;
                diskData.Error = result.Error;
                return diskData;
            }
            
            return result;
        }


        internal static bool IsReusableDiskCache(WmiSnapshot? snapshot, DateTime utcNow, TimeSpan maxAge)
        {
            if (!SnapshotMergeHelper.HasDisplayableData(snapshot) || snapshot!.CollectedAt == default)
                return false;

            var collectedUtc = snapshot.CollectedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(snapshot.CollectedAt, DateTimeKind.Local).ToUniversalTime()
                : snapshot.CollectedAt.ToUniversalTime();
            var age = utcNow - collectedUtc;
            return age >= TimeSpan.Zero && age <= maxAge;
        }

        /// <summary>

        /// Loads event log timeline into an existing snapshot without blocking initial dashboard render.
        /// </summary>
        public static async Task<bool> TryEnrichEventLogsAsync(string ip, string username, string password, WmiSnapshot snap, CancellationToken ct)
        {
            if (!_monitorEventLogs || snap == null || !snap.Connected || ct.IsCancellationRequested)
                return false;

            try
            {
                var scope = await ConnectScopeAsync(ip, username, password, ct).ConfigureAwait(false);
                if (scope == null || !scope.IsConnected)
                    return false;

                await WithTimeoutAsync(
                    Task.Run(() =>
                    {
                        CollectTimelineEvents(scope, snap);
                        return true;
                    }, ct),
                    TimeSpan.FromSeconds(_operationTimeoutSec),
                    false,
                    "EventLogEnrichment",
                    ip,
                    ct).ConfigureAwait(false);
                return snap.Timeline?.Count > 0;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Deferred event log enrichment failed for {ip}.", ex, category: "WMI", action: "EnrichEventLogs");
                return false;
            }
        }

        private static async Task<List<int>> CheckCommonPortsAsync(string ip)
        {
            var openPorts = new List<int>();
            int[] portsToCheck = { 80, 443, 445, 1433, 3389, 389 };

            var tasks = portsToCheck.Select(async port =>
            {
                try
                {
                    using (var client = new System.Net.Sockets.TcpClient())
                    {
                        var connectTask = client.ConnectAsync(ip, port);
                        if (await System.Threading.Tasks.Task.WhenAny(connectTask, System.Threading.Tasks.Task.Delay(100)) == connectTask)
                        {
                            await connectTask;
                            lock (openPorts) openPorts.Add(port);
                        }
                    }
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            });

            await System.Threading.Tasks.Task.WhenAll(tasks);
            return openPorts;
        }

        public static async Task<WmiSnapshot?> LoadFromDiskAsync(string ip, string username)
        {
            var cacheKey = $"{ip}|{username}";
            var snap = await CacheManager.LoadAsync<WmiSnapshot>(cacheKey);
            if (snap != null)
                snap.DataSource = SnapshotDataSources.Cache;
            return snap;
        }

        private static async Task<WmiSnapshot> CollectInternalAsync(string ip, string username, string password, CancellationToken ct, Action<WmiSnapshot>? onProgress, bool includeEventLogs)
        {
            var snap = new WmiSnapshot { Connected = false };
            snap.Ip = ip; // Set IP for the snapshot
            snap.Username = username; // Set Username for the snapshot
            snap.CollectedAt = DateTime.Now; // Set collection time

            try
            {
                var scope = await ConnectScopeAsync(ip, username, password, ct).ConfigureAwait(false);
                if (scope == null)
                {
                    snap.Error = "WMI: Connection Timeout or unreachable";
                    snap.Status = "unreachable";
                    return snap;
                }

                if (!scope.IsConnected)
                {
                    snap.Error = "WMI: Connection failed";
                    snap.Status = "unreachable";
                    return snap;
                }

                // SHARES (Enhanced: Get more info for better Role Detection)
                try
                {
                    using var searcher = CreateTimedSearcher(scope, "SELECT Name, Type, Path FROM Win32_Share");
                    using var collection = searcher.Get();
                    foreach (var m in collection)
                    {
                        try 
                        { 
                            var name = m["Name"]?.ToString() ?? "";
                            var type = m["Type"]?.ToString() ?? "0";
                            // Only add if it's a Disk Drive (0) and not an administrative share (ending in $)
                            // OR if it's a dedicated non-default share
                            if (type == "0" && !name.EndsWith("$"))
                            {
                                snap.SharedFolders.Add(name); 
                            }
                        } 
                        catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    }
                }
                catch (Exception ex)
                {
                    LogWmiProviderFailure("Shares", scope.Path.Path, ex);
                }

                snap.Connected = true;
                snap.DataSource = SnapshotDataSources.Wmi;
                snap.Status = "ok";
                
                // Populate ListenPorts
                snap.ListenPorts = await CheckCommonPortsAsync(ip);

                if (includeEventLogs && _monitorEventLogs)
                {
                    snap.Timeline = new List<TimelineEvent>
                    {
                        new TimelineEvent(DateTime.Now, "System", "Info", "Event Log madenciliği başlatıldı...", "🔍")
                    };
                    onProgress?.Invoke(snap);
                }
                else
                {
                    snap.Timeline = new List<TimelineEvent>();
                }

                // 2. Parallel Collection (Metrics + Sessions)
                var metricsTask = CollectWmiMetricsGroupedAsync(scope, snap, ct, onProgress, includeEventLogs);
                var sessionsTask = WithTimeoutAsync(
                    ActiveSessionCollector.GetSessionsAsync(ip, username, password ?? ""),
                    TimeSpan.FromSeconds(_operationTimeoutSec),
                    new List<SessionRow>(),
                    "Sessions",
                    ip,
                    ct);

                await Task.WhenAll(metricsTask, sessionsTask).ConfigureAwait(false);

                var sessions = sessionsTask.Result?.ToList() ?? new List<SessionRow>();
                snap.Sessions.Clear();
                foreach (var s in sessions) snap.Sessions.Add(s);
                onProgress?.Invoke(snap);

                // IF API fallback returns empty, try WMI approach
                if (!snap.Sessions.Any(s => !s.User.Equals("Error", StringComparison.OrdinalIgnoreCase)))
                {
                    var wmiSessions = GetSessionsViaWmi(scope);
                    if (wmiSessions.Any())
                    {
                        snap.Sessions.Clear();
                        foreach (var s in wmiSessions) snap.Sessions.Add(s);
                        onProgress?.Invoke(snap);
                    }
                }
                snap.ActiveUsers = sessions.Count(s => s.State.Equals("Active", StringComparison.OrdinalIgnoreCase) || 
                                                      s.State.Equals("Aktif", StringComparison.OrdinalIgnoreCase) ||
                                                      s.State.Contains("WTSActive", StringComparison.OrdinalIgnoreCase) ||
                                                      s.State.Contains("RDP", StringComparison.OrdinalIgnoreCase) ||
                                                      s.State.Contains("Console", StringComparison.OrdinalIgnoreCase));

                // Deriving Last User from sessions if WMI UserName was empty
                if (string.IsNullOrEmpty(snap.LastUser) && sessions.Any())
                {
                    var activeSession = sessions.FirstOrDefault(s => s.State.Contains("Active", StringComparison.OrdinalIgnoreCase) || s.State.Contains("Aktif", StringComparison.OrdinalIgnoreCase));
                    if (activeSession != null)
                    {
                        snap.LastUser = activeSession.User;
                        snap.LastLogon = activeSession.Logon;
                        snap.LastSession = activeSession.State.Contains("RDP", StringComparison.OrdinalIgnoreCase) || activeSession.State.Contains("Listen", StringComparison.OrdinalIgnoreCase) ? "RDP" : "Console";
                    }
                }

                return snap;
            }
            catch (Exception ex)
            {
                return new WmiSnapshot { Connected = false, Error = $"Critical: {ex.Message}", Status = "error" };
            }
        }

        private static async Task<ManagementScope?> ConnectScopeAsync(string ip, string username, string password, CancellationToken ct)
        {
            var options = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true,
                Timeout = TimeSpan.FromSeconds(8)
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                options.Username = username;
                options.Password = password ?? string.Empty;
            }

            var scope = new ManagementScope($"\\\\{ip}\\root\\cimv2", options);

            try
            {
                var connectTask = Task.Run(() =>
                {
                    try { scope.Connect(); }
                    catch (Exception ex) { AppLogger.Debug($"WMI connect attempt failed (may retry or timeout): {ex.GetType().Name}", category: "WMI", action: "Connect"); }
                }, ct);

                var timeoutTask = Task.Delay(_connectionTimeoutMs, ct);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completedTask == timeoutTask)
                    return null;

                await connectTask.ConfigureAwait(false);
                return scope.IsConnected ? scope : null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (COMException)
            {
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"WMI scope connect failed: {ex.GetType().Name}", category: "WMI", action: "Connect");
                return null;
            }
        }

        private static async Task CollectWmiMetricsGroupedAsync(ManagementScope scope, WmiSnapshot snap, CancellationToken ct, Action<WmiSnapshot>? onProgress, bool includeEventLogs)
        {
            var scopePath = scope.Path.Path;
            var options = scope.Options;

            // Create separate scopes for each parallel task to avoid thread-safety issues
            ManagementScope CreateScopeClone()
            {
                var s = new ManagementScope(scopePath, options);
                // No need to call s.Connect() here if it's going to be called by the searcher anyway,
                // or we can wrap it. But searchers will connect automatically.
                return s;
            }

            var tasks = new List<Task>
            {
                RunCollectorAsync("OS/RAM", CreateScopeClone, snap, s => CollectOsAndRamInfo(s, snap), onProgress, ct)
            };

            if (_monitorCpu)
                tasks.Add(RunCollectorAsync("CPU", CreateScopeClone, snap, s => CollectCpuInfo(s, snap), onProgress, ct));
            if (_monitorDisk)
                tasks.Add(RunCollectorAsync("Disk", CreateScopeClone, snap, s => CollectDiskInfo(s, snap), onProgress, ct));
            if (_monitorNetwork)
                tasks.Add(RunCollectorAsync("Network", CreateScopeClone, snap, s => CollectNetworkAndIpInfo(s, snap), onProgress, ct));

            tasks.Add(RunCollectorAsync("LastUser", CreateScopeClone, snap, s => CollectLastUserInfo(s, snap), onProgress, ct));
            tasks.Add(RunCollectorAsync("GPU", CreateScopeClone, snap, s => CollectGpuInfo(s, snap), onProgress, ct));
            tasks.Add(RunCollectorAsync("WindowsUpdate", CreateScopeClone, snap, s => CollectWindowsUpdateInfo(s, snap), onProgress, ct));
            tasks.Add(RunCollectorAsync("Firewall", CreateScopeClone, snap, s => CollectFirewallInfo(s, snap), onProgress, ct));
            tasks.Add(RunCollectorAsync("BitLocker", CreateScopeClone, snap, s => CollectBitLockerInfo(s, snap), onProgress, ct));
            tasks.Add(RunCollectorAsync("Defender", CreateScopeClone, snap, s => CollectDefenderInfo(s, snap), onProgress, ct));
            tasks.Add(RunCollectorAsync("OpenPorts", CreateScopeClone, snap, s => CollectOpenPortsInfo(s, snap), onProgress, ct));

            if (_monitorServices)
                tasks.Add(RunCollectorAsync("Services", CreateScopeClone, snap, s => CollectServicesInfo(s, snap), onProgress, ct));

            tasks.Add(RunCollectorAsync("Processes", CreateScopeClone, snap, s => CollectProcessesInfo(s, snap), onProgress, ct));

            if (_monitorEventLogs && includeEventLogs)
                tasks.Add(RunCollectorAsync("EventLog", CreateScopeClone, snap, s => CollectTimelineEvents(s, snap), onProgress, ct));

            await Task.WhenAll(tasks);

            try
            {
                snap.SystemStatusComponents = SystemStatusProbe.BuildComponents(snap, scope);
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"System status components failed: {ex.Message}", category: "WMI", action: "SystemStatus");
            }
        }

        private static async Task RunCollectorAsync(
            string collectorName,
            Func<ManagementScope> createScope,
            WmiSnapshot snap,
            Action<ManagementScope> collector,
            Action<WmiSnapshot>? onProgress,
            CancellationToken ct)
        {
            var entered = false;
            Task? collectorTask = null;
            try
            {
                await WmiConcurrencyGate.WaitAsync(ct).ConfigureAwait(false);
                entered = true;
                collectorTask = Task.Run(() =>
                {
                    try
                    {
                        var scope = createScope();
                        collector(scope);
                        onProgress?.Invoke(snap);
                    }
                    catch (Exception ex)
                    {
                        LogWmiProviderFailure(collectorName, scopePath: null, ex);
                    }
                }, CancellationToken.None);

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_operationTimeoutSec), ct);
                var completed = await Task.WhenAny(collectorTask, timeoutTask).ConfigureAwait(false);
                if (completed != collectorTask)
                {
                    AppLogger.Warning($"{collectorName} WMI collector timed out after {_operationTimeoutSec}s.", category: "WMI", action: "Timeout");
                    _ = collectorTask.ContinueWith(_ => WmiConcurrencyGate.Release(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    entered = false;
                    return;
                }
                await collectorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                AppLogger.Debug($"{collectorName} WMI collector cancelled.", category: "WMI", action: "Cancelled");
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure(collectorName, scopePath: null, ex);
            }
            finally
            {
                if (entered)
                    WmiConcurrencyGate.Release();
            }
        }

        private static async Task<T> WithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, T fallback, string operationName, string target, CancellationToken ct)
        {
            try
            {
                var completed = await Task.WhenAny(task, Task.Delay(timeout, ct)).ConfigureAwait(false);
                if (completed != task)
                {
                    AppLogger.Warning($"{operationName} timed out for {target} after {timeout.TotalSeconds:0}s.", category: "WMI", action: "Timeout");
                    return fallback;
                }

                return await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return fallback;
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure(operationName, target, ex);
                return fallback;
            }
        }

        private static void LogWmiProviderFailure(string collectorName, string? scopePath, Exception ex)
        {
            var message = ClassifyWmiFailure(ex);
            var location = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : $" ({scopePath})";
            AppLogger.Warning($"{collectorName} WMI collector unavailable{location}: {message}", ex, category: "WMI", action: collectorName);
        }

        internal static string ClassifyWmiFailure(Exception ex)
        {
            if (ex is OperationCanceledException)
                return "WMI operation cancelled.";
            if (ex is UnauthorizedAccessException)
                return "Access denied. The account does not have permission for this provider.";
            if (ex is COMException com)
            {
                return unchecked((uint)com.ErrorCode) switch
                {
                    0x80070005 => "Access denied by remote WMI/DCOM permissions.",
                    0x800706BA => "RPC server unavailable. Firewall, network, or remote service may be blocked.",
                    0x8004100E => "Invalid WMI namespace. Provider is not installed or not available on this Windows edition.",
                    0x80041013 => "Provider load failure.",
                    0x80041010 => "Invalid WMI class. Provider is missing or unsupported.",
                    _ => $"COM/WMI error 0x{unchecked((uint)com.ErrorCode):X8}."
                };
            }
            if (ex is ManagementException management)
                return $"WMI provider error: {management.ErrorCode}.";
            return $"WMI provider error: {ex.GetType().Name}.";
        }

        internal static ManagementObjectSearcher CreateTimedSearcher(ManagementScope scope, string query)
        {
            return CreateTimedSearcher(scope, new ObjectQuery(query));
        }

        internal static ManagementObjectSearcher CreateTimedSearcher(ManagementScope scope, ObjectQuery query)
        {
            return new ManagementObjectSearcher(scope, query, new EnumerationOptions
            {
                Timeout = TimeSpan.FromSeconds(_operationTimeoutSec),
                ReturnImmediately = true,
                Rewindable = false
            });
        }
        private static void CollectOsAndRamInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                using var searcherOs = CreateTimedSearcher(scope, new ObjectQuery("SELECT Caption, OSArchitecture, Version, BuildNumber, TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime FROM Win32_OperatingSystem"));
                using var collOs = searcherOs.Get();
                var osObj = collOs.Cast<ManagementObject>().FirstOrDefault();
                if (osObj != null)
                {
                    snap.OSName = osObj["Caption"]?.ToString();
                    snap.Compatibility = osObj["OSArchitecture"]?.ToString() ?? "x64";
                    snap.OSVersion = osObj["Version"]?.ToString();
                    snap.OSBuildNumber = osObj["BuildNumber"]?.ToString();
                    
                    var freeKb = Convert.ToDouble(osObj["FreePhysicalMemory"]);
                    var totalKb = Convert.ToDouble(osObj["TotalVisibleMemorySize"]);
                    if (totalKb > 0)
                    {
                        snap.TotalRamGb = totalKb / (1024.0 * 1024.0);
                        if (_monitorRam)
                            snap.RamUsedPct = ((totalKb - freeKb) / totalKb) * 100.0;
                    }
                    if (_monitorRam)
                        snap.RamFreeGb = freeKb / (1024.0 * 1024.0);
                    
                    var dtStr = osObj["LastBootUpTime"]?.ToString();
                    if (!string.IsNullOrEmpty(dtStr))
                    {
                        var dt = ManagementDateTimeConverter.ToDateTime(dtStr);
                        snap.Uptime = DateTime.Now - dt;
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private static void CollectNetworkAndIpInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                // Adapter Name
                using var searcherNet = CreateTimedSearcher(scope, new ObjectQuery("SELECT NetConnectionID FROM Win32_NetworkAdapter WHERE NetConnectionStatus=2"));
                using var collNet = searcherNet.Get();
                var netObj = collNet.Cast<ManagementObject>().FirstOrDefault();
                if (netObj != null)
                {
                    snap.NetworkAdapter = netObj["NetConnectionID"]?.ToString();
                }

                // IP Addresses
                using var searcherIp = CreateTimedSearcher(scope, new ObjectQuery("SELECT IPAddress, DNSServerSearchOrder, DefaultIPGateway, MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True"));
                using var collIp = searcherIp.Get();
                foreach (ManagementObject obj in collIp)
                {
                    var ips = obj["IPAddress"] as string[];
                    if (ips != null)
                    {
                        foreach (var ip in ips)
                        {
                            if (!ip.Contains(":") && !snap.IPAddresses.Contains(ip))
                                snap.IPAddresses.Add(ip);
                        }
                    }

                    var dnsServers = obj["DNSServerSearchOrder"] as string[];
                    if (dnsServers != null)
                    {
                        foreach (var dns in dnsServers.Where(x => !string.IsNullOrWhiteSpace(x)))
                        {
                            if (!snap.DnsServers.Contains(dns))
                                snap.DnsServers.Add(dns);
                        }
                    }

                    var gateways = obj["DefaultIPGateway"] as string[];
                    var gateway = gateways?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                    if (string.IsNullOrWhiteSpace(snap.DefaultGateway) && !string.IsNullOrWhiteSpace(gateway))
                        snap.DefaultGateway = gateway;

                    var mac = obj["MACAddress"]?.ToString();
                    if (string.IsNullOrWhiteSpace(snap.MacAddress) && !string.IsNullOrWhiteSpace(mac))
                        snap.MacAddress = mac;
                }

                CollectNetworkThroughput(scope, snap);
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private static void CollectNetworkThroughput(ManagementScope scope, WmiSnapshot snap)
        {
            snap.NetworkCountersAvailable = TryCollectNetworkRawCounters(scope, out var rx, out var tx);
            if (snap.NetworkCountersAvailable)
            {
                snap.NetworkBytesReceived = rx;
                snap.NetworkBytesSent = tx;
            }
        }

        private static void CollectGpuInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                using var searcher = CreateTimedSearcher(scope,
                    new ObjectQuery("SELECT Name, AdapterRAM FROM Win32_VideoController"));
                using var collection = searcher.Get();
                var gpu = collection.Cast<ManagementObject>()
                    .FirstOrDefault(obj => !string.IsNullOrWhiteSpace(obj["Name"]?.ToString()));
                if (gpu == null)
                    return;

                snap.GpuName = gpu["Name"]?.ToString();
                if (gpu["AdapterRAM"] != null)
                {
                    var bytes = Convert.ToDouble(gpu["AdapterRAM"]);
                    if (bytes > 0)
                        snap.GpuMemoryGb = bytes / (1024.0 * 1024.0 * 1024.0);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"GPU WMI read failed: {ex.Message}", category: "WMI", action: "GPU");
            }
        }

        private static void CollectWindowsUpdateInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                using var serviceSearcher = CreateTimedSearcher(scope,
                    "SELECT State, StartMode FROM Win32_Service WHERE Name='wuauserv'");
                using var serviceResults = serviceSearcher.Get();
                var service = serviceResults.Cast<ManagementObject>().FirstOrDefault();
                if (service != null)
                    snap.WindowsUpdateServiceStatus = $"{service["State"]} / {service["StartMode"]}";

                const string detectKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect";
                const string installKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install";
                const string auKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";
                const string rebootKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

                snap.WindowsUpdateLastCheck = ReadRegistryString(scope, detectKey, "LastSuccessTime");
                snap.WindowsUpdateLastInstall = ReadRegistryString(scope, installKey, "LastSuccessTime");
                snap.WindowsUpdateAutomaticUpdates = MapAuOption(ReadRegistryDword(scope, auKey, "AUOptions"));
                snap.WindowsUpdateRebootRequired = RegistryKeyExists(scope, rebootKey);
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure("WindowsUpdate", scope.Path.Path, ex);
            }
        }

        private static void CollectFirewallInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                using var serviceSearcher = CreateTimedSearcher(scope,
                    "SELECT State, StartMode FROM Win32_Service WHERE Name='mpssvc'");
                using var serviceResults = serviceSearcher.Get();
                var service = serviceResults.Cast<ManagementObject>().FirstOrDefault();
                if (service != null)
                    snap.FirewallServiceStatus = $"{service["State"]} / {service["StartMode"]}";

                const string policyRoot = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
                snap.FirewallDomainEnabled = ReadRegistryDword(scope, $@"{policyRoot}\DomainProfile", "EnableFirewall") switch
                {
                    0 => false,
                    1 => true,
                    _ => null
                };
                snap.FirewallPrivateEnabled = ReadRegistryDword(scope, $@"{policyRoot}\StandardProfile", "EnableFirewall") switch
                {
                    0 => false,
                    1 => true,
                    _ => null
                };
                snap.FirewallPublicEnabled = ReadRegistryDword(scope, $@"{policyRoot}\PublicProfile", "EnableFirewall") switch
                {
                    0 => false,
                    1 => true,
                    _ => null
                };
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure("Firewall", scope.Path.Path, ex);
            }
        }

        private static void CollectBitLockerInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                var server = scope.Path.Server;
                var bitLockerScope = new ManagementScope($@"\\{server}\root\CIMV2\Security\MicrosoftVolumeEncryption", scope.Options);
                bitLockerScope.Connect();
                if (!bitLockerScope.IsConnected)
                    return;

                using var searcher = CreateTimedSearcher(bitLockerScope,
                    "SELECT DriveLetter, ProtectionStatus, ConversionStatus, EncryptionPercentage FROM Win32_EncryptableVolume");
                using var results = searcher.Get();
                var volumes = new List<BitLockerVolumeRow>();
                foreach (ManagementObject volume in results)
                {
                    var drive = volume["DriveLetter"]?.ToString();
                    if (string.IsNullOrWhiteSpace(drive))
                        drive = volume["DeviceID"]?.ToString() ?? "Volume";

                    var protection = MapBitLockerProtection(ToNullableInt(volume["ProtectionStatus"]));
                    var conversion = MapBitLockerConversion(ToNullableInt(volume["ConversionStatus"]));
                    var percent = volume["EncryptionPercentage"]?.ToString();
                    volumes.Add(new BitLockerVolumeRow(
                        drive,
                        protection,
                        string.IsNullOrWhiteSpace(percent) ? "Not Available" : $"{percent}%",
                        conversion,
                        "Not Available"));
                }

                snap.BitLockerVolumes = volumes;
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure("BitLocker", scope.Path.Path, ex);
            }
        }

        private static void CollectDefenderInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                var server = scope.Path.Server;
                var defenderScope = new ManagementScope($@"\\{server}\root\Microsoft\Windows\Defender", scope.Options);
                defenderScope.Connect();
                if (!defenderScope.IsConnected)
                    return;

                using var statusSearcher = CreateTimedSearcher(defenderScope,
                    "SELECT RealTimeProtectionEnabled, AntivirusSignatureVersion, AMEngineVersion, QuickScanEndTime, FullScanEndTime, AntivirusEnabled FROM MSFT_MpComputerStatus");
                using var statusResults = statusSearcher.Get();
                var status = statusResults.Cast<ManagementObject>().FirstOrDefault();
                if (status != null)
                {
                    snap.DefenderRealTimeProtectionEnabled = ToNullableBool(status["RealTimeProtectionEnabled"]);
                    snap.DefenderSignatureVersion = status["AntivirusSignatureVersion"]?.ToString();
                    snap.DefenderEngineVersion = status["AMEngineVersion"]?.ToString();
                    snap.DefenderLastScan = status["FullScanEndTime"]?.ToString();
                    if (string.IsNullOrWhiteSpace(snap.DefenderLastScan))
                        snap.DefenderLastScan = status["QuickScanEndTime"]?.ToString();

                    var antivirusEnabled = ToNullableBool(status["AntivirusEnabled"]);
                    snap.DefenderHealthState = antivirusEnabled == true && snap.DefenderRealTimeProtectionEnabled == true
                        ? "Healthy"
                        : antivirusEnabled == false
                            ? "Disabled"
                            : "Attention Required";
                }

                using var threatSearcher = CreateTimedSearcher(defenderScope,
                    "SELECT ThreatID FROM MSFT_MpThreatDetection");
                using var threatResults = threatSearcher.Get();
                snap.DefenderThreatCount = threatResults.Count;
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure("Defender", scope.Path.Path, ex);
            }
        }

        private static void CollectOpenPortsInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                var processNames = new Dictionary<int, string>();
                using (var processSearcher = CreateTimedSearcher(scope,
                           "SELECT ProcessId, Name FROM Win32_Process"))
                using (var processResults = processSearcher.Get())
                {
                    foreach (ManagementObject process in processResults)
                    {
                        if (int.TryParse(process["ProcessId"]?.ToString(), out var pid))
                            processNames[pid] = process["Name"]?.ToString() ?? "Unknown";
                    }
                }

                var server = scope.Path.Server;
                var networkScope = new ManagementScope($@"\\{server}\root\StandardCimv2", scope.Options);
                networkScope.Connect();
                if (!networkScope.IsConnected)
                    return;

                var ports = new List<OpenPortRow>();
                using (var tcpSearcher = CreateTimedSearcher(networkScope,
                           "SELECT LocalPort, OwningProcess FROM MSFT_NetTCPConnection WHERE State=2"))
                using (var tcpResults = tcpSearcher.Get())
                {
                    foreach (ManagementObject port in tcpResults)
                        AddOpenPort(ports, "TCP", port["LocalPort"], port["OwningProcess"], processNames);
                }

                using (var udpSearcher = CreateTimedSearcher(networkScope,
                           "SELECT LocalPort, OwningProcess FROM MSFT_NetUDPEndpoint"))
                using (var udpResults = udpSearcher.Get())
                {
                    foreach (ManagementObject port in udpResults)
                        AddOpenPort(ports, "UDP", port["LocalPort"], port["OwningProcess"], processNames);
                }

                snap.OpenPorts = ports
                    .GroupBy(p => $"{p.Protocol}:{p.Port}:{p.Pid}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(p => p.Protocol)
                    .ThenBy(p => p.Port)
                    .Take(300)
                    .ToList();
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure("OpenPorts", scope.Path.Path, ex);
            }
        }

        private static void AddOpenPort(List<OpenPortRow> rows, string protocol, object? portValue, object? pidValue, IReadOnlyDictionary<int, string> processNames)
        {
            if (!int.TryParse(portValue?.ToString(), out var port))
                return;

            int.TryParse(pidValue?.ToString(), out var pid);
            processNames.TryGetValue(pid, out var process);
            rows.Add(new OpenPortRow(protocol, port, pid, string.IsNullOrWhiteSpace(process) ? "Unknown" : process, ClassifyOpenPortRisk(port, protocol)));
        }

        private static string ClassifyOpenPortRisk(int port, string protocol)
        {
            if (protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase) && port is 3389 or 5985 or 5986 or 445 or 135)
                return "High";
            if (port is 21 or 23 or 25 or 1433 or 3306 or 5432)
                return "Medium";
            return "Low";
        }

        private static bool? ToNullableBool(object? value)
        {
            try
            {
                return value == null ? null : Convert.ToBoolean(value);
            }
            catch
            {
                return null;
            }
        }

        private static int? ToNullableInt(object? value)
        {
            try
            {
                return value == null ? null : Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        private static string MapBitLockerProtection(int? status) => status switch
        {
            0 => "Off",
            1 => "On",
            2 => "Unknown",
            _ => "Not Available"
        };

        private static string MapBitLockerConversion(int? status) => status switch
        {
            0 => "Fully decrypted",
            1 => "Fully encrypted",
            2 => "Encryption in progress",
            3 => "Decryption in progress",
            4 => "Encryption paused",
            5 => "Decryption paused",
            _ => "Not Available"
        };

        private static string MapAuOption(uint? option) => option switch
        {
            2 => "Notify before download",
            3 => "Auto download / notify install",
            4 => "Automatic install",
            5 => "Managed by administrator",
            7 => "Auto download / scheduled install",
            _ => "Not Available"
        };

        private static string? ReadRegistryString(ManagementScope scope, string subKey, string valueName)
        {
            try
            {
                using var registry = new ManagementClass(scope, new ManagementPath("StdRegProv"), null);
                using var inParams = registry.GetMethodParameters("GetStringValue");
                inParams["hDefKey"] = 0x80000002u;
                inParams["sSubKeyName"] = subKey;
                inParams["sValueName"] = valueName;
                using var outParams = registry.InvokeMethod("GetStringValue", inParams, null);
                return Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u) == 0
                    ? outParams?["sValue"]?.ToString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static uint? ReadRegistryDword(ManagementScope scope, string subKey, string valueName)
        {
            try
            {
                using var registry = new ManagementClass(scope, new ManagementPath("StdRegProv"), null);
                using var inParams = registry.GetMethodParameters("GetDWORDValue");
                inParams["hDefKey"] = 0x80000002u;
                inParams["sSubKeyName"] = subKey;
                inParams["sValueName"] = valueName;
                using var outParams = registry.InvokeMethod("GetDWORDValue", inParams, null);
                return Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u) == 0
                    ? Convert.ToUInt32(outParams?["uValue"] ?? 0u)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool RegistryKeyExists(ManagementScope scope, string subKey)
        {
            try
            {
                using var registry = new ManagementClass(scope, new ManagementPath("StdRegProv"), null);
                using var inParams = registry.GetMethodParameters("EnumValues");
                inParams["hDefKey"] = 0x80000002u;
                inParams["sSubKeyName"] = subKey;
                using var outParams = registry.InvokeMethod("EnumValues", inParams, null);
                return Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u) == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sums cumulative byte counters from active interfaces (Win32_PerfRawData_Tcpip_NetworkInterface).
        /// </summary>
        public static bool TryCollectNetworkRawCounters(ManagementScope scope, out ulong bytesReceived, out ulong bytesSent)
        {
            bytesReceived = 0;
            bytesSent = 0;

            try
            {
                using var searcher = CreateTimedSearcher(scope,
                    new ObjectQuery(
                        "SELECT Name, BytesReceivedPerSec, BytesSentPerSec FROM Win32_PerfRawData_Tcpip_NetworkInterface " +
                        "WHERE Name!='_Total' AND Name NOT LIKE '%Loopback%' AND Name NOT LIKE '%isatap%' " +
                        "AND Name NOT LIKE '%Teredo%' AND Name NOT LIKE '%6TO4%'"));
                using var coll = searcher.Get();
                var found = false;
                foreach (ManagementObject obj in coll)
                {
                    var name = obj["Name"]?.ToString() ?? string.Empty;
                    if (IsDisabledNetworkInterface(name))
                        continue;

                    bytesReceived += ToUInt64(obj["BytesReceivedPerSec"]);
                    bytesSent += ToUInt64(obj["BytesSentPerSec"]);
                    found = true;
                }

                return found;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Network raw counter read failed: {ex.Message}", category: "Network", action: "RawCounter");
                return false;
            }
        }

        private static bool IsDisabledNetworkInterface(string name) =>
            name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
            || name.Contains("isatap", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Teredo", StringComparison.OrdinalIgnoreCase);

        private static ulong ToUInt64(object? value)
        {
            if (value == null) return 0;
            try { return Convert.ToUInt64(value); }
            catch { return 0; }
        }

        /// <summary>
        /// Fast WMI poll for live chart/network sampling (CPU/RAM/Disk + network counters only).
        /// </summary>
        public static async Task<WmiSnapshot?> GetLightweightMetricsAsync(
            string ip,
            string username,
            string password,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return null;

            var scope = await ConnectScopeAsync(ip, username, password, ct).ConfigureAwait(false);
            if (scope == null)
                return null;

            var entered = false;
            try
            {
                await WmiConcurrencyGate.WaitAsync(ct).ConfigureAwait(false);
                entered = true;
                var work = Task.Run(() =>
                {
                    var snap = new WmiSnapshot
                    {
                        Connected = true,
                        Ip = ip,
                        Username = username,
                        CollectedAt = DateTime.Now,
                        DataSource = SnapshotDataSources.Wmi
                    };

                    if (_monitorCpu)
                        CollectCpuInfo(scope, snap);
                    CollectOsAndRamInfo(scope, snap);
                    if (_monitorDisk)
                        CollectDiskInfo(scope, snap);
                    if (_monitorNetwork)
                        CollectNetworkThroughput(scope, snap);
                    return snap;
                }, CancellationToken.None);

                var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(_operationTimeoutSec), ct)).ConfigureAwait(false);
                if (completed != work)
                {
                    AppLogger.Warning($"Lightweight WMI collector timed out after {_operationTimeoutSec}s.", category: "WMI", action: "Timeout");
                    _ = work.ContinueWith(_ => WmiConcurrencyGate.Release(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    entered = false;
                    return null;
                }
                return await work.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                LogWmiProviderFailure("Lightweight", ip, ex);
                return null;
            }
            finally
            {
                if (entered)
                    WmiConcurrencyGate.Release();
            }
        }

        private static void CollectLastUserInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                using var searcherComp = CreateTimedSearcher(scope, new ObjectQuery("SELECT UserName, Domain, Manufacturer, Model, SystemType FROM Win32_ComputerSystem"));
                using var collComp = searcherComp.Get();
                var compObj = collComp.Cast<ManagementObject>().FirstOrDefault();
                snap.LastUser = compObj?["UserName"]?.ToString();
                snap.DomainName = compObj?["Domain"]?.ToString();
                snap.Manufacturer = compObj?["Manufacturer"]?.ToString();
                snap.Model = compObj?["Model"]?.ToString();
                snap.MachineType = compObj?["SystemType"]?.ToString();

                if (string.IsNullOrEmpty(snap.LastUser))
                {
                    using var searcherProc = CreateTimedSearcher(scope, new ObjectQuery("SELECT Handle FROM Win32_Process WHERE Name='explorer.exe'"));
                    using var collProc = searcherProc.Get();
                    foreach (ManagementObject proc in collProc)
                    {
                        try {
                            var outParams = proc.InvokeMethod("GetOwner", null, null);
                            if (outParams != null && outParams["ReturnValue"]?.ToString() == "0")
                            {
                                var user = outParams["User"]?.ToString();
                                var domain = outParams["Domain"]?.ToString();
                                snap.LastUser = string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
                                snap.LastSession = "Active";
                                break;
                            }
                        } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    }
                }

                if (string.IsNullOrEmpty(snap.LastUser))
                {
                    using var searcherLogon = CreateTimedSearcher(scope, new ObjectQuery("SELECT Antecedent FROM Win32_LoggedOnUser"));
                    using var collLogon = searcherLogon.Get();
                    foreach (ManagementObject logon in collLogon)
                    {
                        var ant = logon["Antecedent"]?.ToString();
                        if (ant != null && !ant.Contains("SERVICE", StringComparison.OrdinalIgnoreCase) && !ant.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(ant, "Domain=\"(.*?)\",Name=\"(.*?)\"");
                            if (match.Success)
                            {
                                snap.LastUser = $"{match.Groups[1].Value}\\{match.Groups[2].Value}";
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(snap.LastUser))
                {
                    var parts = snap.LastUser.Contains("\\") ? snap.LastUser.Split('\\') : new[] { "", snap.LastUser };
                    var userName = parts.Last();
                    using var searcherLogin = CreateTimedSearcher(scope, new ObjectQuery($"SELECT LastLogon FROM Win32_NetworkLoginProfile WHERE Name='{userName}' OR Name='{snap.LastUser.Replace("\\","\\\\")}'"));
                    using var collLogin = searcherLogin.Get();
                    var loginObj = collLogin.Cast<ManagementObject>().FirstOrDefault();
                    if (loginObj != null)
                    {
                        var lastLogonStr = loginObj["LastLogon"]?.ToString();
                        if (!string.IsNullOrEmpty(lastLogonStr))
                        {
                            try {
                                var dt = ManagementDateTimeConverter.ToDateTime(lastLogonStr);
                                snap.LastLogon = dt.ToString("dd.MM.yyyy HH:mm");
                            } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        }
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }


        private static void CollectCpuInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                using var searcher = CreateTimedSearcher(scope, new ObjectQuery("SELECT Name, LoadPercentage FROM Win32_Processor"));
                using var collection = searcher.Get();
                double totalLoad = 0; int count = 0;
                foreach (ManagementObject obj in collection)
                {
                    if (snap.CPUModel == null) snap.CPUModel = obj["Name"]?.ToString();
                    totalLoad += Convert.ToDouble(obj["LoadPercentage"]);
                    count++;
                }
                if (count > 0) snap.CpuPct = (int)Math.Round(totalLoad / count);
                snap.CPUCoreCount = count;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        private static void CollectDiskInfo(ManagementScope scope, WmiSnapshot snap)
{
    try
    {
        using var searcher = CreateTimedSearcher(scope, new ObjectQuery("SELECT DeviceID, FreeSpace, Size FROM Win32_LogicalDisk WHERE DriveType=3"));
        using var collection = searcher.Get();
        
        double totalSize = 0;
        double totalFree = 0;
        double largestNonC = 0;

        foreach (ManagementObject obj in collection)
        {
            var drive = obj["DeviceID"]?.ToString();
            var free = Convert.ToDouble(obj["FreeSpace"]);
            var size = Convert.ToDouble(obj["Size"]);
            
            if (size > 0)
            {
                totalSize += size;
                totalFree += free;

                if (drive != null && !drive.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                {
                    if (size > largestNonC) largestNonC = size;
                }
            }
        }

        if (totalSize > 0)
        {
            snap.DiskTotalGb = totalSize / (1024.0 * 1024.0 * 1024.0);
            snap.DiskFreeGb = totalFree / (1024.0 * 1024.0 * 1024.0);
            snap.DiskUsedPct = ((totalSize - totalFree) / totalSize) * 100.0;
        }
    }
    catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
}


        private static void CollectServicesInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Services] Collecting from: {scope.Path.Path}");
                
                // Fetching more properties for better analysis
                using var searcher = CreateTimedSearcher(scope, new ObjectQuery("SELECT Name, DisplayName, State, StartMode FROM Win32_Service"));
                using var collection = searcher.Get();
                
                var results = new List<ServiceRow>();
                var criticalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { 
                    "wuauserv", "mpssvc", "TermService", "RemoteRegistry", "WinRM", 
                    "LanmanServer", "Dnscache", "Dhcp", "EventLog", "SamSs",
                    "W3SVC", "IISADMIN", "MSSQLSERVER", "SQLBrowser", "SQLWriter",
                    "NTDS", "IsmServ", "Kdc", "Netlogon", "DNS", "UmRdpService", "SessionEnv",
                    "SmtpSvc"
                };

                foreach (ManagementObject obj in collection)
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    var display = obj["DisplayName"]?.ToString() ?? name;
                    var state = obj["State"]?.ToString() ?? "Unknown";
                    var startMode = obj["StartMode"]?.ToString() ?? "Manual";

                    bool isCritical = criticalNames.Contains(name);

                    // Expanded: Include if critical OR starts with role-specific prefixes (SQL, IIS, etc.)
                    bool isRoleRelated = name.StartsWith("SQL", StringComparison.OrdinalIgnoreCase) || 
                                         name.StartsWith("IIS", StringComparison.OrdinalIgnoreCase);

                    // We want to show:
                    // 1. All critical services (running OR stopped)
                    // 2. All role-related services (running OR stopped)
                    // 3. Other stopped services that are not Disabled
                    
                    bool shouldInclude = isCritical || isRoleRelated ||
                                        (!state.Equals("Running", StringComparison.OrdinalIgnoreCase) && 
                                         !startMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase));

                    if (shouldInclude)
                    {
                        var lastStopHint = "";
                        if (isCritical && state.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
                        {
                            lastStopHint = "Security Risk: Critical service is not running.";
                        }

                        results.Add(new ServiceRow(
                            Name: name,
                            Display: display,
                            State: state,
                            IsCritical: isCritical,
                            StartMode: startMode,
                            Dependencies: "",
                            LastStopHint: lastStopHint
                        ));
                    }
                }
                
                snap.Services.Clear();
                
                // Sort: Critical ones first, then by name
                var sortedServices = results
                    .OrderByDescending(s => s.IsCritical)
                    .ThenBy(s => s.Name)
                    .Take(300) 
                    .ToList();

                foreach (var s in sortedServices) snap.Services.Add(s);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Services] Error collecting services: {ex.Message}");
            }
        }

        private static void CollectProcessesInfo(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                // Note: KernelModeTime/UserModeTime units are 100-nanoseconds.
                // For a quick snapshot, we'll just get the current working set and name.
                // Proper CPU % requires two samples, so we'll just sort by WorkingSetSize (RAM) for now
                // as a proxy for "Heaviest" processes, or use PercentProcessorTime from Win32_PerfFormattedData.
                using var searcher = CreateTimedSearcher(scope, new ObjectQuery("SELECT Name, IDProcess, PercentProcessorTime, WorkingSetPrivate FROM Win32_PerfFormattedData_PerfProc_Process WHERE Name != '_Total' AND Name != 'Idle'"));
                using var collection = searcher.Get();
                
                var procs = new List<ProcessRow>();
                foreach (ManagementObject obj in collection)
                {
                    var name = obj["Name"]?.ToString() ?? "Unknown";
                    var id = Convert.ToInt32(obj["IDProcess"]);
                    var cpu = Convert.ToDouble(obj["PercentProcessorTime"]);
                    var ramBytes = Convert.ToDouble(obj["WorkingSetPrivate"]);
                    
                    procs.Add(new ProcessRow(name, id, cpu, ramBytes / (1024.0 * 1024.0)));
                }
                
                snap.TopProcesses = procs.OrderByDescending(p => p.CpuPct).ThenByDescending(p => p.RamMb).Take(10).ToList();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        public static async Task<(uint Code, string Output)> ExecuteServiceActionAsync(string ip, string username, string password, string serviceName, string action)
        {
            try
            {
                var scope = CreateManagementScope(ip, username, password);
                await Task.Run(() => scope.Connect());

                if (!scope.IsConnected) return (999, "WMI bağlantısı kurulamadı.");

                using var searcher = CreateTimedSearcher(scope, new ObjectQuery($"SELECT * FROM Win32_Service WHERE Name='{serviceName}'"));
                using var collection = searcher.Get();
                var service = collection.Cast<ManagementObject>().FirstOrDefault();
                
                if (service == null) return (404, $"Servis '{serviceName}' bulunamadı.");

                // Get current state before action
                string stateBefore = service["State"]?.ToString() ?? "Unknown";
                string startMode = service["StartMode"]?.ToString() ?? "Unknown";

                object? result = null;
                switch (action.ToLowerInvariant())
                {
                    case "start":
                        result = service.InvokeMethod("StartService", null);
                        break;
                    case "stop":
                        result = service.InvokeMethod("StopService", null);
                        break;
                    case "restart":
                        service.InvokeMethod("StopService", null);
                        await Task.Delay(3000);
                        result = service.InvokeMethod("StartService", null);
                        break;
                }

                if (result == null) return (8, "InvokeMethod null döndü.");
                uint code = Convert.ToUInt32(result);

                // Wait and verify the actual state after the action
                await Task.Delay(2000);
                string stateAfter = "Unknown";
                try
                {
                    service.Get(); // Refresh
                    stateAfter = service["State"]?.ToString() ?? "Unknown";
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

                string output = $"WMI Kodu: {code}\nÖnceki Durum: {stateBefore}\nSonraki Durum: {stateAfter}\nBaşlangıç Modu: {startMode}";

                // If WMI said success but service didn't actually change state, report as failure
                if (code == 0 && action == "start" && !stateAfter.Equals("Running", StringComparison.OrdinalIgnoreCase))
                {
                    return (8888, output + "\n\nServis başlatma komutu kabul edildi ama servis çalışmıyor. Bu servis 'Trigger Start' modunda olabilir ve elle başlatılamaz.");
                }
                if (code == 0 && action == "stop" && !stateAfter.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
                {
                    return (8888, output + "\n\nServis durdurma komutu kabul edildi ama servis hâlâ çalışıyor.");
                }

                return (code, output);
            }
            catch (Exception ex)
            {
                return (999, $"Hata: {ex.Message}");
            }
        }

        public static async Task<(bool Success, string Message)> ExecuteSessionActionAsync(string ip, string username, string password, int sessionId, string action)
        {
            if (!action.Equals("logoff", StringComparison.OrdinalIgnoreCase))
                return (false, "Unsupported session action.");

            try
            {
                var args = $"{sessionId} /server:{ip}";
                var (exitCode, output, error) = await ActiveSessionCollector.RunCommandWithExitCodeAsync(
                    "rwinsta", args, username, password ?? string.Empty).ConfigureAwait(false);

                if (exitCode == 0)
                    return (true, "Session logoff command succeeded.");

                var detail = string.Join(" ", new[] { error, output }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                if (string.IsNullOrWhiteSpace(detail))
                    detail = $"rwinsta exited with code {exitCode}.";

                return (false, detail);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Remote logoff failed.", ex);
                return (false, "Session logoff failed. Verify credentials and permissions.");
            }
        }

        public static async Task<(bool Success, string Message)> ExecuteOsPowerActionAsync(
            string ip,
            string username,
            string password,
            bool reboot,
            Func<string, Task<bool>>? rdpCommandExecutor = null)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return (false, "Sunucu IP adresi boş.");

            var errors = new List<string>();
            var shellCommand = reboot
                ? "shutdown /r /f /t 0 /c \"Netvium restart\""
                : "shutdown /s /f /t 0 /c \"Netvium shutdown\"";

            // 1) Aktif RDP oturumu — WMI/RPC gerekmez
            if (rdpCommandExecutor != null)
            {
                try
                {
                    if (await rdpCommandExecutor(shellCommand))
                        return (true, reboot ? "RDP oturumu üzerinden yeniden başlatma gönderildi." : "RDP oturumu üzerinden kapatma gönderildi.");
                    errors.Add("RDP oturumu komutu gönderilemedi.");
                }
                catch (Exception ex)
                {
                    errors.Add($"RDP: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(username))
                return (false, errors.Count > 0 ? string.Join(" | ", errors) : "Sunucu kullanıcı adı tanımlı değil.");

            // 2) WNet IPC$ + shutdown /m
            var wnetShutdown = await TryWNetRemoteShutdownAsync(ip, username, password ?? string.Empty, reboot);
            if (wnetShutdown.Success)
                return wnetShutdown;
            errors.Add(wnetShutdown.Message);

            // 3) PowerShell WinRM (Invoke-Command)
            var psShutdown = await TryPowerShellRemoteShutdownAsync(ip, username, password ?? string.Empty, reboot);
            if (psShutdown.Success)
                return psShutdown;
            errors.Add(psShutdown.Message);

            // 4) net use + shutdown.exe
            var remoteShutdown = await TryRemoteShutdownAsync(ip, username, password ?? string.Empty, reboot);
            if (remoteShutdown.Success)
                return remoteShutdown;
            errors.Add(remoteShutdown.Message);

            // 5) WMI (RPC açıksa)
            var scope = await TryConnectManagementScopeAsync(ip, username, password ?? string.Empty);
            if (scope != null)
            {
                var wmiReboot = await Task.Run(() => TryWmiRebootOrShutdown(scope, reboot));
                if (wmiReboot.Success) return wmiReboot;
                errors.Add(wmiReboot.Message);

                var wmiProcess = await Task.Run(() => TryWmiProcessPowerCommand(scope, reboot));
                if (wmiProcess.Success) return wmiProcess;
                errors.Add(wmiProcess.Message);
            }
            else
            {
                errors.Add("WMI/RPC kullanılamıyor.");
            }

            return (false, errors.Count == 1 ? errors[0] : string.Join(" | ", errors.Distinct()));
        }

        private static async Task<(bool Success, string Message)> TryWNetRemoteShutdownAsync(string ip, string username, string password, bool reboot)
        {
            if (string.IsNullOrWhiteSpace(password))
                return (false, "Uzak shutdown için şifre gerekli.");

            var ipcPath = $@"\\{ip}\IPC$";
            await Task.Run(() => NativeMethods.WNetCancelConnection2(ipcPath, 0, true));

            var connected = false;
            string? lastError = null;
            foreach (var user in GetCredentialUserCandidates(username, ip))
            {
                var result = await Task.Run(() => TryWNetConnect(ipcPath, user, password));
                if (result == NativeMethods.NO_ERROR || result == NativeMethods.ERROR_SESSION_CREDENTIAL_CONFLICT)
                {
                    connected = true;
                    break;
                }

                lastError = FormatWin32Error(result);
            }

            if (!connected)
                return (false, $"IPC$ bağlantısı kurulamadı: {lastError ?? "bilinmeyen hata"}");

            try
            {
                var shutdownOk = await Task.Run(() =>
                {
                    try
                    {
                        return NativeMethods.InitiateSystemShutdownEx(ip, "Netvium", 0, true, reboot, 0);
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (shutdownOk)
                    return (true, reboot ? "Yeniden başlatma komutu gönderildi." : "Kapatma komutu gönderildi.");

                var args = reboot
                    ? $@"/m \\{ip} /r /f /t 0 /c ""Netvium restart"""
                    : $@"/m \\{ip} /s /f /t 0 /c ""Netvium shutdown""";

                var (code, output, err) = await ActiveSessionCollector.RunCommandWithExitCodeAsync(
                    @"C:\Windows\System32\shutdown.exe", args, "", "", timeoutMs: 20000);

                if (code == 0)
                    return (true, reboot ? "Yeniden başlatma komutu gönderildi." : "Kapatma komutu gönderildi.");

                var detail = string.Join(" ", new[] { err, output }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                if (string.IsNullOrWhiteSpace(detail))
                    detail = $"shutdown.exe kod: {code}";

                return (false, detail);
            }
            finally
            {
                await Task.Run(() => NativeMethods.WNetCancelConnection2(ipcPath, 0, true));
            }
        }

        private static int TryWNetConnect(string remotePath, string user, string password)
        {
            var resource = new NativeMethods.NETRESOURCE
            {
                dwScope = NativeMethods.RESOURCE_GLOBALNET,
                dwType = NativeMethods.RESOURCETYPE_ANY,
                lpRemoteName = remotePath
            };
            return NativeMethods.WNetAddConnection2(ref resource, password, user, NativeMethods.CONNECT_TEMPORARY);
        }

        private static async Task<(bool Success, string Message)> TryPowerShellRemoteShutdownAsync(string ip, string username, string password, bool reboot)
        {
            if (string.IsNullOrWhiteSpace(password))
                return (false, "PowerShell remoting için şifre gerekli.");

            var psUser = FormatPowerShellCredentialUser(username, ip);
            var flag = reboot ? "/r" : "/s";
            var script = $@"
$ErrorActionPreference = 'Stop'
$sec = ConvertTo-SecureString '{EscapePowerShellSingleQuoted(password)}' -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential('{EscapePowerShellSingleQuoted(psUser)}', $sec)
Invoke-Command -ComputerName '{EscapePowerShellSingleQuoted(ip)}' -Credential $cred -ScriptBlock {{ shutdown.exe {flag} /f /t 0 /c 'Netvium' }}
";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var (code, output, err) = await ActiveSessionCollector.RunCommandWithExitCodeAsync(
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                "", "", timeoutMs: 30000);

            if (code == 0)
                return (true, reboot ? "PowerShell ile yeniden başlatma gönderildi." : "PowerShell ile kapatma gönderildi.");

            var detail = string.Join(" ", new[] { err, output }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (string.IsNullOrWhiteSpace(detail) || detail.Contains("Connecting to remote server", StringComparison.OrdinalIgnoreCase))
                return (false, "PowerShell remoting (WinRM) kullanılamıyor.");

            return (false, detail.Length > 180 ? detail[..180] + "…" : detail);
        }

        private static IEnumerable<string> GetCredentialUserCandidates(string username, string ip)
        {
            var user = username.Trim();
            if (user.Contains('\\') || user.Contains('@'))
            {
                yield return user;
                yield break;
            }

            yield return $@"{ip}\{user}";
            yield return user;
        }

        private static string FormatPowerShellCredentialUser(string username, string ip)
        {
            var user = username.Trim();
            if (user.Contains('\\') || user.Contains('@'))
                return user;
            return $@"{ip}\{user}";
        }

        private static string EscapePowerShellSingleQuoted(string value)
            => value.Replace("'", "''");

        private static string FormatWin32Error(int code)
            => code == 0 ? "OK" : new System.ComponentModel.Win32Exception(code).Message;

        private static async Task<(bool Success, string Message)> TryRemoteShutdownAsync(string ip, string username, string password, bool reboot)
        {
            try
            {
                await EstablishRemoteIpcSessionAsync(ip, username, password);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }

            var args = reboot
                ? $"/m \\\\{ip} /r /f /t 0 /c \"Netvium restart\""
                : $"/m \\\\{ip} /s /f /t 0 /c \"Netvium shutdown\"";

            var (code1, output1, err1) = await ActiveSessionCollector.RunCommandWithExitCodeAsync(
                @"C:\Windows\System32\shutdown.exe", args, "", "", timeoutMs: 20000);
            if (code1 == 0)
                return (true, reboot ? "Yeniden başlatma komutu gönderildi." : "Kapatma komutu gönderildi.");

            var (code2, output2, err2) = await ActiveSessionCollector.RunCommandWithExitCodeAsync(
                @"C:\Windows\System32\shutdown.exe", args, username, password, timeoutMs: 20000);
            if (code2 == 0)
                return (true, reboot ? "Yeniden başlatma komutu gönderildi." : "Kapatma komutu gönderildi.");

            var detail = string.Join(" ", new[] { err1, output1, err2, output2 }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (string.IsNullOrWhiteSpace(detail))
                detail = $"shutdown.exe kod: {code2}";

            if (detail.Contains("RPC server is unavailable", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("1722", StringComparison.OrdinalIgnoreCase))
            {
                detail += " Uzak sunucuda File/Printer Sharing, RPC (135) ve admin yetkisi gerekir.";
            }

            return (false, detail);
        }

        private static async Task EstablishRemoteIpcSessionAsync(string ip, string username, string password)
        {
            var ipcPath = $@"\\{ip}\IPC$";
            await Task.Run(() => NativeMethods.WNetCancelConnection2(ipcPath, 0, true));

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return;

            string? lastError = null;
            foreach (var user in GetCredentialUserCandidates(username, ip))
            {
                var code = await Task.Run(() => TryWNetConnect(ipcPath, user, password));
                if (code == NativeMethods.NO_ERROR || code == NativeMethods.ERROR_SESSION_CREDENTIAL_CONFLICT)
                    return;
                lastError = FormatWin32Error(code);
            }

            throw new InvalidOperationException($"Uzak oturum açılamadı: {lastError ?? "bilinmeyen hata"}");
        }

        private static async Task<ManagementScope?> TryConnectManagementScopeAsync(string ip, string username, string password)
        {
            try
            {
                var scope = CreateManagementScope(ip, username, password);
                await Task.Run(() =>
                {
                    try { scope.Connect(); }
                    catch (COMException) { }
                    catch (ManagementException) { }
                });
                return scope.IsConnected ? scope : null;
            }
            catch
            {
                return null;
            }
        }

        private static (bool Success, string Message) TryWmiRebootOrShutdown(ManagementScope scope, bool reboot)
        {
            using var searcher = CreateTimedSearcher(scope, new ObjectQuery("SELECT * FROM Win32_OperatingSystem"));
            using var collection = searcher.Get();
            var os = collection.Cast<ManagementObject>().FirstOrDefault();
            if (os == null)
                return (false, "Win32_OperatingSystem bulunamadı.");

            // 1) Dedicated WMI Reboot/Shutdown methods
            try
            {
                var methodName = reboot ? "Reboot" : "Shutdown";
                var code = GetInvokeReturnValue(os.InvokeMethod(methodName, null, null));
                if (code == 0)
                    return (true, reboot ? "WMI Reboot komutu gönderildi." : "WMI Shutdown komutu gönderildi.");
                if (code != 999)
                    return (false, $"{methodName} kodu: {DescribeShutdownCode(code)}");
            }
            catch (ManagementException)
            {
                // Method not available on this OS — fall through to Win32Shutdown.
            }

            // 2) Win32Shutdown flags: 1=shutdown, 2=reboot, 4=force
            var inParams = os.GetMethodParameters("Win32Shutdown");
            uint flags = reboot ? 6u : 5u;
            inParams["Flags"] = flags;
            inParams["Reserved"] = 0;
            var shutdownCode = GetInvokeReturnValue(os.InvokeMethod("Win32Shutdown", inParams, null));
            if (shutdownCode == 0)
                return (true, reboot ? "Win32Shutdown reboot gönderildi." : "Win32Shutdown shutdown gönderildi.");

            return (false, $"Win32Shutdown kodu: {DescribeShutdownCode(shutdownCode)}");
        }

        private static (bool Success, string Message) TryWmiProcessPowerCommand(ManagementScope scope, bool reboot)
        {
            var cmd = reboot
                ? @"C:\Windows\System32\shutdown.exe /r /f /t 0 /c ""Netvium"""
                : @"C:\Windows\System32\shutdown.exe /s /f /t 0 /c ""Netvium""";

            using var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
            var inParams = processClass.GetMethodParameters("Create");
            inParams["CommandLine"] = cmd;
            inParams["CurrentDirectory"] = @"C:\Windows\System32";
            var outParams = processClass.InvokeMethod("Create", inParams, null) as ManagementBaseObject;
            if (outParams == null)
                return (false, "Win32_Process.Create yanıt vermedi.");

            var code = Convert.ToUInt32(outParams["ReturnValue"]);
            if (code == 0)
                return (true, reboot ? "Uzak shutdown /r başlatıldı." : "Uzak shutdown /s başlatıldı.");

            return (false, $"Win32_Process.Create kodu: {DescribeProcessCreateCode(code)}");
        }

        private static uint GetInvokeReturnValue(object? result)
        {
            if (result == null) return 999;
            if (result is ManagementBaseObject mbo)
            {
                if (mbo.Properties["ReturnValue"]?.Value != null)
                    return Convert.ToUInt32(mbo["ReturnValue"]);
            }

            try { return Convert.ToUInt32(result); }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to parse WMI shutdown return value.", ex);
                return 999;
            }
        }

        private static string DescribeShutdownCode(uint code) => code switch
        {
            0 => "Başarılı",
            2 => "Erişim reddedildi (admin gerekli)",
            5 => "Zaten kapatılıyor",
            1190 => "Kapatma zaten devam ediyor",
            _ => code.ToString()
        };

        private static string DescribeProcessCreateCode(uint code) => code switch
        {
            0 => "Başarılı",
            2 => "Erişim reddedildi",
            3 => "Yetersiz yetki",
            8 => "Bilinmeyen hata",
            21 => "Geçersiz parametre",
            _ => code.ToString()
        };

        public static ManagementScope CreateManagementScope(string ip, string username, string password) =>
            CreateManagementScopeInternal(ip, username, password);

        private static ManagementScope CreateManagementScopeInternal(string ip, string username, string password)
        {
            var options = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true,
                Timeout = TimeSpan.FromSeconds(15)
            };

            ApplyWmiCredentials(options, username, password, ip);
            return new ManagementScope($@"\\{ip}\root\cimv2", options);
        }

        private static void ApplyWmiCredentials(ConnectionOptions options, string username, string password, string? remoteComputer = null)
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            var user = username.Trim();
            options.Password = password ?? string.Empty;

            if (user.Contains('\\'))
            {
                var parts = user.Split('\\', 2);
                options.Authority = $"ntlmdomain:{parts[0]}";
                options.Username = parts[1];
            }
            else if (user.Contains('@'))
            {
                options.Username = user;
            }
            else
            {
                options.Username = user;
                if (!string.IsNullOrWhiteSpace(remoteComputer))
                    options.Authority = $"ntlmdomain:{remoteComputer}";
            }
        }

        private static List<SessionRow> GetSessionsViaWmi(ManagementScope scope)
        {
            var rows = new List<SessionRow>();
            try
            {
                // Get sessions with active logon types: 2 (Interactive/Console), 10 (RemoteInteractive/RDP)
                var sessionsDict = new Dictionary<string, (string Type, string StartTime)>();
                using var searcherSession = CreateTimedSearcher(scope, new ObjectQuery("SELECT LogonId, LogonType, StartTime FROM Win32_LogonSession WHERE LogonType=2 OR LogonType=10"));
                using var collSession = searcherSession.Get();
                foreach (ManagementObject sess in collSession)
                {
                    var id = sess["LogonId"]?.ToString();
                    var type = sess["LogonType"]?.ToString() == "10" ? "RDP" : "Console";
                    var startStr = sess["StartTime"]?.ToString();
                    var startTime = "-";
                    if (!string.IsNullOrEmpty(startStr))
                    {
                        try { startTime = ManagementDateTimeConverter.ToDateTime(startStr).ToString("dd.MM HH:mm"); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    }
                    if (id != null) sessionsDict[id] = (type, startTime);
                }

                // Map sessions to users
                using var searcherLogon = CreateTimedSearcher(scope, new ObjectQuery("SELECT Antecedent, Dependent FROM Win32_LoggedOnUser"));
                using var collLogon = searcherLogon.Get();
                foreach (ManagementObject logon in collLogon)
                {
                    var dep = logon["Dependent"]?.ToString(); // Win32_LogonSession.LogonId="..."
                    var ant = logon["Antecedent"]?.ToString(); // Win32_Account.Domain="...",Name="..."
                    
                    if (dep != null && ant != null)
                    {
                        var idMatch = Regex.Match(dep, "LogonId=\"(.*?)\"");
                        if (idMatch.Success && sessionsDict.TryGetValue(idMatch.Groups[1].Value, out var sessInfo))
                        {
                            var (type, startTime) = sessInfo;
                            var rawIdStr = idMatch.Groups[1].Value;
                            int? numericId = int.TryParse(rawIdStr, out var idVal) ? idVal : (int?)null;

                            var userMatch = Regex.Match(ant, "Domain=\"(.*?)\",Name=\"(.*?)\"");
                            if (userMatch.Success)
                            {
                                var domain = userMatch.Groups[1].Value;
                                var user = userMatch.Groups[2].Value;
                                
                                // Filter system noise
                                if (user.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || 
                                    user.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) ||
                                    user.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) ||
                                    user.StartsWith("DWM-", StringComparison.OrdinalIgnoreCase) ||
                                    user.StartsWith("UMFD-", StringComparison.OrdinalIgnoreCase) ||
                                    user.EndsWith("$") ||
                                    domain.Equals("Window Manager", StringComparison.OrdinalIgnoreCase) ||
                                    domain.Equals("Font Driver Host", StringComparison.OrdinalIgnoreCase)) continue;

                                rows.Add(new SessionRow($"{domain}\\{user}", type, "-", startTime, Id: numericId));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            return rows;
        }
        private static void CollectTimelineEvents(ManagementScope scope, WmiSnapshot snap)
        {
            try
            {
                if (!scope.IsConnected) scope.Connect();
                
                var events = new List<TimelineEvent>();
                // Add mining indicator while we query
                snap.Timeline = new List<TimelineEvent> { new TimelineEvent(DateTime.Now, "System", "Info", "Event Log madenciliği yapılıyor (Son 5 Gün)...", "🔍") };

                // Increase lookback to 7 days for deep pulse extraction
                var lookback = DateTime.Now.AddDays(-7);
                var wmiTime = ToWmiTimestamp(lookback);

                // 1. System Events (Pulse Indicator)
                int sysCount = 0;
                try
                {
                    var sysQuery = $"SELECT EventCode, TimeGenerated, Message, SourceName FROM Win32_NTLogEvent " +
                                   $"WHERE Logfile = 'System' AND (EventCode = 7036 OR EventCode = 1074 OR EventCode = 1076 OR EventCode = 6005 OR EventCode = 6006 OR EventCode = 6008 OR EventCode = 19 OR EventCode = 43 OR EventCode = 7045) " +
                                   $"AND TimeGenerated > '{wmiTime}'";

                    using var sysSearcher = CreateTimedSearcher(scope, new ObjectQuery(sysQuery));
                    using var sysColl = sysSearcher.Get();
                    foreach (ManagementObject obj in sysColl)
                    {
                        sysCount++;
                        var code = obj["EventCode"]?.ToString();
                        var timeStr = obj["TimeGenerated"]?.ToString() ?? "";
                        var msg = obj["Message"]?.ToString() ?? "";
                        var src = obj["SourceName"]?.ToString() ?? "System";
                        var ts = ManagementDateTimeConverter.ToDateTime(timeStr);

                        if (code == "7036")
                        {
                            if (msg.Contains("stopped", StringComparison.OrdinalIgnoreCase))
                                events.Add(new TimelineEvent(ts, "Service", "Warning", $"{src}: {msg}", "🛑"));
                            else if (msg.Contains("running", StringComparison.OrdinalIgnoreCase))
                                events.Add(new TimelineEvent(ts, "Service", "Success", $"{src} çalışmaya başladı", "🟢"));
                        }
                        else if (code == "1074" || code == "1076")
                            events.Add(new TimelineEvent(ts, "System", "Warning", $"Restart: {msg}", "⚠️"));
                        else if (code == "6005")
                            events.Add(new TimelineEvent(ts, "System", "Success", "Sistem Başlatıldı", "🚀"));
                        else if (code == "6008")
                            events.Add(new TimelineEvent(ts, "System", "Error", "Beklenmedik Kapanma", "🛑"));
                        else if (code == "19" || code == "43")
                            events.Add(new TimelineEvent(ts, "Update", "Success", $"Update Tamamlandı: {msg}", "🆙"));
                        else if (code == "7045")
                            events.Add(new TimelineEvent(ts, "Service", "Info", $"Yeni Servis: {src}", "🆕"));
                    }
                }
                catch (Exception ex) 
                { 
                    System.Diagnostics.Debug.WriteLine($"[Timeline-System] Error: {ex.Message}");
                }

                // 2. Security Events (Logon Pulse) - Lighter time filter to avoid timezone bugs
                int secCount = 0;
                try
                {
                    // Process 4624 events separately for IP extraction
                    var secQuery4624 = new SelectQuery("Win32_NTLogEvent", $"Logfile='Security' AND EventCode=4624 AND TimeGenerated > '{wmiTime}'");
                    using (var searcher4624 = CreateTimedSearcher(scope, secQuery4624))
                    {
                        searcher4624.Options.Timeout = TimeSpan.FromSeconds(5);
                        foreach (ManagementObject obj in searcher4624.Get())
                        {
                            try
                            {
                                var timeStr = obj["TimeGenerated"]?.ToString();
                                var dt = !string.IsNullOrEmpty(timeStr) ? ManagementDateTimeConverter.ToDateTime(timeStr) : DateTime.Now;
                                
                                string msg = obj["Message"]?.ToString() ?? "";
                                string ip = "-";

                                // Extract Source IP from logon message
                                if (msg.Contains("Source Network Address:"))
                                {
                                    var parts = msg.Split(new[] { "Source Network Address:" }, StringSplitOptions.None);
                                    if (parts.Length > 1)
                                    {
                                        ip = parts[1].Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                                        if (ip == "-" || ip == "::1") ip = "Local"; 
                                    }
                                }

                                events.Add(new TimelineEvent(dt, "Logon", "Success", "Sisteme giriş yapıldı (Sec-Logon)", "🟢", ip));
                                secCount++;
                            }
                            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        }
                    }

                    // Process other security events (4625, 4648)
                    var secQueryOther = $"SELECT EventCode, TimeGenerated, Message FROM Win32_NTLogEvent " +
                                        $"WHERE Logfile = 'Security' AND (EventCode = 4625 OR EventCode = 4648) " +
                                        $"AND TimeGenerated > '{wmiTime}'";

                    using var secSearcherOther = CreateTimedSearcher(scope, new ObjectQuery(secQueryOther));
                    using var secCollOther = secSearcherOther.Get();
                    foreach (ManagementObject obj in secCollOther)
                    {
                        secCount++;
                        var code = obj["EventCode"]?.ToString();
                        var timeStr = obj["TimeGenerated"]?.ToString() ?? "";
                        var ts = ManagementDateTimeConverter.ToDateTime(timeStr);

                        if (code == "4625")
                            events.Add(new TimelineEvent(ts, "Logon", "Warning", "Hatalı Giriş Denemesi", "🔐"));
                        else if (code == "4648")
                            events.Add(new TimelineEvent(ts, "Logon", "Success", "Uzaktan Bağlantı OK", "🟢"));
                    }
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

                // 3. RDP Core Events (TerminalServices Operational) - BROAD QUERY
                int rdpCount = 0;
                try
                {
                    var rdpQuery = $"SELECT EventCode, TimeGenerated, Message FROM Win32_NTLogEvent " +
                                   $"WHERE Logfile = 'Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational' " +
                                   $"AND (EventCode = 1149 OR EventCode = 21 OR EventCode = 24 OR EventCode = 25)";

                    using var rdpSearcher = CreateTimedSearcher(scope, new ObjectQuery(rdpQuery));
                    using var rdpColl = rdpSearcher.Get();
                    foreach (ManagementObject obj in rdpColl)
                    {
                        rdpCount++;
                        var code = obj["EventCode"]?.ToString();
                        var timeStr = obj["TimeGenerated"]?.ToString() ?? "";
                        var ts = ManagementDateTimeConverter.ToDateTime(timeStr);

                        // Only add recent ones manually here if needed, but WMI query without time is safer to see if ANY exist
                        if (ts > lookback) {
                            if (code == "1149")
                                events.Add(new TimelineEvent(ts, "RDP", "Success", "RDP Bağlantısı Başarılı", "🟢"));
                            else if (code == "21")
                                events.Add(new TimelineEvent(ts, "Logon", "Success", "Hesaba giriş yapıldı (Oturum Açıldı)", "🟢"));
                        }
                    }
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

                var sorted = events.OrderByDescending(e => e.Timestamp).Take(100).ToList();
                if (!sorted.Any())
                {
                    int totalFound = sysCount + secCount + rdpCount;
                    string hint = totalFound > 0 ? $"({totalFound} olay taranmış ama kriterlere uymadı)" : "(Log kütüklerine erişilemedi veya boş)";
                    sorted.Add(new TimelineEvent(DateTime.Now, "System", "Info", $"Sistem stabil. {hint}", "📊"));
                }
                snap.Timeline = sorted;
                snap.Timeline = sorted;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Timeline-Global] Error: {ex.Message}");
            }
        }


        public static async Task<(bool Success, string Message)> ControlServiceAsync(string ip, string username, string password, string serviceName, string action)
        {
            try
            {
                var options = new ConnectionOptions { Username = username, Password = password, EnablePrivileges = true, Timeout = TimeSpan.FromSeconds(10) };
                var scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                await Task.Run(() => scope.Connect());

                var query = new ObjectQuery($"SELECT * FROM Win32_Service WHERE Name = '{serviceName}'");
                using var searcher = CreateTimedSearcher(scope, query);
                using var collection = searcher.Get();

                foreach (ManagementObject service in collection)
                {
                    uint result = 0;
                    switch (action.ToLower())
                    {
                        case "start":
                            result = (uint)service.InvokeMethod("StartService", null);
                            break;
                        case "stop":
                            result = (uint)service.InvokeMethod("StopService", null);
                            break;
                        case "restart":
                            service.InvokeMethod("StopService", null);
                            // Wait a bit for stop
                            await Task.Delay(2000);
                            result = (uint)service.InvokeMethod("StartService", null);
                            break;
                    }

                    if (result == 0) return (true, "İşlem başarıyla tamamlandı.");
                    if (result == 10) return (false, "Servis zaten istenen durumda.");
                    return (false, $"Hata kodu: {result}");
                }

                return (false, "Servis bulunamadı.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static string ToWmiTimestamp(DateTime dt)
        {
            return ManagementDateTimeConverter.ToDmtfDateTime(dt);
        }
    }
}
