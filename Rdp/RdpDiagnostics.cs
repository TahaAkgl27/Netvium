using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using System.IO;
using Netvium.Helpers;

namespace Netvium.Rdp
{
    public class RdpDiagnosticResult
    {
        public bool IsStaThread { get; set; }
        public bool ClsidRegistered { get; set; }
        public bool MstscExists { get; set; }
        public bool CanCreateInstance { get; set; }
        public bool PortReachable { get; set; }
        public string? OSArchitecture { get; set; }
        public string? SelectedActiveXVersion { get; set; }
        public string? SelectedClsid { get; set; }
        public string? AppArchitecture { get; set; }
        public string? OcxVersion { get; set; }
        public string? ErrorMessage { get; set; }

        public bool IsPassed => IsStaThread && ClsidRegistered && MstscExists && CanCreateInstance;

        public override string ToString()
        {
            return $"STA: {IsStaThread}, CLSID: {ClsidRegistered}, MSTSC: {MstscExists}, Instance: {CanCreateInstance}, Port: {PortReachable}, OS: {OSArchitecture}, App: {AppArchitecture}, OCX: {OcxVersion}";
        }
    }

    public static class RdpDiagnostics
    {
        public static RdpDiagnosticResult Run(string clsid) =>
            Run(clsid, RdpActiveXVersions.TryGetLabel(clsid));

        public static RdpDiagnosticResult Run(string? clsid, string? activeXVersion)
        {
            var result = new RdpDiagnosticResult
            {
                SelectedActiveXVersion = activeXVersion,
                SelectedClsid = clsid,
            };
            try
            {
                // 1. STA Check
                result.IsStaThread = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;

                // 2. OS/App Architecture
                result.OSArchitecture = RuntimeInformation.OSArchitecture.ToString();
                result.AppArchitecture = RuntimeInformation.ProcessArchitecture.ToString();

                // 3. Registry Check
                if (string.IsNullOrWhiteSpace(clsid))
                {
                    result.OcxVersion = activeXVersion ?? "Henüz seçilmedi";
                    result.ErrorMessage = "Aktif RDP bileşeni henüz oluşturulmadı.";
                    return result;
                }

                result.ClsidRegistered = CheckClsidInRegistry(clsid);

                // 4. MSTSC Check
                string mstscPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
                result.MstscExists = File.Exists(mstscPath);

                // 5. COM Activation Test
                result.CanCreateInstance = TestComActivation(clsid, activeXVersion, out string version, out string? error);
                result.OcxVersion = version;
                result.ErrorMessage = error;

                return result;
            }
            catch (Exception ex)
            {
                AppLogger.Error("RDP ActiveX diagnostic failed.", ex);
                result.ErrorMessage = "RDP bileşeni tanısı tamamlanamadı.";
                return result;
            }
        }

        public static bool CheckPort(string host, int port = 3389, int timeoutMs = 2000)
        {
            // Kept for non-async callers; internally delegates to the async version.
            try
            {
                return CheckPortAsync(host, port, timeoutMs).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Port check failed for {host}:{port}.", ex);
                return false;
            }
        }

        public static async Task<bool> CheckPortAsync(string host, int port = 3389, int timeoutMs = 2000)
        {
            // CRITICAL FIX: Previous implementation used AsyncWaitHandle.WaitOne which still
            // blocks the calling thread for up to timeoutMs (1500ms). This froze the UI.
            // Using true async TcpClient.ConnectAsync with CancellationToken avoids any blocking.
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                using var client = new System.Net.Sockets.TcpClient();
                await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Async port check failed for {host}:{port}.", ex);
                return false;
            }
        }

        private static bool CheckClsidInRegistry(string clsid)
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
                return key != null;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"CLSID registry check failed for {clsid}.", ex);
                return false;
            }
        }

        private static bool TestComActivation(string clsid, string? activeXVersion, out string version, out string? error)
        {
            version = activeXVersion ?? "Bilinmiyor";
            error = null;
            object? instance = null;
            try
            {
                Type? type = Type.GetTypeFromCLSID(new Guid(clsid));
                if (type == null)
                {
                    error = "RDP ActiveX bileşeni bulunamadı.";
                    return false;
                }

                instance = Activator.CreateInstance(type);
                if (instance == null)
                {
                    error = "RDP ActiveX bileşeni başlatılamadı.";
                    return false;
                }

                // Try to get version via dynamic
                try
                {
                    dynamic client = instance;
                    version = client.Version;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"RDP ActiveX version read failed type={ex.GetType().FullName} hresult=0x{unchecked((uint)ex.HResult):X8}.");
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(
                    $"RDP ActiveX activation diagnostic failed version={activeXVersion ?? "unknown"} "
                    + $"exception={ex.GetType().FullName ?? ex.GetType().Name} "
                    + $"hresult=0x{unchecked((uint)ex.HResult):X8}.");
                error = "RDP ActiveX bileşeni doğrulanamadı.";
                return false;
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance))
                {
                    try { Marshal.FinalReleaseComObject(instance); }
                    catch (Exception ex) { AppLogger.Error("RDP diagnostic COM cleanup failed.", ex); }
                }
            }
        }
    }
}
