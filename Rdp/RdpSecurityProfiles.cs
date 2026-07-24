using System.Collections.Generic;

namespace Netvium.Rdp
{
    public static class RdpSecurityProfiles
    {
        public static IReadOnlyList<RdpSecurityProfile> DefaultRotation { get; } = new List<RdpSecurityProfile>
        {
            RdpSecurityProfile.StrictNLA,
            RdpSecurityProfile.RelaxedNLA,
            RdpSecurityProfile.Legacy
        };
    }
}
