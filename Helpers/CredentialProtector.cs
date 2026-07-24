using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Netvium.Helpers
{
    public static class CredentialProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Netvium.CredentialVault.v1");

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            var data = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static bool TryUnprotect(string protectedText, out string plainText)
        {
            plainText = string.Empty;
            if (string.IsNullOrEmpty(protectedText))
                return true;

            try
            {
                var protectedBytes = Convert.FromBase64String(protectedText);
                var data = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                plainText = Encoding.UTF8.GetString(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string Unprotect(string protectedText)
        {
            return TryUnprotect(protectedText, out var plain) ? plain : string.Empty;
        }

        public static void SecureDeleteFile(string path)
        {
            if (!File.Exists(path))
                return;

            var length = new FileInfo(path).Length;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[4096];
                var rng = RandomNumberGenerator.Create();
                for (int pass = 0; pass < 3; pass++)
                {
                    stream.Position = 0;
                    long remaining = length;
                    while (remaining > 0)
                    {
                        rng.GetBytes(buffer);
                        int write = (int)Math.Min(buffer.Length, remaining);
                        stream.Write(buffer, 0, write);
                        remaining -= write;
                    }
                    stream.Flush();
                }
            }

            File.Delete(path);
        }
    }
}
