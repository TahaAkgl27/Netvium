using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ScreenComparisonService
    {
        public static ScreenComparisonResult Compare(
            VisionObservation? previous,
            VisionObservation current)
        {
            var previousApp = NormalizeApplication(previous);
            var currentApp = NormalizeApplication(current);

            var applicationChanged = !string.IsNullOrWhiteSpace(previousApp)
                                     && !string.IsNullOrWhiteSpace(currentApp)
                                     && !AreRelatedApplications(previousApp, currentApp);

            var sameApplicationFamily = AreRelatedApplications(previousApp, currentApp);

            return new ScreenComparisonResult
            {
                PreviousApplication = previousApp,
                CurrentApplication = currentApp,
                ApplicationChanged = applicationChanged,
                SameApplicationFamily = sameApplicationFamily,
                WindowTitleChanged = !string.Equals(
                    previous?.WindowTitle?.Trim(),
                    current.WindowTitle?.Trim(),
                    StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string? NormalizeApplication(VisionObservation? observation)
        {
            if (observation == null)
                return null;

            if (!string.IsNullOrWhiteSpace(observation.Application))
                return observation.Application.Trim();

            if (!string.IsNullOrWhiteSpace(observation.WindowTitle))
                return observation.WindowTitle.Trim();

            return observation.DetectedSoftware.FirstOrDefault()?.Trim();
        }

        private static bool AreRelatedApplications(string? previous, string? current)
        {
            if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
                return true;

            var a = previous.ToLowerInvariant();
            var b = current.ToLowerInvariant();

            if (a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
                return true;

            foreach (var family in ApplicationFamilies)
            {
                var prevMatch = family.Any(p => a.Contains(p, StringComparison.Ordinal));
                var currMatch = family.Any(p => b.Contains(p, StringComparison.Ordinal));
                if (prevMatch && currMatch)
                    return true;
            }

            return false;
        }

        private static readonly string[][] ApplicationFamilies =
        {
            new[] { "active directory", "dsa.msc", "users and computers" },
            new[] { "dns manager", "dnsmgmt", "dns management" },
            new[] { "dhcp", "dhcpmgmt" },
            new[] { "hyper-v", "virtmgmt" },
            new[] { "internet information services", "iis manager", "inetmgr" },
            new[] { "event viewer", "eventvwr" },
            new[] { "services.msc", "services" },
            new[] { "task scheduler", "taskschd" },
            new[] { "registry editor", "regedit" },
            new[] { "powershell", "cmd", "command prompt" }
        };
    }

    internal sealed class ScreenComparisonResult
    {
        public string? PreviousApplication { get; init; }
        public string? CurrentApplication { get; init; }
        public bool ApplicationChanged { get; init; }
        public bool SameApplicationFamily { get; init; }
        public bool WindowTitleChanged { get; init; }
    }
}
