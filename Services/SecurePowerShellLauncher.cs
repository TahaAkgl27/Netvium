using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Netvium.Helpers;

namespace Netvium.Services
{
    public static class SecurePowerShellLauncher
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
        private static readonly object Gate = new();

        private static string PendingDeletePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Netvium", "pending_ps_scripts.txt");

        public static void CleanupPendingScriptsOnStartup()
        {
            try
            {
                if (!File.Exists(PendingDeletePath))
                    return;

                var remaining = new List<string>();
                foreach (var line in File.ReadAllLines(PendingDeletePath))
                {
                    var path = line?.Trim();
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (TryDeleteScript(path))
                        continue;

                    remaining.Add(path);
                }

                lock (Gate)
                {
                    if (remaining.Count == 0)
                    {
                        if (File.Exists(PendingDeletePath))
                            File.Delete(PendingDeletePath);
                    }
                    else
                    {
                        File.WriteAllLines(PendingDeletePath, remaining, Utf8NoBom);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Pending PowerShell script cleanup failed.", ex, category: "PowerShell", action: "Cleanup");
            }
        }

        public static Process? LaunchRemoteConsole(string ip, string user, string password)
            => LaunchRemoteConsoleAsync(ip, user, password, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

        public static async Task<Process?> LaunchRemoteConsoleAsync(
            string ip,
            string user,
            string password,
            CancellationToken cancellationToken,
            TimeSpan? startupTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return null;

            Process? process = null;
            try
            {
                process = Process.Start(CreateStartInfo());
                if (process == null)
                    return null;

                var timeout = startupTimeout ?? TimeSpan.FromSeconds(10);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                await WriteRuntimeInputAsync(process, ip, user, password, timeoutCts.Token).ConfigureAwait(false);
                return process;
            }
            catch (OperationCanceledException)
            {
                KillProcess(process);
                throw;
            }
            catch (Exception ex)
            {
                KillProcess(process);
                AppLogger.Error(
                    $"Failed to launch secure PowerShell console type={ex.GetType().Name} hresult={ex.HResult}",
                    ex,
                    category: "PowerShell",
                    action: "Launch");
                throw;
            }
        }

        public static string BuildBootstrapScript()
            => "$ErrorActionPreference = 'Stop'\r\n"
                + "$ip = [Console]::In.ReadLine()\r\n"
                + "$user = [Console]::In.ReadLine()\r\n"
                + "$plainPassword = [Console]::In.ReadLine()\r\n"
                + "$securePassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force\r\n"
                + "$credential = New-Object System.Management.Automation.PSCredential($user, $securePassword)\r\n"
                + "try { Enter-PSSession -ComputerName $ip -Credential $credential } catch { Write-Host 'Uzak PowerShell bağlantısı kurulamadı.'; Read-Host 'Press Enter to close' }\r\n";

        private static ProcessStartInfo CreateStartInfo()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                WindowStyle = ProcessWindowStyle.Normal,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(BuildBootstrapScript());
            return psi;
        }

        private static async Task WriteRuntimeInputAsync(
            Process process,
            string ip,
            string user,
            string password,
            CancellationToken cancellationToken)
        {
            await process.StandardInput.WriteLineAsync(ip.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync((user ?? string.Empty).AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync((password ?? string.Empty).AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        private static void KillProcess(Process? process)
        {
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { /* bounded teardown is best effort */ }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }

        private static bool TryDeleteScript(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return !File.Exists(path);
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    $"Could not delete legacy PowerShell script: {Path.GetFileName(path)} type={ex.GetType().Name}",
                    category: "PowerShell", action: "Delete");
                return false;
            }
        }
    }
}