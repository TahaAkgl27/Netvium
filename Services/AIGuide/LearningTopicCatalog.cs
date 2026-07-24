using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class LearningTopicCatalog
    {
        private static readonly (string[] Patterns, LearningTopicGroup Group)[] Catalog =
        [
            (
                new[] { "active directory", "users and computers", "dsa.msc" },
                Group("Active Directory", "Active Directory", "Organizational Units", "FSMO Roles", "Password Policies", "Group Policy")
            ),
            (
                new[] { "dns manager", "dnsmgmt", "dns" },
                Group("DNS", "DNS Forward Lookup", "Reverse Lookup Zones", "Conditional Forwarders", "DNS Records")
            ),
            (
                new[] { "dhcp", "dhcpmgmt" },
                Group("DHCP", "DHCP Scopes", "Reservations", "Lease Duration", "Scope Options")
            ),
            (
                new[] { "hyper-v", "virtmgmt" },
                Group("Hyper-V", "Hyper-V Checkpoints", "Virtual Switches", "Generation 1 vs 2 VMs", "Integration Services")
            ),
            (
                new[] { "iis", "inetmgr", "internet information services" },
                Group("IIS", "Application Pools", "Site Bindings", "Worker Processes", "HTTP Errors")
            ),
            (
                new[] { "event viewer", "eventvwr" },
                Group("Event Viewer", "Event IDs", "Critical Events", "Log Filtering", "Event Sources")
            ),
            (
                new[] { "services", "services.msc" },
                Group("Services", "Service Recovery", "Startup Types", "Service Dependencies", "Windows Services")
            ),
            (
                new[] { "registry editor", "regedit" },
                Group("Registry", "Registry Basics", "Registry Hives", "Registry Values", "Backup Before Edits")
            ),
            (
                new[] { "task scheduler", "taskschd" },
                Group("Task Scheduler", "Triggers", "Task Actions", "Task History", "Scheduled Tasks")
            ),
            (
                new[] { "group policy", "gpmc" },
                Group("Group Policy", "Group Policy", "GPO Inheritance", "Policy Refresh", "Security Filtering")
            )
        ];

        public static LearningSuggestion Resolve(string? application, string? windowTitle, string? role)
        {
            var haystack = $"{application} {windowTitle} {role}".ToLowerInvariant();
            var groups = new List<LearningTopicGroup>();
            var all = new List<LearningTopic>();

            foreach (var (patterns, group) in Catalog)
            {
                if (!patterns.Any(p => haystack.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                groups.Add(group);
                all.AddRange(group.Topics);
            }

            if (all.Count == 0)
            {
                var fallback = Group("Windows Server", "Windows Server Basics", "Remote Desktop", "Server Manager", "Administrative Tools");
                groups.Add(fallback);
                all.AddRange(fallback.Topics);
            }

            return new LearningSuggestion
            {
                Groups = groups,
                AllTopics = all.DistinctBy(t => t.Label).Take(8).ToList()
            };
        }

        private static LearningTopicGroup Group(string name, params string[] topics) =>
            new()
            {
                Name = name,
                Topics = topics.Select(t => new LearningTopic
                {
                    Label = t,
                    TeachPrompt = AIGuideLocalizedCatalogs.TeachAbout(t),
                    Group = name
                }).ToList()
            };
    }
}
