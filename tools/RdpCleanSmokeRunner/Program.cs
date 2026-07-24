using System.Diagnostics;
using System.Text.Json;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Rdp;
using Netvium.Rdp.Clean;
using Netvium.Services;
using Netvium.Services.AIGuide;

const int iterations = 20;
const int settleMs = 2500;

var server = ResolveServer(out var resolveError);
if (string.IsNullOrWhiteSpace(server.Password))
{
    Console.Error.WriteLine(resolveError ?? "No RDP password. Set NETVIUM_RDP_PASSWORD or add WebApp to connections.json.");
    return 2;
}

var exitCode = 1;
var thread = new Thread(() => exitCode = RunSmoke(server, iterations));
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join(TimeSpan.FromMinutes(45));
Environment.Exit(exitCode);
return exitCode;

static ServerInfo ResolveServer(out string? error)
{
    error = null;
    var host = Environment.GetEnvironmentVariable("NETVIUM_RDP_HOST");
    var user = Environment.GetEnvironmentVariable("NETVIUM_RDP_USER");
    var pass = Environment.GetEnvironmentVariable("NETVIUM_RDP_PASSWORD");

    if (string.IsNullOrWhiteSpace(pass))
    {
        if (TryLoadWebAppFromConnections(out var loaded, out var loadError))
            return loaded!;

        error = loadError;
        return new ServerInfo("Smoke", host ?? "10.40.0.15", user ?? "Administrator", string.Empty, string.Empty);
    }

    return new ServerInfo("Smoke", host ?? "10.40.0.15", user ?? "Administrator", pass, string.Empty);
}

static bool TryLoadWebAppFromConnections(out ServerInfo? server, out string? error)
{
    server = null;
    error = null;

    try
    {
        var path = StorageService.GetConnectionsPath();
        if (!File.Exists(path))
        {
            error = $"connections.json not found: {path}";
            return false;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var s in doc.RootElement.GetProperty("Servers").EnumerateArray())
        {
            var name = s.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() : null;
            var ip = s.TryGetProperty("Ip", out var ipEl) ? ipEl.GetString() : null;
            if (!string.Equals(name, "WebApp", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ip, "10.40.0.15", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var username = s.TryGetProperty("Username", out var userEl) ? userEl.GetString() ?? "Administrator" : "Administrator";
            var encPassword = s.TryGetProperty("Password", out var passEl) ? passEl.GetString() ?? string.Empty : string.Empty;
            var password = encPassword;
            if (password.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase))
            {
                var protectedPart = password["dpapi:".Length..];
                if (!CredentialProtector.TryUnprotect(protectedPart, out password) || string.IsNullOrWhiteSpace(password))
                {
                    error = "WebApp found in connections.json but dpapi password decrypt failed. Run smoke test in your interactive user session or set NETVIUM_RDP_PASSWORD.";
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                error = "WebApp found in connections.json but password is empty. Set NETVIUM_RDP_PASSWORD.";
                return false;
            }

            var folder = s.TryGetProperty("Folder", out var folderEl) ? folderEl.GetString() ?? string.Empty : string.Empty;
            server = new ServerInfo(name ?? "WebApp", ip ?? "10.40.0.15", username, password, folder);
            return true;
        }

        error = "WebApp / 10.40.0.15 not found in connections.json. Set NETVIUM_RDP_PASSWORD.";
        return false;
    }
    catch (Exception ex)
    {
        error = $"Failed to read connections.json: {ex.Message}";
        return false;
    }
}

static void PumpMessagesUntil(Task task, TimeSpan maxWait)
{
    var sw = Stopwatch.StartNew();
    while (!task.IsCompleted && sw.Elapsed < maxWait)
    {
        Application.DoEvents();
        Thread.Sleep(25);
    }
}

static RdpSessionHostControl PrepareHost(Form form)
{
    var hostControl = new RdpSessionHostControl { Dock = DockStyle.Fill };
    form.Controls.Add(hostControl);
    form.Show();
    form.PerformLayout();
    hostControl.EnsureActiveXCreated();

    for (var attempt = 0; attempt < 200 && !hostControl.IsLayoutReadyForConnect(); attempt++)
    {
        form.PerformLayout();
        hostControl.SyncRdpBounds();
        Application.DoEvents();
        Thread.Sleep(50);
    }

    return hostControl;
}

static RdpSessionHostControl RecreateHost(Form form, RdpSessionHostControl? previous)
{
    if (previous != null)
    {
        form.Controls.Remove(previous);
        try { previous.Dispose(); } catch { /* optional */ }
        Application.DoEvents();
        Thread.Sleep(300);
    }

    return PrepareHost(form);
}

static int RunSmoke(ServerInfo server, int count)
{
    using var form = new Form
    {
        Text = "RdpCleanSmokeRunner",
        Width = 1280,
        Height = 800,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-5000, -5000),
        ShowInTaskbar = false,
    };

    var hostControl = PrepareHost(form);
    var (layoutW, layoutH) = hostControl.GetClientPixelSize();
    if (!hostControl.IsLayoutReadyForConnect())
    {
        Console.Error.WriteLine($"Layout not ready after warmup: {layoutW}x{layoutH}");
        return 2;
    }

    Console.WriteLine($"Layout ready: {layoutW}x{layoutH}");

    var loginTimes = new List<double>();
    var failures = new List<string>();
    var blankFrames = 0;
    var resizeOk = true;
    var reconnectOk = true;

    Console.WriteLine($"=== RdpCleanSmokeRunner target={server.Ip} iterations={count} ===");

    for (var i = 1; i <= count; i++)
    {
        using var session = new RdpCleanSession();
        Console.Write($"[{i}/{count}] connect... ");
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var connectTask = session.ConnectAsync(server, hostControl, cts.Token);
            PumpMessagesUntil(connectTask, TimeSpan.FromSeconds(95));
            connectTask.GetAwaiter().GetResult();
            sw.Stop();

            if (!session.IsConnected || !hostControl.IsRdpConnected)
            {
                failures.Add($"#{i}: not connected ({session.ConnectionState}) {session.LastError}");
                Console.WriteLine($"FAIL ({session.ConnectionState}: {session.LastError})");
                session.Disconnect();
                Application.DoEvents();
                Thread.Sleep(500);
                hostControl = RecreateHost(form, hostControl);
                continue;
            }

            loginTimes.Add(sw.Elapsed.TotalSeconds);
            Console.Write($"{sw.Elapsed.TotalSeconds:F1}s ");

            Thread.Sleep(settleMs);
            Application.DoEvents();

            var metrics = hostControl.GetActiveXMetrics();
            var snapshot = new RdpWarmupSnapshot
            {
                HostHandle = metrics.Handle,
                Width = metrics.Width,
                Height = metrics.Height,
            };

            var frameOk = RdpFrameProbe.IsNonBlankFrameAsync(snapshot, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (!frameOk)
            {
                blankFrames++;
                Console.Write("blank ");
            }

            if (i == 5 || i == 15)
            {
                form.Width = i == 5 ? 1024 : 1440;
                form.Height = i == 5 ? 768 : 900;
                form.Update();
                Application.DoEvents();
                hostControl.ApplyFillFromSurface(syncSessionDisplay: true);
                hostControl.SyncSessionDisplaySettings();
                Application.DoEvents();
                Thread.Sleep(800);

                var after = hostControl.GetActiveXMetrics();
                if (after.Width <= 0 || after.Height <= 0 || !session.IsConnected)
                {
                    resizeOk = false;
                    failures.Add($"#{i}: resize failed {after.Width}x{after.Height}");
                    Console.Write("resize-fail ");
                }
                else
                {
                    Console.Write("resize-ok ");
                }
            }

            if (i == 10)
            {
                var reconnectTask = session.ReconnectAsync(hostControl, CancellationToken.None);
                PumpMessagesUntil(reconnectTask, TimeSpan.FromSeconds(95));
                reconnectTask.GetAwaiter().GetResult();
                Application.DoEvents();
                Thread.Sleep(settleMs);
                if (!session.IsConnected)
                {
                    reconnectOk = false;
                    failures.Add("#10: reconnect failed");
                    Console.Write("reconnect-fail ");
                }
                else
                {
                    Console.Write("reconnect-ok ");
                }
            }

            session.Disconnect();
            Application.DoEvents();
            Thread.Sleep(1500);
            hostControl = RecreateHost(form, hostControl);
            Console.WriteLine("OK");
        }
        catch (Exception ex)
        {
            sw.Stop();
            failures.Add($"#{i}: {ex.Message}");
            Console.WriteLine($"FAIL ({ex.Message})");
            try { session.Disconnect(); } catch { /* optional */ }
            Application.DoEvents();
            Thread.Sleep(500);
            hostControl = RecreateHost(form, hostControl);
        }
    }

    try { hostControl.Dispose(); } catch { /* optional */ }
    form.Close();

    var passCount = count - failures.Count;
    var avgLogin = loginTimes.Count > 0 ? loginTimes.Average() : 0;
    var maxLogin = loginTimes.Count > 0 ? loginTimes.Max() : 0;

    Console.WriteLine();
    Console.WriteLine($"Passed: {passCount}/{count}");
    Console.WriteLine($"LoginComplete avg={avgLogin:F1}s max={maxLogin:F1}s (n={loginTimes.Count})");
    Console.WriteLine($"Blank frames after settle: {blankFrames}/{loginTimes.Count}");
    Console.WriteLine($"Resize checks: {(resizeOk ? "PASS" : "FAIL")}");
    Console.WriteLine($"Reconnect check: {(reconnectOk ? "PASS" : "FAIL")}");

    if (failures.Count > 0)
    {
        Console.WriteLine("Failures:");
        foreach (var f in failures)
            Console.WriteLine($"  - {f}");
    }

    var allPass = failures.Count == 0 && passCount == count && blankFrames == 0 && resizeOk && reconnectOk;
    Console.WriteLine(allPass ? "ALL PASS" : "SOME FAILED");
    return allPass ? 0 : 1;
}
