using System.Collections.Generic;
using System.Linq;
using Netvium.Models.Dashboard;

namespace Netvium.Helpers
{
    public static class DashboardWidgetRegistry
    {
        public const int ColumnCount = 12;
        public const int RowUnitHeight = 58;
        public const int ColumnGap = 12;
        public const int RowGap = 12;

        // Bump when the built-in default layout changes so older saved layouts
        // (created before this redesign) auto-migrate to the new default.
        public const int CurrentSchemaVersion = 3;

        private static readonly List<DashboardWidgetDefinition> Definitions = BuildDefinitions();

        public static IReadOnlyList<DashboardWidgetDefinition> All => Definitions;

        public static DashboardWidgetDefinition? Get(string type) =>
            Definitions.FirstOrDefault(d => d.Type.Equals(type, System.StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<string> Categories =>
            Definitions.Select(d => d.Category).Distinct().OrderBy(c => c);

        public static IEnumerable<DashboardWidgetDefinition> GetByCategory(string category) =>
            Definitions.Where(d => d.Category.Equals(category, System.StringComparison.OrdinalIgnoreCase));

        public static DashboardWidgetInstance CreateInstance(string type)
        {
            var def = Get(type) ?? throw new System.ArgumentException($"Unknown widget type: {type}");
            return new DashboardWidgetInstance
            {
                Type = def.Type,
                W = def.DefaultW,
                H = def.DefaultH,
                MinW = def.MinW,
                MinH = def.MinH,
                MaxW = def.MaxW,
                MaxH = def.MaxH,
                Settings = new DashboardWidgetSettings
                {
                    Title = def.Title,
                    Icon = def.Icon
                }
            };
        }

        public static DashboardLayoutDocument CreateDefaultDashboardLayout(string? serverKey = null)
        {
            var workspace = new DashboardWorkspace
            {
                Name = "Default",
                Widgets =
                {
                    // HERO ROW — Performance is the centerpiece (≈67%), AI Health beside it (≈33%)
                    Widget("performance", 0, 0, 8, 6),
                    Widget("ai", 8, 0, 4, 6),

                    // CONTEXT ROW — three equal cards: System Status | Server Information | Recent Events
                    Widget("health", 0, 6, 4, 4),
                    Widget("info", 4, 6, 4, 4),
                    Widget("events", 8, 6, 4, 4),

                    // METRIC ROW — six equal compact KPI cards
                    Widget("cpu", 0, 10, 2, 2),
                    Widget("ram", 2, 10, 2, 2),
                    Widget("disk", 4, 10, 2, 2),
                    Widget("network", 6, 10, 2, 2),
                    Widget("ip-address", 8, 10, 2, 2),
                    Widget("uptime", 10, 10, 2, 2)
                }
            };

            return new DashboardLayoutDocument
            {
                Version = CurrentSchemaVersion,
                ServerKey = serverKey,
                ActiveWorkspaceId = workspace.Id,
                Workspaces = { workspace }
            };
        }

        public static DashboardLayoutDocument CreateDefaultLayout() =>
            CreateDefaultDashboardLayout();

        /// <summary>
        /// Internal-only validation layout used by QA/runtime smoke tests. It is not surfaced
        /// in the end-user workspace picker or Add Widget flow.
        /// </summary>
        public static DashboardLayoutDocument CreateInternalQaDashboardLayout(string? serverKey = null)
        {
            var workspace = new DashboardWorkspace
            {
                Name = "Internal QA Validation",
                Widgets =
                {
                    Widget("performance", 0, 0, 8, 5),
                    Widget("ai", 8, 0, 4, 5),
                    Widget("health", 0, 5, 4, 4),
                    Widget("info", 4, 5, 4, 4),
                    Widget("events", 8, 5, 4, 4),
                    Widget("cpu", 0, 9, 2, 2),
                    Widget("ram", 2, 9, 2, 2),
                    Widget("disk", 4, 9, 2, 2),
                    Widget("network", 6, 9, 2, 2),
                    Widget("ip-address", 8, 9, 2, 2),
                    Widget("uptime", 10, 9, 2, 2),
                    Widget("latency", 0, 11, 2, 2),
                    Widget("gpu", 2, 11, 2, 2),
                    Widget("processes", 4, 11, 4, 3),
                    Widget("services", 8, 11, 4, 3),
                    Widget("sessions", 0, 14, 4, 3),
                    Widget("windows-updates", 4, 14, 4, 3),
                    Widget("firewall", 8, 14, 2, 3),
                    Widget("bitlocker", 10, 14, 2, 3),
                    Widget("windows-defender", 0, 17, 4, 3),
                    Widget("security-score", 4, 17, 4, 3),
                    Widget("open-ports", 8, 17, 4, 3),
                    Widget("agent-health", 0, 20, 3, 2)
                }
            };

            return new DashboardLayoutDocument
            {
                Version = CurrentSchemaVersion,
                ServerKey = serverKey,
                ActiveWorkspaceId = workspace.Id,
                Workspaces = { workspace }
            };
        }

        private static DashboardWidgetInstance Widget(string type, int x, int y, int w, int h)
        {
            var inst = CreateInstance(type);
            inst.X = x;
            inst.Y = y;
            inst.W = w;
            inst.H = h;
            if (h == 1)
                inst.MinH = 1;
            return inst;
        }

        private static DashboardWidgetInstance Widget(string type, int x, int y, int w, int h, int minH)
        {
            var inst = Widget(type, x, y, w, h);
            inst.MinH = minH;
            return inst;
        }

        private static List<DashboardWidgetDefinition> BuildDefinitions() => new()
        {
            Def("cpu", "CPU Usage", "System", "\uE950", "Live CPU utilization", 2, 2, minW: 2, minH: 2, panelKey: "cpu"),
            Def("ram", "RAM Usage", "System", "\uE964", "Live memory utilization", 2, 2, minW: 2, minH: 2, panelKey: "ram"),
            Def("disk", "Disk Usage", "System", "\uEEDA", "Live disk utilization", 2, 2, minW: 2, minH: 2, panelKey: "disk"),
            Def("network", "Network", "System", "\uE968", "Live network throughput", 2, 2, minW: 2, minH: 2, panelKey: "network"),
            Def("uptime", "Uptime", "Server", "\uE823", "Server uptime", 2, 2, minW: 2, minH: 2, panelKey: "uptime"),
            Def("ip-address", "IP Address", "Server", "\uE968", "Primary IP address", 2, 2, minW: 2, minH: 2, panelKey: "ip-address"),
            Def("latency", "Latency", "System", "\uE91F", "Ping latency", 2, 2, panelKey: "latency"),
            Def("gpu", "GPU", "System", "\uE7F4", "GPU metrics", 2, 2, panelKey: "gpu"),
            Def("health", "System Status", "System", "\uE72E", "Component health grid", 4, 5, panelKey: "health"),
            Def("performance", "Performance", "Charts", "\uE9D2", "CPU/RAM/Disk/Network chart", 6, 4, minW: 4, minH: 3, panelKey: "performance"),
            Def("services", "Active Services", "Services", "\uE90F", "Running services table", 3, 3, panelKey: "services"),
            Def("processes", "Processes", "Services", "\uE8F1", "Top processes", 3, 3, panelKey: "processes"),
            Def("windows-updates", "Windows Updates", "System", "\uE895", "Update status", 3, 2, panelKey: "windows-updates"),
            Def("firewall", "Firewall", "Security", "\uE83F", "Firewall status", 2, 2, panelKey: "firewall"),
            Def("bitlocker", "BitLocker", "Security", "\uE72E", "BitLocker status", 2, 2, panelKey: "bitlocker"),
            Def("sessions", "Sessions", "Users", "\uE77B", "Active sessions", 3, 3, panelKey: "sessions"),
            Def("events", "Recent Events", "Charts", "\uE823", "Event timeline", 3, 3, panelKey: "events"),
            Def("ai", "AI Smart Analysis", "AI", "\uE99A", "AI insights and recommendations", 6, 5, minW: 4, minH: 4, panelKey: "ai"),
            Def("agent-health", "Agent Health", "AI", "\uE95E", "Agent heartbeat", 2, 2, panelKey: "agent-health"),
            Def("info", "Server Information", "Server", "\uE8A5", "Server metadata", 4, 4, panelKey: "info"),
            Def("security-score", "Security Score", "Security", "\uE9D9", "Calculated security posture", 4, 3, panelKey: "security-score"),
            Def("open-ports", "Open Ports", "Security", "\uE968", "Listening ports and process risk", 4, 3, panelKey: "open-ports"),
            Def("windows-defender", "Windows Defender", "Security", "\uE83F", "Defender protection status", 4, 3, panelKey: "windows-defender")
        };

        private static DashboardWidgetDefinition Def(
            string type, string title, string category, string icon, string description,
            int defaultW, int defaultH, int minW = 1, int minH = 2, int? maxW = 12, int? maxH = null,
            string panelKey = "")
        {
            return new DashboardWidgetDefinition
            {
                Type = type,
                Title = title,
                Category = category,
                Icon = icon,
                Description = description,
                DefaultW = defaultW,
                DefaultH = defaultH,
                MinW = minW,
                MinH = minH,
                MaxW = maxW,
                MaxH = maxH,
                PanelKey = string.IsNullOrEmpty(panelKey) ? type : panelKey
            };
        }
    }
}
