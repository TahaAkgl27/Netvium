using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Netvium.Helpers
{
    public static class WindowsCredentialHelper
    {
        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;

        public static void Save(string ip, int port, string username, string password)
        {
            SaveTarget(BuildTarget(ip, port), username, password, "Netvium stored credential");
        }

        public static void SaveTermsrv(string server, string username, string password)
        {
            var host = server.Split(':')[0].Trim();
            if (string.IsNullOrWhiteSpace(host))
                return;

            SaveTarget($"TERMSRV/{host}", username, password, "Netvium RDP credential");
        }

        private static void SaveTarget(string target, string username, string password, string comment)
        {
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = username,
                CredentialBlob = Marshal.StringToCoTaskMemUni(password),
                CredentialBlobSize = (uint)(password.Length * 2),
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = comment
            };

            try
            {
                CredWrite(ref credential, 0);
            }
            finally
            {
                Marshal.FreeCoTaskMem(credential.CredentialBlob);
            }
        }

        public static (string Username, string Password)? Load(string ip, int port)
        {
            var target = BuildTarget(ip, port);
            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr) || credPtr == IntPtr.Zero)
                return null;

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                var password = cred.CredentialBlobSize > 0
                    ? Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2) ?? string.Empty
                    : string.Empty;
                return (cred.UserName ?? string.Empty, password);
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        private static string BuildTarget(string ip, int port)
        {
            var p = port <= 0 ? 3389 : port;
            return $"Netvium:{ip}:{p}";
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([In] ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr cred);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }
    }
}
