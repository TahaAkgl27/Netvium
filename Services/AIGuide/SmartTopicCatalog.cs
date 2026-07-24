using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Application-specific suggested topics based on detected application name.
    /// Topics are questions only — never executable actions.
    /// </summary>
    internal static class SmartTopicCatalog
    {
        private static readonly (string[] Patterns, SuggestedTopic[] Topics)[] Catalog =
        {
            (
                new[] { "active directory users and computers", "dsa.msc", "users and computers" },
                new[]
                {
                    new SuggestedTopic { Label = "Reset Password", Question = "How do I reset a user password manually on this screen?" },
                    new SuggestedTopic { Label = "Unlock Account", Question = "How do I unlock a locked user account from what is visible?" },
                    new SuggestedTopic { Label = "Create User", Question = "How do I create a new user using the visible options?" },
                    new SuggestedTopic { Label = "Groups", Question = "How do I manage group membership from this screen?" }
                }
            ),
            (
                new[] { "dns manager", "dnsmgmt.msc", "dns management" },
                new[]
                {
                    new SuggestedTopic { Label = "A Record", Question = "How do I create an A record from what is visible?" },
                    new SuggestedTopic { Label = "DNS Zone", Question = "How do I check this DNS zone from the visible screen?" },
                    new SuggestedTopic { Label = "Reverse Lookup", Question = "How do I work with a reverse lookup zone on this screen?" },
                    new SuggestedTopic { Label = "Name Resolution", Question = "How do I troubleshoot name resolution using what is visible?" }
                }
            ),
            (
                new[] { "dhcp", "dhcpmgmt.msc" },
                new[]
                {
                    new SuggestedTopic { Label = "Scopes", Question = "How do I check DHCP scopes from what is visible?" },
                    new SuggestedTopic { Label = "Reservation", Question = "How do I create a DHCP reservation manually?" },
                    new SuggestedTopic { Label = "Leases", Question = "How do I review DHCP leases on this screen?" },
                    new SuggestedTopic { Label = "Scope Options", Question = "How do I review scope options from what is visible?" }
                }
            ),
            (
                new[] { "hyper-v manager", "virtmgmt.msc", "hyper-v" },
                new[]
                {
                    new SuggestedTopic { Label = "VM State", Question = "How do I check the VM state from what is visible?" },
                    new SuggestedTopic { Label = "Start/Stop VM", Question = "How do I start or stop a VM manually from this screen?" },
                    new SuggestedTopic { Label = "Virtual Switch", Question = "How do I configure a virtual switch from what is visible?" },
                    new SuggestedTopic { Label = "Checkpoints", Question = "How do I review VM checkpoints on this screen?" }
                }
            ),
            (
                new[] { "internet information services", "iis manager", "inetmgr" },
                new[]
                {
                    new SuggestedTopic { Label = "Site Status", Question = "How do I check IIS site status from what is visible?" },
                    new SuggestedTopic { Label = "App Pool", Question = "How do I restart an application pool manually from this screen?" },
                    new SuggestedTopic { Label = "Bindings", Question = "How do I review site bindings from what is visible?" },
                    new SuggestedTopic { Label = "HTTP Errors", Question = "How do I troubleshoot HTTP errors using what is visible?" }
                }
            ),
            (
                new[] { "event viewer", "eventvwr" },
                new[]
                {
                    new SuggestedTopic { Label = "Event ID", Question = "What does the visible Event ID mean on this screen?" },
                    new SuggestedTopic { Label = "Critical Errors", Question = "How do I filter critical errors from what is visible?" },
                    new SuggestedTopic { Label = "Root Cause", Question = "What visible evidence helps find the root cause here?" },
                    new SuggestedTopic { Label = "Service Crash", Question = "How do I analyze a service crash from this Event Viewer screen?" }
                }
            ),
            (
                new[] { "services", "services.msc" },
                new[]
                {
                    new SuggestedTopic { Label = "Service Status", Question = "How do I check the visible service status?" },
                    new SuggestedTopic { Label = "Restart Service", Question = "How do I restart a service manually from this screen?" },
                    new SuggestedTopic { Label = "Startup Type", Question = "How do I identify the startup type from what is visible?" },
                    new SuggestedTopic { Label = "Stopped Service", Question = "How do I troubleshoot a stopped service from this screen?" }
                }
            ),
            (
                new[] { "task scheduler", "taskschd.msc" },
                new[]
                {
                    new SuggestedTopic { Label = "Task History", Question = "How do I check task history from what is visible?" },
                    new SuggestedTopic { Label = "Run Task", Question = "How do I run a scheduled task manually from this screen?" },
                    new SuggestedTopic { Label = "Triggers", Question = "How do I review task triggers from what is visible?" },
                    new SuggestedTopic { Label = "Failed Task", Question = "How do I investigate a failed task on this screen?" }
                }
            ),
            (
                new[] { "registry editor", "regedit" },
                new[]
                {
                    new SuggestedTopic { Label = "Explain Key", Question = "What does the visible registry key mean?" },
                    new SuggestedTopic { Label = "Value Meaning", Question = "What does the visible registry value mean?" },
                    new SuggestedTopic { Label = "Edit Warning", Question = "What should I know before editing this registry key manually?" }
                }
            ),
            (
                new[] { "powershell", "windows powershell" },
                new[]
                {
                    new SuggestedTopic { Label = "Explain Output", Question = "What does the visible PowerShell output mean?" },
                    new SuggestedTopic { Label = "Error Message", Question = "How do I interpret the visible error message?" },
                    new SuggestedTopic { Label = "Next Step", Question = "What is a safe manual next step based on this screen?" }
                }
            ),
            (
                new[] { "cmd", "command prompt" },
                new[]
                {
                    new SuggestedTopic { Label = "Explain Output", Question = "What does the visible command output mean?" },
                    new SuggestedTopic { Label = "Error Message", Question = "How do I interpret the visible error message?" },
                    new SuggestedTopic { Label = "Next Step", Question = "What is a safe manual next step based on this screen?" }
                }
            )
        };

        public static IReadOnlyList<SuggestedTopic> Resolve(string? application, string? windowTitle, bool lowConfidence) =>
            AIGuideLocalizedCatalogs.ResolveSmartTopics(application, windowTitle, lowConfidence);
    }
}
