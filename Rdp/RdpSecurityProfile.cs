using System;

namespace Netvium.Rdp
{
    public enum SecurityLayer
    {
        Auto = 0,
        Rdp = 1,
        Ssl = 2,
        Negotiate = 3
    }

    public class RdpSecurityProfile
    {
        public string Name { get; set; } = "StrictNLA";
        public bool EnableCredSsp { get; set; } = true;
        public int AuthenticationLevel { get; set; } = 2; // 0: None, 1: Warn, 2: Require (NLA)
        public bool NegotiateSecurityLayer { get; set; } = true;
        
        public static RdpSecurityProfile StrictNLA => new RdpSecurityProfile
        {
            Name = "StrictNLA",
            EnableCredSsp = true,
            AuthenticationLevel = 2,
            // Embedded ActiveX: single authoritative value (also applied to ExtendedSettings).
            NegotiateSecurityLayer = false
        };

        public static RdpSecurityProfile RelaxedNLA => new RdpSecurityProfile
        {
            Name = "RelaxedNLA",
            EnableCredSsp = true,
            AuthenticationLevel = 1, // Relaxed (Warn but allow)
            NegotiateSecurityLayer = false
        };

        public static RdpSecurityProfile Legacy => new RdpSecurityProfile
        {
            Name = "Legacy",
            EnableCredSsp = false,
            AuthenticationLevel = 0,
            NegotiateSecurityLayer = false
        };
    }
}
