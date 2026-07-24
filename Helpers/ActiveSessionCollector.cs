using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Netvium.Models;
using Netvium.Native;

namespace Netvium.Helpers
{
    public static class ActiveSessionCollector
    {
        // P/Invoke constants
        private const int ERROR_IO_PENDING = 997;

        public static async Task<List<SessionRow>> GetSessionsAsync(string ip, string username, string password)
        {
            var rows = new List<SessionRow>();

            // 1. PRIMARY: WTS API (Try twice if 997)
            try
            {
                var wtsResult = TryGetSessionsViaWts(ip);
                if (wtsResult.Success)
                {
                    return wtsResult.Sessions; // Success
                }

                if (wtsResult.ErrorCode == ERROR_IO_PENDING)
                {
                    // Retry once after delay
                    await Task.Delay(1500);
                    var retryResult = TryGetSessionsViaWts(ip);
                    if (retryResult.Success)
                    {
                        return retryResult.Sessions;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WTS failed: {ex.Message}");
            }

            // 2 & 3. FALLBACKS: query user and qwinsta (Parallel)
            try
            {
                var queryTask = RunCommandAsync("query", $"user /server:{ip}", username, password);
                var qwinstaTask = RunCommandAsync("qwinsta", $"/server:{ip}", username, password);
                await Task.WhenAll(queryTask, qwinstaTask);

                var queryOutput = await queryTask;
                if (!string.IsNullOrWhiteSpace(queryOutput))
                {
                    var queryRows = ParseQueryUserOutput(queryOutput);
                    if (queryRows.Count > 0) return queryRows;
                }

                var qwinstaOutput = await qwinstaTask;
                if (!string.IsNullOrWhiteSpace(qwinstaOutput))
                {
                    var qwinstaRows = ParseQwinstaOutput(qwinstaOutput);
                    if (qwinstaRows.Count > 0) return qwinstaRows;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            return rows;
        }

        private static (bool Success, List<SessionRow> Sessions, int ErrorCode) TryGetSessionsViaWts(string ip)
        {
            var list = new List<SessionRow>();
            IntPtr serverHandle = IntPtr.Zero;
            
            try
            {
                serverHandle = NativeMethods.WTSOpenServer(ip);
                if (serverHandle == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    return (false, list, err);
                }

                IntPtr sessionInfo = IntPtr.Zero;
                int count = 0;

                // Attempt enumeration
                if (!NativeMethods.WTSEnumerateSessions(serverHandle, 0, 1, out sessionInfo, out count))
                {
                    var err = Marshal.GetLastWin32Error();
                    return (false, list, err);
                }

                try
                {
                    var dataSize = Marshal.SizeOf(typeof(NativeMethods.WTS_SESSION_INFO));
                    for (int i = 0; i < count; i++)
                    {
                        var p = IntPtr.Add(sessionInfo, i * dataSize);
                        var si = Marshal.PtrToStructure<NativeMethods.WTS_SESSION_INFO>(p);

                        // Query details using centralized helpers
                        var user = NativeMethods.QueryWtsString(serverHandle, si.SessionID, NativeMethods.WTS_INFO_CLASS.WTSUserName);
                        var domain = NativeMethods.QueryWtsString(serverHandle, si.SessionID, NativeMethods.WTS_INFO_CLASS.WTSDomainName);
                        var state = si.State.ToString();
                        
                        if (!string.IsNullOrWhiteSpace(user))
                        {
                            var fullUser = string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
                            
                            var idle = NativeMethods.QueryWtsTime(serverHandle, si.SessionID, NativeMethods.WTS_INFO_CLASS.WTSIdleTime, isDuration: true);
                            var logon = NativeMethods.QueryWtsTime(serverHandle, si.SessionID, NativeMethods.WTS_INFO_CLASS.WTSLogonTime, isDuration: false);
                            
                            string displayState = state;
                            if (state.Equals("WTSActive", StringComparison.OrdinalIgnoreCase)) displayState = "Active";
                            else if (state.Equals("WTSDisconnected", StringComparison.OrdinalIgnoreCase)) displayState = "Disconnected";

                            list.Add(new SessionRow(fullUser, displayState, idle, logon, Id: si.SessionID));
                        }
                    }
                }
                finally
                {
                    if (sessionInfo != IntPtr.Zero) NativeMethods.WTSFreeMemory(sessionInfo);
                }

                return (true, list, 0);
            }
            catch (Exception)
            {
                return (false, list, -1);
            }
            finally
            {
                if (serverHandle != IntPtr.Zero) NativeMethods.WTSCloseServer(serverHandle);
            }
        }


        public static async Task<(int ExitCode, string Output, string Error)> RunCommandWithExitCodeAsync(string command, string args, string username, string password, int timeoutMs = 15000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    var ss = new SecureString();
                    foreach (char c in password) ss.AppendChar(c);
                    psi.Password = ss;
                    if (username.Contains('\\'))
                    {
                        var parts = username.Split('\\', 2);
                        psi.Domain = parts[0];
                        psi.UserName = parts[1];
                    }
                    else
                    {
                        psi.UserName = username;
                    }
                }

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                var readOut = proc.StandardOutput.ReadToEndAsync();
                var readErr = proc.StandardError.ReadToEndAsync();
                var exited = await Task.Run(() => proc.WaitForExit(timeoutMs));
                if (!exited)
                {
                    try { proc.Kill(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    return (-1, "", "Komut zaman aşımına uğradı.");
                }

                return (proc.ExitCode, await readOut, await readErr);
            }
            catch (Exception ex)
            {
                return (-1, "", ex.Message);
            }
        }

        public static async Task<string> RunCommandAsync(string command, string args, string username, string password)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Add credentials if provided
                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                     psi.Domain = ""; // Often helpful to split domain/user if needed, but ProcessStartInfo takes user/domain separately
                     // However, setting password requires SecureString which is annoying here.
                     // IMPORTANT: Running as another user requires "LoadUserProfile" usually or specific privileges.
                     // The user request said "Commands must run with provided domain credentials".
                     // NET USE is the standard way to auth against a remote server for RPC/pipes without changing process identity.
                     // If WMI worked, we likely have an authenticated session (IPC$).
                     // If not, we can try to run `cmd /c net use \\IP\ipc$ ...` but that's complex.
                     // For now, let's assume the session is authenticated via WMI or the user sets it up.
                     // BUT, ProcessStartInfo with Password needs a SecureString.
                     
                     // Implementation Note: Since we cannot easily convert string to SecureString in a safe, simple snippet without unsafe code or specific contexts,
                     // and given that WMI Connect() establishes a logon session for the target, `qwinsta /server:IP` *might* piggyback on that if it uses the same LUID.
                     // Alternatively, `cmdkey /add:IP /user:U /pass:P` is a trick.
                     
                     // Let's stick to simple execution first. If we need explicit creds passed to the process itself, we need SecureString.
                     var ss = new SecureString();
                     foreach(char c in password) ss.AppendChar(c);
                     psi.Password = ss;
                     psi.UserName = username;
                     // Extract domain if present in username
                     if (username.Contains("\\")) {
                         var parts = username.Split('\\');
                         psi.Domain = parts[0];
                         psi.UserName = parts[1];
                     }
                }

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                
                // Timeout 5s
                var readTask = proc.StandardOutput.ReadToEndAsync();
                var waitTask = Task.Run(() => proc.WaitForExit(5000));
                
                if (await Task.WhenAny(waitTask, Task.Delay(5000)) == waitTask && proc.HasExited)
                {
                    return await readTask;
                }
                else
                {
                    try { proc.Kill(); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    return "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Command failed: {ex.Message}");
                return "";
            }
        }

        private static List<SessionRow> ParseQueryUserOutput(string output)
        {
            // Format:
            // USERNAME              SESSIONNAME        ID  STATE   IDLE TIME  LOGON TIME
            // administrator         rdp-tcp#0           2  Active          .  2/20/2026 9:00 AM
            
            var rows = new List<SessionRow>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Skip header
            foreach (var line in lines.Skip(1))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // quser format: USERNAME SESSIONNAME ID STATE IDLE TIME LOGON TIME
                // Split by multiple spaces
                var parts = Regex.Split(trimmed, @"\s{2,}");
                
                if (parts.Length >= 2)
                {
                    var user = parts[0].StartsWith(">") ? parts[0].Substring(1) : parts[0];
                    if (user.Equals("USERNAME", StringComparison.OrdinalIgnoreCase)) continue;

                    string state = "-";
                    string idle = "-";
                    string logon = "-";

                    // The ID field is usually numeric. If parts[1] is numeric, it's ID. 
                    // If not, parts[1] is SESSIONNAME.
                    if (parts.Length > 3)
                    {
                        // Minimal heuristic to find state
                        // Usually: User [Session] ID State Idle Logon
                        state = parts.FirstOrDefault(p => p.Equals("Active", StringComparison.OrdinalIgnoreCase) || 
                                                       p.Equals("Aktif", StringComparison.OrdinalIgnoreCase) ||
                                                       p.Equals("Disc", StringComparison.OrdinalIgnoreCase) ||
                                                       p.Equals("Disconnected", StringComparison.OrdinalIgnoreCase)) ?? "-";
                        
                        // If we didn't find active/aktif in parts, try to use indices
                        if (state == "-" && parts.Length >= 4) state = parts[3];
                        
                        idle = parts.Length > 4 ? parts[4] : "-";
                        logon = parts.Length > 5 ? string.Join(" ", parts.Skip(5)) : "-";
                    }

                    int? sessionId = null;
                    if (parts.Length > 2 && int.TryParse(parts[2], out var id)) sessionId = id;

                    rows.Add(new SessionRow(user, state, idle, logon, Id: sessionId));
                }
            }
            return rows;
        }

        private static List<SessionRow> ParseQwinstaOutput(string output)
        {
             // SESSIONNAME       USERNAME                 ID  STATE   TYPE        DEVICE
             // rdp-tcp#0         Administrator             2  Active
             var rows = new List<SessionRow>();
             var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

             // Use the ID-anchored logic we developed earlier, it's robust
             foreach (var line in lines.Skip(1))
             {
                 var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                 if (parts.Length < 3) continue;

                 string user = "-";
                 string state = "-";
                 bool foundId = false;

                 for (int i = 0; i < parts.Length; i++)
                 {
                     if (int.TryParse(parts[i], out _))
                     {
                         foundId = true;
                         // ID is at index i.
                         if (i + 1 < parts.Length) state = parts[i + 1];
                         if (i > 0) user = parts[i - 1];

                         // Normalize state if it's "Aktif" (Turkish) or "Active"
                         if (state.Equals("Aktif", StringComparison.OrdinalIgnoreCase)) state = "Active";

                         var lower = user.ToLowerInvariant();
                         // Filter out session names masquerading as users
                         if (lower.StartsWith("rdp-") || lower.StartsWith("ica-") || lower == "console" || lower == "services")
                         {
                             // If this is index 1, it means column 0 was sessionname.
                             // e.g. "console 1 Active". User is missing.
                             if (i == 1) user = "-"; 
                         }
                         break;
                     }
                 }

                 if (foundId && user != "-" && user.Length > 1) 
                 {
                      int? sessionId = null;
                      // Find the ID part again or use a captured one
                      for (int j = 0; j < parts.Length; j++) if (int.TryParse(parts[j], out var id)) { sessionId = id; break; }

                      rows.Add(new SessionRow(user, state, "-", "-", Id: sessionId));
                 }
             }
             return rows;
        }

    }
}
