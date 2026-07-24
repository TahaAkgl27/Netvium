using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class WorkflowCatalog
    {
        public static IReadOnlyList<DetectedWorkflow> All { get; } = BuildCatalog();

        private static List<DetectedWorkflow> BuildCatalog()
        {
            return new List<DetectedWorkflow>
            {
                AdWorkflow(
                    "ad-password-reset",
                    "Reset Active Directory User Password",
                    "Browse Users Container",
                    new[] { "users container", "active directory users and computers", "console root" },
                    "User Selected",
                    new[] { "user selected", "properties", "user object", "cn=" },
                    "User Properties Open",
                    new[] { "properties", "account tab", "member of", "user properties" },
                    "Reset Password Dialog",
                    new[] { "reset password", "password reset", "set password", "new password" }),

                AdWorkflow(
                    "ad-user-create",
                    "Create Active Directory User",
                    "AD Console Open",
                    new[] { "active directory users and computers", "dsa.msc" },
                    "New User Action",
                    new[] { "new user", "new object", "create user" },
                    "User Details Entry",
                    new[] { "new object - user", "first name", "user logon name", "full name" },
                    "User Creation Complete",
                    new[] { "user created", "finish", "completed successfully" }),

                AdWorkflow(
                    "ad-user-unlock",
                    "Unlock Active Directory User Account",
                    "Browse Users Container",
                    new[] { "users container", "active directory users and computers" },
                    "User Selected",
                    new[] { "user selected", "locked out", "account is locked" },
                    "Unlock Account Action",
                    new[] { "unlock account", "account options", "account is locked out" },
                    "Unlock Confirmation",
                    new[] { "unlock", "account unlocked", "account options" }),

                AdWorkflow(
                    "ad-group-membership",
                    "Manage Active Directory Group Membership",
                    "Group or User Selected",
                    new[] { "group", "member of", "users container", "groups container" },
                    "Properties Open",
                    new[] { "properties", "member of", "members tab" },
                    "Add or Remove Members",
                    new[] { "add", "remove", "select users", "group membership" },
                    "Membership Updated",
                    new[] { "members", "member of", "apply", "ok" }),

                DnsWorkflow(
                    "dns-create-a-record",
                    "Create DNS A Record",
                    "DNS Manager Open",
                    new[] { "dns manager", "dnsmgmt", "forward lookup zones" },
                    "Zone Selected",
                    new[] { "forward lookup", "zone", "dns zone" },
                    "New Host Record",
                    new[] { "new host", "a record", "host record", "new resource record" },
                    "Record Properties",
                    new[] { "host", "ip address", "name", "a record" }),

                DnsWorkflow(
                    "dns-reverse-lookup",
                    "Configure Reverse Lookup Zone",
                    "DNS Manager Open",
                    new[] { "dns manager", "reverse lookup zones" },
                    "Reverse Zone Selected",
                    new[] { "reverse lookup", "in-addr.arpa", "reverse zone" },
                    "New Pointer Record",
                    new[] { "pointer", "ptr", "new resource record" },
                    "Record Created",
                    new[] { "ptr record", "pointer", "reverse lookup" }),

                DnsWorkflow(
                    "dns-conditional-forwarder",
                    "Configure Conditional Forwarder",
                    "DNS Manager Open",
                    new[] { "dns manager", "conditional forwarders" },
                    "Forwarder Settings",
                    new[] { "conditional forwarder", "new conditional forwarder", "forwarder" },
                    "Forwarder Domain Entry",
                    new[] { "dns domain", "ip addresses", "forwarder ip" },
                    "Forwarder Saved",
                    new[] { "conditional forwarder", "apply", "ok" }),

                SimpleWorkflow(
                    "hyper-v-create-vm",
                    "Create Hyper-V Virtual Machine",
                    "Hyper-V",
                    new[] { "hyper-v manager", "virtmgmt" },
                    "New Virtual Machine Wizard",
                    new[] { "new virtual machine", "new vm", "before you begin" },
                    "Specify Name and Location",
                    new[] { "name", "store the virtual machine", "generation" },
                    "Configure Memory and Storage",
                    new[] { "memory", "virtual hard disk", "vhd", "virtual switch", "network" },
                    "Complete VM Creation",
                    new[] { "installation options", "install an operating system", "finish", "complete" }),

                SimpleWorkflow(
                    "hyper-v-start-vm",
                    "Start Hyper-V Virtual Machine",
                    "Hyper-V",
                    new[] { "hyper-v manager" },
                    "VM Selected",
                    new[] { "virtual machines", "vm selected", "state" },
                    "Start Action Visible",
                    new[] { "start", "turn on", "running", "off" },
                    "VM Starting",
                    new[] { "starting", "running", "start" },
                    "VM Running",
                    new[] { "running", "started", "state running" }),

                SimpleWorkflow(
                    "hyper-v-checkpoint",
                    "Create Hyper-V Checkpoint",
                    "Hyper-V",
                    new[] { "hyper-v manager" },
                    "VM Selected",
                    new[] { "virtual machine", "checkpoints" },
                    "Checkpoint Action",
                    new[] { "checkpoint", "snapshot", "create checkpoint" },
                    "Checkpoint Created",
                    new[] { "checkpoint", "snapshot", "created" },
                    "Checkpoint Saved",
                    new[] { "checkpoint", "snapshot", "apply", "ok" }),

                SimpleWorkflow(
                    "iis-restart-app-pool",
                    "Restart IIS Application Pool",
                    "IIS",
                    new[] { "internet information services", "iis manager", "inetmgr" },
                    "Application Pools View",
                    new[] { "application pools", "app pools" },
                    "Pool Selected",
                    new[] { "application pool", "recycle", "stop", "start" },
                    "Restart Action",
                    new[] { "recycle", "restart", "start", "stopped" },
                    "Pool Restarted",
                    new[] { "started", "running", "recycle", "success" }),

                SimpleWorkflow(
                    "iis-configure-binding",
                    "Configure IIS Site Binding",
                    "IIS",
                    new[] { "iis manager", "internet information services" },
                    "Site Selected",
                    new[] { "sites", "default web site", "bindings" },
                    "Edit Bindings",
                    new[] { "bindings", "edit site binding", "add site binding" },
                    "Binding Properties",
                    new[] { "binding", "port", "host name", "ip address", "https" },
                    "Binding Saved",
                    new[] { "binding", "apply", "ok", "close" }),

                SimpleWorkflow(
                    "iis-start-website",
                    "Start IIS Website",
                    "IIS",
                    new[] { "iis manager" },
                    "Sites View",
                    new[] { "sites", "web site" },
                    "Site Selected",
                    new[] { "start", "stopped", "website" },
                    "Site Started",
                    new[] { "started", "start", "running" },
                    "Site Running",
                    new[] { "started", "running", "online" }),

                SimpleWorkflow(
                    "event-investigation",
                    "Event Viewer Investigation",
                    "Event Viewer",
                    new[] { "event viewer", "eventvwr" },
                    "Log Selected",
                    new[] { "application", "system", "security", "windows logs" },
                    "Event Selected",
                    new[] { "event id", "event properties", "general", "details" },
                    "Event Details Review",
                    new[] { "event id", "source", "level", "error", "warning", "critical" },
                    "Analysis Complete",
                    new[] { "details", "description", "general", "event id" }),

                SimpleWorkflow(
                    "service-restart",
                    "Restart Windows Service",
                    "Services",
                    new[] { "services", "services.msc" },
                    "Service Selected",
                    new[] { "service selected", "local services" },
                    "Service Status Review",
                    new[] { "status", "running", "stopped", "start", "restart" },
                    "Restart Action Visible",
                    new[] { "restart", "stop", "start", "running" },
                    "Service Restarted",
                    new[] { "running", "started", "restart", "success" }),

                SimpleWorkflow(
                    "service-startup-type",
                    "Change Service Startup Type",
                    "Services",
                    new[] { "services", "services.msc" },
                    "Service Selected",
                    new[] { "service selected" },
                    "Properties Open",
                    new[] { "properties", "startup type", "general tab" },
                    "Startup Type Changed",
                    new[] { "startup type", "automatic", "manual", "disabled" },
                    "Change Applied",
                    new[] { "apply", "ok", "startup type" }),

                SimpleWorkflow(
                    "task-troubleshoot",
                    "Task Scheduler Troubleshooting",
                    "Task Scheduler",
                    new[] { "task scheduler", "taskschd" },
                    "Task Selected",
                    new[] { "task selected", "scheduled task" },
                    "History Review",
                    new[] { "history", "last run result", "task history" },
                    "Failure Analysis",
                    new[] { "failed", "error", "last run result", "triggers", "actions" },
                    "Resolution Review",
                    new[] { "history", "success", "last run result", "ready" })
            };
        }

        private static DetectedWorkflow AdWorkflow(
            string id,
            string name,
            string step1Name, string[] step1Patterns,
            string step2Name, string[] step2Patterns,
            string step3Name, string[] step3Patterns,
            string step4Name, string[] step4Patterns) =>
            Build(id, name, "Active Directory",
                new[] { "active directory", "dsa.msc", "users and computers", "ad users" },
                step1Name, step1Patterns,
                step2Name, step2Patterns,
                step3Name, step3Patterns,
                step4Name, step4Patterns);

        private static DetectedWorkflow DnsWorkflow(
            string id,
            string name,
            string step1Name, string[] step1Patterns,
            string step2Name, string[] step2Patterns,
            string step3Name, string[] step3Patterns,
            string step4Name, string[] step4Patterns) =>
            Build(id, name, "DNS",
                new[] { "dns manager", "dnsmgmt", "dns management" },
                step1Name, step1Patterns,
                step2Name, step2Patterns,
                step3Name, step3Patterns,
                step4Name, step4Patterns);

        private static DetectedWorkflow SimpleWorkflow(
            string id,
            string name,
            string category,
            string[] appPatterns,
            string step1Name, string[] step1Patterns,
            string step2Name, string[] step2Patterns,
            string step3Name, string[] step3Patterns,
            string step4Name, string[] step4Patterns) =>
            Build(id, name, category, appPatterns,
                step1Name, step1Patterns,
                step2Name, step2Patterns,
                step3Name, step3Patterns,
                step4Name, step4Patterns);

        private static DetectedWorkflow Build(
            string id,
            string name,
            string category,
            string[] appPatterns,
            string step1Name, string[] step1Patterns,
            string step2Name, string[] step2Patterns,
            string step3Name, string[] step3Patterns,
            string step4Name, string[] step4Patterns)
        {
            var steps = new List<WorkflowStep>
            {
                Step("1", step1Name, step1Patterns, step2Name),
                Step("2", step2Name, step2Patterns, step3Name),
                Step("3", step3Name, step3Patterns, step4Name),
                Step("4", step4Name, step4Patterns, null)
            };

            return new DetectedWorkflow
            {
                Id = id,
                Name = name,
                Category = category,
                ApplicationPatterns = appPatterns.ToList(),
                Steps = steps
            };
        }

        private static WorkflowStep Step(string id, string name, string[] patterns, string? nextStepName) =>
            new()
            {
                Id = id,
                Name = name,
                EvidencePatterns = patterns.ToList(),
                NextLikelyStepHint = nextStepName == null
                    ? null
                    : $"The next manual step is typically related to: {nextStepName}."
            };
    }
}
