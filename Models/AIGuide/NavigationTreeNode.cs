namespace Netvium.Models.AIGuide

{

    public enum NavigationNodeKind

    {

        Unknown = 0,

        Root,

        Ipv4,

        DhcpScope,

        ScopeChild,

        Folder,

        Leaf

    }



    /// <summary>

    /// A node reconstructed from OCR tree text or vision controls.

    /// </summary>

    public sealed class NavigationTreeNode

    {

        public string Label { get; init; } = string.Empty;

        public int Depth { get; init; }

        public NavigationNodeKind Kind { get; init; } = NavigationNodeKind.Unknown;

        public bool IsSelected { get; init; }

        public bool IsExpanded { get; init; }

        public string? ScopeNetwork { get; init; }

        public IReadOnlyList<NavigationTreeNode> Children { get; init; } = Array.Empty<NavigationTreeNode>();

    }



    public sealed class DetectedVisibleControl

    {

        public string Kind { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public string Location { get; init; } = string.Empty;

        public bool IsFocused { get; init; }

    }



    public sealed class NavigationGuidanceHint

    {

        public string CurrentScreen { get; init; } = string.Empty;

        public string VisibleNavigation { get; init; } = string.Empty;

        public string NextClick { get; init; } = string.Empty;

        public string Reason { get; init; } = string.Empty;

        public string ExpectedResult { get; init; } = string.Empty;

        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> NavigationSteps { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> NavigationPath { get; init; } = Array.Empty<string>();

        public string TargetScopeName { get; init; } = string.Empty;

        public string? TargetIp { get; init; }

        public bool IsDestinationVisible { get; init; }

        public bool IsOnCorrectTool { get; init; }

        public bool UseLocalAnswer { get; init; }

        public string? LocalAnswer { get; init; }

        public string PromptDirective { get; init; } = string.Empty;

        public bool LowOcrTreeConfidence { get; init; }

        public bool ScopeAmbiguous { get; init; }

        public string ConfidenceLabel { get; init; } = string.Empty;

    }

}

