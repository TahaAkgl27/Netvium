using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Netvium.Agent;

internal static class AgentSecretStore
{
    private const int CryptProtectLocalMachine = 0x4;
    private static readonly byte[] ServiceEntropy = Encoding.UTF8.GetBytes("Netvium.Agent.ServiceKey.v1");
    private static readonly byte[] UserEntropy = Encoding.UTF8.GetBytes("Netvium.Agent.UserKey.v1");

    public static string DefaultServiceKeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Netvium", "Agent", "agent.key.dpapi");

    public static string DefaultUserKeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Netvium", "Agent", "agent.key.dpapi");

    public static bool TryLoadProtectedKey(string path, out string key) =>
        TryLoad(path, localMachine: true, out key);

    public static bool TryLoadCurrentUserProtectedKey(string path, out string key) =>
        TryLoad(path, localMachine: false, out key);

    internal static bool TryProtectKeyForProvisioning(string key, string path) =>
        TryProtectForProvisioning(key, path, localMachine: true);

    internal static bool TryProtectCurrentUserKeyForProvisioning(string key, string path) =>
        TryProtectForProvisioning(key, path, localMachine: false);

    private static bool TryLoad(string path, bool localMachine, out string key)
    {
        key = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var protectedBytes = Convert.FromBase64String(File.ReadAllText(path, Encoding.ASCII).Trim());
            if (!TryUnprotect(protectedBytes, localMachine, out var plainBytes))
                return false;

            key = Encoding.UTF8.GetString(plainBytes);
            CryptographicOperations.ZeroMemory(plainBytes);
            return !string.IsNullOrWhiteSpace(key);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryProtectForProvisioning(string key, string path, bool localMachine)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
            return false;

        byte[]? plain = null;
        byte[]? protectedBytes = null;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return false;
            Directory.CreateDirectory(directory);
            plain = Encoding.UTF8.GetBytes(key);
            if (!TryProtect(plain, localMachine, out protectedBytes))
                return false;
            File.WriteAllText(path, Convert.ToBase64String(protectedBytes), Encoding.ASCII);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
            if (protectedBytes != null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static bool TryProtect(byte[] data, bool localMachine, out byte[] protectedBytes)
    {
        protectedBytes = Array.Empty<byte>();
        var input = new DATA_BLOB(data);
        var entropy = new DATA_BLOB(localMachine ? ServiceEntropy : UserEntropy);
        try
        {
            var flags = localMachine ? CryptProtectLocalMachine : 0;
            if (!CryptProtectData(ref input, "Netvium Agent protected key", ref entropy, IntPtr.Zero, IntPtr.Zero, flags, out var output))
                return false;
            protectedBytes = output.ToArrayAndFree();
            return protectedBytes.Length > 0;
        }
        finally
        {
            input.Free();
            entropy.Free();
        }
    }

    private static bool TryUnprotect(byte[] data, bool localMachine, out byte[] plainBytes)
    {
        plainBytes = Array.Empty<byte>();
        var input = new DATA_BLOB(data);
        var entropy = new DATA_BLOB(localMachine ? ServiceEntropy : UserEntropy);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                return false;
            plainBytes = output.ToArrayAndFree();
            return plainBytes.Length > 0;
        }
        finally
        {
            input.Free();
            entropy.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;

        public DATA_BLOB(byte[] bytes)
        {
            cbData = bytes.Length;
            pbData = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pbData, bytes.Length);
        }

        public byte[] ToArrayAndFree()
        {
            if (pbData == IntPtr.Zero || cbData <= 0)
            {
                Free();
                return Array.Empty<byte>();
            }
            var bytes = new byte[cbData];
            Marshal.Copy(pbData, bytes, 0, cbData);
            Free();
            return bytes;
        }

        public void Free()
        {
            if (pbData == IntPtr.Zero)
                return;
            if (cbData > 0)
            {
                var zeroes = new byte[cbData];
                Marshal.Copy(zeroes, 0, pbData, cbData);
            }
            Marshal.FreeHGlobal(pbData);
            pbData = IntPtr.Zero;
            cbData = 0;
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string description, ref DATA_BLOB entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, IntPtr description, ref DATA_BLOB entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB pDataOut);
}