using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Netvium.Models.Dashboard
{
    public sealed class DashboardLayoutDocument
    {
        public int Version { get; set; } = 1;
        public string? ServerKey { get; set; }
        public string ActiveWorkspaceId { get; set; } = string.Empty;
        public bool IsLayoutLocked { get; set; }
        public List<DashboardWorkspace> Workspaces { get; set; } = new();
    }

    public sealed class DashboardWorkspace
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Operations";
        public List<DashboardWidgetInstance> Widgets { get; set; } = new();
    }

    public sealed class DashboardWidgetInstance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Type { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; } = 4;
        public int H { get; set; } = 3;
        public int MinW { get; set; } = 1;
        public int MinH { get; set; } = 2;
        public int? MaxW { get; set; } = 12;
        public int? MaxH { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool IsPinned { get; set; }
        public bool IsLocked { get; set; }
        public bool IsCollapsed { get; set; }
        public DashboardWidgetSettings Settings { get; set; } = new();
    }

    public sealed class DashboardWidgetSettings
    {
        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = "\uE9D2";
        public string AccentColor { get; set; } = "#4F8CFF";
        public int RefreshIntervalSeconds { get; set; }
        public double Transparency { get; set; } = 1.0;
        public bool ShowBorder { get; set; } = true;
        public bool ShowHeader { get; set; } = true;
        public bool CompactMode { get; set; }
        public string ChartType { get; set; } = "line";
        public bool AnimationEnabled { get; set; } = true;
        public int DecimalPrecision { get; set; } = 0;
        public double WarningThreshold { get; set; } = 80;
        public double CriticalThreshold { get; set; } = 90;
        public string WarningColor { get; set; } = "#FBBF24";
        public string CriticalColor { get; set; } = "#D4848A";
        public string VisibilityRule { get; set; } = string.Empty;
    }

    public sealed class DashboardWidgetDefinition
    {
        public string Type { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Icon { get; init; } = "\uE9D2";
        public string Description { get; init; } = string.Empty;
        public int DefaultW { get; init; } = 4;
        public int DefaultH { get; init; } = 3;
        public int MinW { get; init; } = 1;
        public int MinH { get; init; } = 2;
        public int? MaxW { get; init; } = 12;
        public int? MaxH { get; init; }
        public string PanelKey { get; init; } = string.Empty;
        public bool IsComposite { get; init; }
    }
}
