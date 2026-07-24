namespace Netvium.Rdp
{
    /// <summary>
    /// MsRdpClient NotSafeForScripting CLSIDs — same COM classes as AxMSTSCLib, hosted via AxHost on STA UI thread.
    /// </summary>
    public static class RdpActiveXVersions
    {
        public readonly record struct RdpActiveXVersion(string Label, string Clsid);
        public static readonly RdpActiveXVersion[] CleanHostOrder =
        {
            new("MsRdpClient11", "A0C63C30-F08D-4AB4-907C-34905D770C7D"),
            new("MsRdpClient10", "7390F3D8-7719-4416-A154-66753B070F87"),
            new("MsRdpClient9", "8B918B82-7985-4C24-89DF-C33AD2BBFBCD"),
        };


        public static readonly RdpActiveXVersion[] FallbackOrder =
        {
            new("MsRdpClient11NotSafeForScripting", "A0C63C30-F08D-4AB4-907C-34905D770C7D"),
            new("MsRdpClient10NotSafeForScripting", "7390F3D8-7719-4416-A154-66753B070F87"),
            new("MsRdpClient9NotSafeForScripting", "8B918B82-7985-4C24-89DF-C33AD2BBFBCD"),
            new("MsRdpClient8NotSafeForScripting", "509759F7-709E-4632-8170-54217639937D"),
            new("MsRdpClient7NotSafeForScripting", "6994FC00-0336-4B75-8744-C709D619147E"),
        };

        public static string? TryGetLabel(string clsid)
        {
            // The clean host is the active RDP engine. Prefer its labels so
            // diagnostics describe the actual AxMsRdpClient type in use;
            // retain legacy labels only for the unused manager's v7/v8 CLSIDs.
            foreach (var v in CleanHostOrder)
            {
                if (string.Equals(v.Clsid, clsid, StringComparison.OrdinalIgnoreCase))
                    return v.Label;
            }

            foreach (var v in FallbackOrder)
            {
                if (string.Equals(v.Clsid, clsid, StringComparison.OrdinalIgnoreCase))
                    return v.Label;
            }

            return null;
        }

        public static bool IsRegistered(string clsid)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
                return key != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
