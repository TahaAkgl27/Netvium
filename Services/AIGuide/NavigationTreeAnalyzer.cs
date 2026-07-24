using System.Text.RegularExpressions;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal sealed class NavigationTreeAnalysis
    {
        public IReadOnlyList<NavigationTreeNode> FlatNodes { get; init; } = Array.Empty<NavigationTreeNode>();
        public IReadOnlyList<NavigationTreeNode> RootNodes { get; init; } = Array.Empty<NavigationTreeNode>();
        public NavigationTreeNode? SelectedNode { get; init; }
        public string Summary { get; init; } = string.Empty;
        public bool LowConfidence { get; init; }
    }

    /// <summary>
    /// Reconstructs MMC TreeView hierarchy from left-pane OCR text.
    /// </summary>
    internal static class NavigationTreeAnalyzer
    {
        private static readonly Regex ScopePattern = new(
            @"scope\s*\[?\s*(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s*\]?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Ipv4Pattern = new(
            @"\bipv4\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] DhcpScopeChildLabels =
        [
            "address pool", "address leases", "reservations", "scope options", "policies", "failover"
        ];

        private static readonly string[] TreeDecorationChars =
        [
            "├", "│", "└", "►", "▼", "▶", "▷", "•", "·", "-", "–", "—", "+", "▸", "▾"
        ];

        public static NavigationTreeAnalysis Analyze(string treeOcrText, double ocrConfidence)
        {
            if (string.IsNullOrWhiteSpace(treeOcrText))
            {
                return new NavigationTreeAnalysis
                {
                    LowConfidence = true,
                    Summary = string.Empty
                };
            }

            var rawLines = treeOcrText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd())
                .Where(l => l.Trim().Length >= 2)
                .ToList();

            if (rawLines.Count == 0)
            {
                return new NavigationTreeAnalysis { LowConfidence = true };
            }

            var parsed = rawLines
                .Select((line, index) => ParseLine(line, index == rawLines.Count - 1))
                .ToList();

            AssignNodeKinds(parsed);
            InferExpandedScopes(parsed);

            var selected = parsed.LastOrDefault(n => n.IsSelected)
                ?? parsed.LastOrDefault();

            var lowConf = ocrConfidence < 0.45 || parsed.Count < 2;

            return new NavigationTreeAnalysis
            {
                FlatNodes = parsed,
                RootNodes = parsed,
                SelectedNode = selected,
                Summary = BuildFlatSummary(parsed, selected),
                LowConfidence = lowConf
            };
        }

        private static NavigationTreeNode ParseLine(string line, bool isLastLine)
        {
            var depth = MeasureDepth(line);
            var label = CleanLabel(line);

            string? scopeNetwork = null;
            var scopeMatch = ScopePattern.Match(label);
            if (scopeMatch.Success)
                scopeNetwork = scopeMatch.Groups[1].Value;

            return new NavigationTreeNode
            {
                Label = label,
                Depth = depth,
                IsSelected = isLastLine,
                ScopeNetwork = scopeNetwork
            };
        }

        private static int MeasureDepth(string line)
        {
            var leading = 0;
            foreach (var ch in line)
            {
                if (ch == ' ' || ch == '\t')
                    leading++;
                else
                    break;
            }

            var trimmed = line.TrimStart();
            var decorationDepth = 0;
            while (trimmed.Length > 0 && TreeDecorationChars.Any(d => trimmed.StartsWith(d, StringComparison.Ordinal)))
            {
                decorationDepth++;
                trimmed = trimmed[1..].TrimStart();
            }

            var spaceDepth = leading / 2;
            return Math.Max(spaceDepth, decorationDepth);
        }

        private static string CleanLabel(string line)
        {
            var result = line.Trim();
            foreach (var ch in TreeDecorationChars)
            {
                while (result.StartsWith(ch, StringComparison.Ordinal))
                    result = result[ch.Length..].TrimStart();
            }

            return Regex.Replace(result, @"\s{2,}", " ").Trim();
        }

        private static void AssignNodeKinds(List<NavigationTreeNode> nodes)
        {
            NavigationTreeNode? currentScope = null;

            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var lower = node.Label.ToLowerInvariant();
                NavigationNodeKind kind;
                var scopeNetwork = node.ScopeNetwork;

                if (ScopePattern.IsMatch(node.Label))
                {
                    kind = NavigationNodeKind.DhcpScope;
                    currentScope = node;
                }
                else if (Ipv4Pattern.IsMatch(node.Label))
                {
                    kind = NavigationNodeKind.Ipv4;
                    currentScope = null;
                }
                else if (DhcpScopeChildLabels.Any(c => lower.Contains(c, StringComparison.Ordinal)))
                {
                    kind = NavigationNodeKind.ScopeChild;
                    if (currentScope == null)
                    {
                        for (var j = i - 1; j >= 0; j--)
                        {
                            if (nodes[j].Kind == NavigationNodeKind.DhcpScope
                                || ScopePattern.IsMatch(nodes[j].Label))
                            {
                                currentScope = nodes[j];
                                break;
                            }
                        }
                    }
                }
                else if (i == 0)
                {
                    kind = NavigationNodeKind.Root;
                }
                else
                {
                    kind = NavigationNodeKind.Folder;
                }

                nodes[i] = new NavigationTreeNode
                {
                    Label = node.Label,
                    Depth = node.Depth,
                    Kind = kind,
                    IsSelected = node.IsSelected,
                    IsExpanded = node.IsExpanded,
                    ScopeNetwork = scopeNetwork,
                    Children = node.Children
                };
            }
        }

        private static void InferExpandedScopes(List<NavigationTreeNode> nodes)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Kind != NavigationNodeKind.DhcpScope)
                    continue;

                var hasChildBelow = i + 1 < nodes.Count
                    && (nodes[i + 1].Kind == NavigationNodeKind.ScopeChild
                        || DhcpScopeChildLabels.Any(c =>
                            nodes[i + 1].Label.Contains(c, StringComparison.OrdinalIgnoreCase)));

                if (hasChildBelow)
                {
                    nodes[i] = CopyNode(nodes[i], isExpanded: true);
                }
            }
        }

        private static string BuildFlatSummary(IReadOnlyList<NavigationTreeNode> flat, NavigationTreeNode? selected)
        {
            if (flat.Count == 0)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var node in flat)
            {
                var indent = new string(' ', node.Depth * 2);
                var marker = node.IsSelected ? " [SELECTED]" : node.IsExpanded ? " [EXPANDED]" : string.Empty;
                sb.AppendLine($"{indent}{node.Label}{marker}");
            }

            if (selected != null && !string.IsNullOrWhiteSpace(selected.Label))
                sb.AppendLine($"Selected: {selected.Label}");
            return sb.ToString().TrimEnd();
        }

        private static NavigationTreeNode CopyNode(NavigationTreeNode node, bool? isExpanded = null) =>
            new()
            {
                Label = node.Label,
                Depth = node.Depth,
                Kind = node.Kind,
                IsSelected = node.IsSelected,
                IsExpanded = isExpanded ?? node.IsExpanded,
                ScopeNetwork = node.ScopeNetwork,
                Children = node.Children
            };

        private static string BuildSummary(IReadOnlyList<NavigationTreeNode> roots, NavigationTreeNode? selected) =>
            BuildFlatSummary(roots, selected);
    }
}
