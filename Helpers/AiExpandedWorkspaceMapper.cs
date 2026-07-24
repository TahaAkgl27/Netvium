using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    internal static class AiExpandedWorkspaceMapper
    {
        private const string NotAvailable = "Not available";

        public static AiExpandedWorkspaceState Build(
            AiSmartAnalysisReport? report,
            ServerAnalysisContext? ctx,
            ServerIntelligence intelligence,
            WmiSnapshot? snap,
            int healthScore,
            int riskScore,
            string riskLevelLabel,
            DateTime? analysisTimestampUtc,
            TimeSpan? analysisDuration,
            DateTime? previousAnalysisUtc)
        {
            var role = report?.ServerRole?.Role ?? intelligence.DetectedRole ?? ctx?.DetectedRole ?? NotAvailable;
            var confidence = report?.ServerRole?.Confidence
                             ?? (intelligence.Confidence > 0 ? intelligence.Confidence : ctx?.RoleConfidence ?? 0);

            var critical = CollectRisks(ctx, intelligence, report, InsightSeverity.Critical);
            var high = CollectRisks(ctx, intelligence, report, InsightSeverity.Warning);
            var medium = CollectRisks(ctx, intelligence, report, InsightSeverity.Info);
            var low = CollectRisks(ctx, intelligence, report, InsightSeverity.Tip);

            var businessImpact = ResolveBusinessImpactLevel(riskScore, critical.Count, high.Count);
            var infraStatus = ResolveInfrastructureStatus(ctx, intelligence);
            var fixTime = EstimateFixTime(critical.Count, high.Count, report);

            return new AiExpandedWorkspaceState
            {
                HealthScoreValue = healthScore,
                HealthScoreDisplay = $"{healthScore}",
                RiskLevel = string.IsNullOrWhiteSpace(riskLevelLabel) ? NotAvailable : riskLevelLabel.ToUpperInvariant(),
                RiskLevelColor = RiskColor(riskScore),
                AiConfidenceValue = confidence,
                AiConfidenceDisplay = confidence > 0 ? $"{confidence}%" : "—",
                BusinessImpactLevel = businessImpact.Level,
                BusinessImpactColor = businessImpact.Color,
                InfrastructureStatus = infraStatus.Label,
                InfrastructureStatusColor = infraStatus.Color,
                EstimatedFixTime = fixTime,
                AnalysisAge = FormatAnalysisAge(analysisTimestampUtc),
                AnalysisSource = report?.FromAi == true ? "AI + Rules" : report != null ? "Rule Engine" : NotAvailable,
                ServerRole = string.IsNullOrWhiteSpace(role) ? NotAvailable : role,
                Headline = BuildHeadline(healthScore, riskScore, riskLevelLabel, critical, high),
                Subheadline = BuildSubheadline(report, ctx, intelligence, critical),

                InfrastructureMap = BuildInfrastructureMap(ctx, snap, intelligence),
                RiskDistribution = new AiRiskDistributionVisual
                {
                    Critical = critical.Count,
                    High = high.Count,
                    Medium = medium.Count,
                    Low = low.Count
                },
                CriticalRisks = critical,
                HighRisks = high,
                MediumRisks = medium,
                LowRisks = low,
                Changes = BuildChanges(ctx, snap, healthScore, riskScore),
                Timeline = BuildTimeline(snap, ctx, analysisTimestampUtc),
                ReasoningPipeline = BuildReasoningPipeline(report, ctx, intelligence, snap, healthScore, riskScore),
                BusinessImpacts = BuildBusinessImpacts(ctx, intelligence, critical, high),
                ActionCenter = BuildActionCenter(report, intelligence, riskScore),
                Predictions = BuildPredictions(report, ctx),
                LiveInsights = BuildLiveInsights(ctx, snap, intelligence),
                TrendSparklines = BuildSparklines(ctx, snap, healthScore, riskScore)
            };
        }

        private static string BuildHeadline(int health, int risk, string riskLabel, List<AiExpandedRiskItem> critical, List<AiExpandedRiskItem> high)
        {
            if (critical.Count > 0)
                return $"Critical attention required — {critical[0].Title}";
            if (high.Count > 0)
                return $"Elevated risk detected — {high[0].Title}";
            if (health >= 85)
                return "Infrastructure operating within healthy parameters";
            if (health >= 60)
                return "Warning indicators detected — review recommended";
            return $"{riskLabel} state — risk score {risk}/100";
        }

        private static string BuildSubheadline(AiSmartAnalysisReport? report, ServerAnalysisContext? ctx, ServerIntelligence intelligence, List<AiExpandedRiskItem> critical)
        {
            if (critical.Count > 0)
                return Truncate(critical[0].Reason, 120);
            var top = intelligence.Insights.FirstOrDefault()?.Title;
            if (!string.IsNullOrWhiteSpace(top))
                return Truncate(top, 120);
            return report?.HealthScore?.Text ?? "Complete a refresh cycle to populate intelligence.";
        }

        private static List<AiInfraHealthNode> BuildInfrastructureMap(ServerAnalysisContext? ctx, WmiSnapshot? snap, ServerIntelligence intelligence)
        {
            var nodes = new List<AiInfraHealthNode>
            {
                Node("Identity", ResolveIdentityStatus(ctx, snap), "\uE77B"),
                Node("DNS", ResolveDnsStatus(snap), "\uE968"),
                Node("DHCP", AiInfraStatus.Unknown, "\uE968"),
                Node("Active Directory", ResolveAdStatus(snap, ctx), "\uE716"),
                Node("IIS", ResolveIisStatus(ctx, snap), "\uE774"),
                Node("SQL", ResolveSqlStatus(ctx, snap), "\uE943"),
                Node("WinRM", ctx == null ? AiInfraStatus.Unknown : ctx.WinRmRunning ? AiInfraStatus.Healthy : AiInfraStatus.Warning, "\uE945"),
                Node("Firewall", ctx == null ? AiInfraStatus.Unknown : ctx.FirewallRunning ? AiInfraStatus.Healthy : AiInfraStatus.Critical, "\uEAF6"),
                Node("Defender", ctx == null ? AiInfraStatus.Unknown : ctx.AntivirusRunning ? AiInfraStatus.Healthy : AiInfraStatus.Warning, "\uE1D2"),
                Node("Certificates", AiInfraStatus.Unknown, "\uE72E"),
                Node("Network", ResolveNetworkStatus(ctx), "\uE968"),
                Node("Storage", ResolveStorageStatus(ctx), "\uE964"),
                Node("Authentication", ctx == null ? AiInfraStatus.Unknown : ctx.RdpOpen ? AiInfraStatus.Healthy : AiInfraStatus.Warning, "\uE8FA")
            };

            foreach (var comp in intelligence.Components.Take(3))
            {
                if (nodes.Any(n => n.Name.Equals(comp.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                nodes.Add(Node(comp.Name, MapComponentStatus(comp.Status), comp.Icon));
            }

            return nodes;
        }

        private static AiInfraHealthNode Node(string name, AiInfraStatus status, string icon) => new()
        {
            Name = name,
            Status = status,
            StatusLabel = StatusLabel(status),
            StatusColor = StatusColor(status),
            IconGlyph = icon
        };

        private static AiInfraStatus MapComponentStatus(string status)
        {
            var s = status?.ToLowerInvariant() ?? "";
            if (s.Contains("critical") || s.Contains("stopped") || s.Contains("offline") || s.Contains("closed"))
                return AiInfraStatus.Critical;
            if (s.Contains("warn") || s.Contains("degrad") || s.Contains("pasif"))
                return AiInfraStatus.Warning;
            if (s.Contains("active") || s.Contains("open") || s.Contains("running") || s.Contains("healthy"))
                return AiInfraStatus.Healthy;
            return AiInfraStatus.Unknown;
        }

        private static AiInfraStatus ResolveIdentityStatus(ServerAnalysisContext? ctx, WmiSnapshot? snap) =>
            !string.IsNullOrWhiteSpace(ctx?.LastUser) || snap?.ActiveUsers > 0 ? AiInfraStatus.Healthy : AiInfraStatus.Unknown;

        private static AiInfraStatus ResolveDnsStatus(WmiSnapshot? snap) =>
            snap?.DnsServers?.Count > 0 ? AiInfraStatus.Healthy : AiInfraStatus.Unknown;

        private static AiInfraStatus ResolveAdStatus(WmiSnapshot? snap, ServerAnalysisContext? ctx)
        {
            var role = ctx?.DetectedRole ?? "";
            if (role.Contains("Domain Controller", StringComparison.OrdinalIgnoreCase)
                || role.Contains("Active Directory", StringComparison.OrdinalIgnoreCase))
                return ctx?.StoppedCriticalServices.Count > 0 ? AiInfraStatus.Warning : AiInfraStatus.Healthy;
            return string.IsNullOrWhiteSpace(snap?.DomainName) ? AiInfraStatus.Unknown : AiInfraStatus.Healthy;
        }

        private static AiInfraStatus ResolveIisStatus(ServerAnalysisContext? ctx, WmiSnapshot? snap)
        {
            if (ctx?.InstalledRoles.Any(r => r.Contains("Web", StringComparison.OrdinalIgnoreCase)) == true)
                return AiInfraStatus.Healthy;
            if (snap?.ListenPorts?.Any(p => p is 80 or 443) == true)
                return AiInfraStatus.Healthy;
            return AiInfraStatus.Unknown;
        }

        private static AiInfraStatus ResolveSqlStatus(ServerAnalysisContext? ctx, WmiSnapshot? snap)
        {
            if (snap?.ListenPorts?.Contains(1433) == true)
                return AiInfraStatus.Healthy;
            if (ctx?.InstalledRoles.Any(r => r.Contains("SQL", StringComparison.OrdinalIgnoreCase)) == true)
                return ctx.StoppedCriticalServices.Count > 0 ? AiInfraStatus.Warning : AiInfraStatus.Healthy;
            return AiInfraStatus.Unknown;
        }

        private static AiInfraStatus ResolveNetworkStatus(ServerAnalysisContext? ctx)
        {
            if (ctx?.LatencyMs == null) return AiInfraStatus.Unknown;
            if (ctx.LatencyMs > 150) return AiInfraStatus.Warning;
            if (ctx.LatencyMs < 0) return AiInfraStatus.Critical;
            return AiInfraStatus.Healthy;
        }

        private static AiInfraStatus ResolveStorageStatus(ServerAnalysisContext? ctx)
        {
            if (ctx?.DiskPercent == null) return AiInfraStatus.Unknown;
            if (ctx.DiskPercent > 90) return AiInfraStatus.Critical;
            if (ctx.DiskPercent > 75) return AiInfraStatus.Warning;
            return AiInfraStatus.Healthy;
        }

        private static List<AiChangeIndicator> BuildChanges(ServerAnalysisContext? ctx, WmiSnapshot? snap, int health, int risk)
        {
            var list = new List<AiChangeIndicator>();
            if (ctx?.Trends == null || ctx.Trends.SampleCount < 2)
            {
                list.Add(Change("Trend History", "Insufficient data", AiDeltaDirection.Unknown, "—", "#64748B"));
                return list;
            }

            list.Add(Change("CPU", ctx.CpuPercent.HasValue ? $"{ctx.CpuPercent}%" : "—", ParseDirection(ctx.Trends.CpuTrend), ctx.Trends.CpuTrend ?? "Stable", TrendColor(ctx.Trends.CpuTrend, invert: false)));
            list.Add(Change("Memory", ctx.RamPercent.HasValue ? $"{ctx.RamPercent:0}%" : "—", ParseDirection(ctx.Trends.RamTrend), ctx.Trends.RamTrend ?? "Stable", TrendColor(ctx.Trends.RamTrend, invert: false)));
            list.Add(Change("Disk", ctx.DiskPercent.HasValue ? $"{ctx.DiskPercent:0}%" : "—", ParseDirection(ctx.Trends.DiskTrend), ctx.Trends.DiskTrend ?? "Stable", TrendColor(ctx.Trends.DiskTrend, invert: false)));
            list.Add(Change("Services", ctx.StoppedCriticalServices.Count > 0 ? $"{ctx.StoppedCriticalServices.Count} stopped" : "Stable", ctx.StoppedCriticalServices.Count > 0 ? AiDeltaDirection.Up : AiDeltaDirection.Stable, ctx.StoppedCriticalServices.Count > 0 ? "Critical services affected" : "No change", ctx.StoppedCriticalServices.Count > 0 ? "#F97316" : "#22C55E"));
            list.Add(Change("Events", ctx.RecentEventLogs.Count > 0 ? $"{ctx.RecentEventLogs.Count} recent" : "None", ParseDirection(ctx.Trends.EventTrend), ctx.Trends.EventTrend ?? "Stable", TrendColor(ctx.Trends.EventTrend, invert: true)));
            list.Add(Change("Risk Score", $"{risk}/100", ParseDirection(ctx.Trends.RiskTrend), ctx.Trends.RiskTrend ?? "Stable", TrendColor(ctx.Trends.RiskTrend, invert: true)));
            list.Add(Change("Health Score", $"{health}/100", ParseDirection(ctx.Trends.HealthTrend), ctx.Trends.HealthTrend ?? "Stable", TrendColor(ctx.Trends.HealthTrend, invert: false)));
            if (ctx.LatencyMs.HasValue)
                list.Add(Change("Latency", $"{ctx.LatencyMs} ms", AiDeltaDirection.Stable, snap?.PingMs >= 0 ? "Live ping" : "Measured", "#4F8CFF"));

            return list;
        }

        private static AiChangeIndicator Change(string metric, string value, AiDeltaDirection dir, string delta, string color) => new()
        {
            Metric = metric,
            CurrentValue = value,
            Direction = dir,
            ArrowGlyph = dir switch { AiDeltaDirection.Up => "\uE70E", AiDeltaDirection.Down => "\uE70D", _ => "\uE910" },
            DeltaLabel = delta,
            AccentColor = color
        };

        private static List<AiTimelineNode> BuildTimeline(WmiSnapshot? snap, ServerAnalysisContext? ctx, DateTime? analysisUtc)
        {
            var nodes = new List<AiTimelineNode>();

            if (snap?.Timeline?.Count > 0)
            {
                foreach (var ev in snap.Timeline.OrderBy(e => e.Timestamp).Take(6))
                {
                    nodes.Add(new AiTimelineNode
                    {
                        TimeLabel = ev.Timestamp.ToLocalTime().ToString("HH:mm"),
                        Title = Truncate(ev.Message, 80),
                        Detail = ev.Type,
                        SeverityColor = ev.Level.Equals("Error", StringComparison.OrdinalIgnoreCase) ? "#F97316"
                            : ev.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? "#F59E0B" : "#4F8CFF"
                    });
                }
            }
            else if (ctx?.RecentEventLogs.Count > 0)
            {
                foreach (var ev in ctx.RecentEventLogs.Take(4))
                {
                    nodes.Add(new AiTimelineNode
                    {
                        TimeLabel = "Recent",
                        Title = Truncate(ev, 80),
                        Detail = "Event Log",
                        SeverityColor = "#F59E0B"
                    });
                }
            }

            if (ctx?.StoppedCriticalServices.Count > 0)
            {
                nodes.Add(new AiTimelineNode
                {
                    TimeLabel = "Now",
                    Title = $"Critical service stopped: {ctx.StoppedCriticalServices[0]}",
                    Detail = "Services",
                    SeverityColor = "#F97316"
                });
            }

            if (analysisUtc.HasValue)
            {
                nodes.Add(new AiTimelineNode
                {
                    TimeLabel = analysisUtc.Value.ToLocalTime().ToString("HH:mm"),
                    Title = "AI Analysis completed",
                    Detail = "Analysis",
                    SeverityColor = "#A78BFA",
                    ShowConnector = false
                });
            }

            if (nodes.Count == 0)
            {
                nodes.Add(new AiTimelineNode
                {
                    TimeLabel = "—",
                    Title = "No timeline events available",
                    Detail = "Awaiting event log enrichment",
                    SeverityColor = "#64748B",
                    ShowConnector = false
                });
            }
            else
            {
                var last = nodes[^1];
                nodes[^1] = new AiTimelineNode
                {
                    TimeLabel = last.TimeLabel,
                    Title = last.Title,
                    Detail = last.Detail,
                    SeverityColor = last.SeverityColor,
                    ShowConnector = false
                };
            }

            return nodes;
        }

        private static List<AiReasoningStepVisual> BuildReasoningPipeline(
            AiSmartAnalysisReport? report,
            ServerAnalysisContext? ctx,
            ServerIntelligence intelligence,
            WmiSnapshot? snap,
            int healthScore,
            int riskScore)
        {
            var hasTelemetry = snap?.Connected == true || ctx?.HasWmiData == true;
            var hasRules = ctx?.RuleFindings.Count > 0 || ctx?.TopFindings.Count > 0;
            var hasHistory = ctx?.Trends?.SampleCount >= 2;
            var hasAi = report?.FromAi == true;

            return new List<AiReasoningStepVisual>
            {
                Step("Telemetry", hasTelemetry ? "Live WMI / agent metrics ingested" : NotAvailable, "\uE9F5", hasTelemetry),
                Step("Rule Engine", hasRules ? $"{ctx!.RuleFindings.Count} rule findings evaluated" : "Baseline rules applied", "\uE90F", hasRules || ctx != null),
                Step("Infrastructure Context", ctx != null ? $"Role: {ctx.DetectedRole}" : NotAvailable, "\uE774", ctx != null),
                Step("Historical Snapshot", hasHistory ? $"{ctx!.Trends!.SampleCount} samples compared" : "Insufficient history", "\uE81C", hasHistory),
                Step("AI Analysis", hasAi ? "GPT enrichment merged" : "Rule-only mode", "\uE774", report != null),
                Step("Decision", $"Health {healthScore} · Risk {riskScore}", "\uE734", ctx != null),
                Step("Recommendations", $"{(report?.RecommendedActions.Count ?? 0) + intelligence.Recommendations.Count} actions generated", "\uE735", (report?.RecommendedActions.Count ?? 0) + intelligence.Recommendations.Count > 0)
            };
        }

        private static AiReasoningStepVisual Step(string title, string subtitle, string icon, bool complete) => new()
        {
            Title = title,
            Subtitle = subtitle,
            IconGlyph = icon,
            IsComplete = complete
        };

        private static List<AiBusinessImpactVisual> BuildBusinessImpacts(
            ServerAnalysisContext? ctx,
            ServerIntelligence intelligence,
            List<AiExpandedRiskItem> critical,
            List<AiExpandedRiskItem> high)
        {
            var severity = critical.Count > 0 ? "Critical" : high.Count > 0 ? "High" : ctx?.RiskScore > 60 ? "Medium" : "Low";
            var fill = severity switch { "Critical" => 0.95, "High" => 0.75, "Medium" => 0.5, "Low" => 0.25, _ => 0.1 };
            var color = severity switch { "Critical" => "#F97316", "High" => "#F59E0B", "Medium" => "#94A3B8", _ => "#22C55E" };

            var areas = new[] { "Authentication", "Printing", "ERP", "File Shares", "Web Applications", "Remote Access", "Backups", "User Productivity" };
            var list = new List<AiBusinessImpactVisual>();

            foreach (var area in areas)
            {
                var level = MapAreaImpact(area, ctx, severity);
                list.Add(new AiBusinessImpactVisual
                {
                    Area = area,
                    ImpactLevel = level,
                    ImpactColor = ImpactLevelColor(level),
                    ImpactFill = ImpactFill(level)
                });
            }

            return list;
        }

        private static string MapAreaImpact(string area, ServerAnalysisContext? ctx, string defaultSeverity)
        {
            if (ctx == null) return "No Impact";
            return area switch
            {
                "Authentication" when !ctx.RdpOpen || ctx.StoppedCriticalServices.Any(s => s.Contains("NTDS", StringComparison.OrdinalIgnoreCase)) => "High",
                "Remote Access" when !ctx.RdpOpen => "High",
                "Web Applications" when ctx.InstalledRoles.Any(r => r.Contains("Web", StringComparison.OrdinalIgnoreCase)) && ctx.StoppedCriticalServices.Count > 0 => "Medium",
                "File Shares" when !ctx.SmbRunning => "Medium",
                "Backups" when ctx.DiskPercent > 85 => "Medium",
                "User Productivity" when ctx.CpuPercent > 85 || ctx.RamPercent > 90 => "Medium",
                _ when defaultSeverity == "Critical" => "Medium",
                _ when defaultSeverity == "High" => "Low",
                _ => "No Impact"
            };
        }

        private static List<AiActionCenterCard> BuildActionCenter(AiSmartAnalysisReport? report, ServerIntelligence intelligence, int riskScore)
        {
            var cards = new List<AiActionCenterCard>();

            foreach (var action in report?.RecommendedActions ?? new List<AiActionItem>())
            {
                var reduction = RiskReduction(action.Priority);
                cards.Add(new AiActionCenterCard
                {
                    Priority = action.Priority,
                    PriorityColor = PriorityColor(action.Priority),
                    Title = action.Text,
                    ExpectedImprovement = InferImprovement(action.Priority, action.Text),
                    EstimatedDuration = DurationFromPriority(action.Priority),
                    RequiredPermissions = InferPermissions(action.Text),
                    AutomationNote = "Automation roadmap",
                    CommandExample = ExtractCommandHint(action.Evidence),
                    RiskBefore = riskScore,
                    RiskAfter = Math.Max(0, riskScore - reduction),
                    BusinessImpactReduction = reduction >= 20 ? "High" : reduction >= 10 ? "Medium" : "Low"
                });
            }

            foreach (var rec in intelligence.Recommendations)
            {
                if (cards.Any(c => c.Title == rec.Description || c.Title == rec.Action))
                    continue;
                var reduction = RiskReduction(rec.Priority);
                cards.Add(new AiActionCenterCard
                {
                    Priority = rec.Priority,
                    PriorityColor = PriorityColor(rec.Priority),
                    Title = string.IsNullOrWhiteSpace(rec.Description) ? rec.Action : rec.Description,
                    ExpectedImprovement = NotAvailable,
                    EstimatedDuration = DurationFromPriority(rec.Priority),
                    RequiredPermissions = InferPermissions(rec.Action),
                    AutomationNote = "Automation roadmap",
                    CommandExample = NotAvailable,
                    RiskBefore = riskScore,
                    RiskAfter = Math.Max(0, riskScore - reduction),
                    BusinessImpactReduction = "Low"
                });
            }

            return cards.Take(8).ToList();
        }

        private static List<AiPredictionVisual> BuildPredictions(AiSmartAnalysisReport? report, ServerAnalysisContext? ctx)
        {
            var list = new List<AiPredictionVisual>();

            if (report?.Prediction?.Available == true && !string.IsNullOrWhiteSpace(report.Prediction.Text))
            {
                list.Add(new AiPredictionVisual
                {
                    Title = "Disk / Capacity",
                    Detail = report.Prediction.Text,
                    SeverityColor = "#F59E0B",
                    Available = true
                });
            }
            else if (ctx?.DiskTrendPrediction?.Available == true)
            {
                list.Add(new AiPredictionVisual
                {
                    Title = "Disk Exhaustion",
                    Detail = ctx.DiskTrendPrediction.Text,
                    SeverityColor = "#F59E0B",
                    Available = true
                });
            }

            if (ctx?.Trends?.RiskTrend?.Contains("up", StringComparison.OrdinalIgnoreCase) == true
                || ctx?.Trends?.RiskTrend?.Contains("degrad", StringComparison.OrdinalIgnoreCase) == true)
            {
                list.Add(new AiPredictionVisual
                {
                    Title = "Risk Escalation",
                    Detail = ctx.Trends.RiskTrend,
                    SeverityColor = "#F97316",
                    Available = true
                });
            }

            if (ctx?.CpuPercent > 80)
                list.Add(new AiPredictionVisual { Title = "CPU Saturation", Detail = $"Current CPU {ctx.CpuPercent}% — sustained high utilization", SeverityColor = "#F59E0B", Available = true });

            if (ctx?.RamPercent > 85)
                list.Add(new AiPredictionVisual { Title = "Memory Pressure", Detail = $"RAM at {ctx.RamPercent:0}%", SeverityColor = "#F59E0B", Available = true });

            if (list.Count == 0)
            {
                list.Add(new AiPredictionVisual
                {
                    Title = "Predictive models pending",
                    Detail = "Additional snapshot history required for reliable forecasts.",
                    SeverityColor = "#64748B",
                    Available = false
                });
            }

            return list.Take(5).ToList();
        }

        private static List<AiLiveInsightCard> BuildLiveInsights(ServerAnalysisContext? ctx, WmiSnapshot? snap, ServerIntelligence intelligence)
        {
            var cards = new List<AiLiveInsightCard>();

            if (snap?.TopProcesses?.Count > 0)
            {
                var top = snap.TopProcesses.OrderByDescending(p => p.CpuPct ?? 0).First();
                cards.Add(Live("\uE9F5", "Top CPU Process", top.Name, "#4F8CFF", (top.CpuPct ?? 0) / 100.0));
            }
            else if (ctx?.CpuPercent.HasValue == true)
                cards.Add(Live("\uE9F5", "CPU Load", $"{ctx.CpuPercent}%", ctx.CpuPercent > 85 ? "#F97316" : "#22C55E", ctx.CpuPercent.Value / 100.0));

            if (ctx?.RamPercent.HasValue == true)
                cards.Add(Live("\uE964", "Memory", $"{ctx.RamPercent:0}%", ctx.RamPercent > 90 ? "#F97316" : "#4F8CFF", ctx.RamPercent.Value / 100.0));

            if (ctx?.DiskPercent.HasValue == true)
                cards.Add(Live("\uE7F4", "Storage", $"{ctx.DiskPercent:0}% used", ctx.DiskPercent > 85 ? "#F59E0B" : "#22C55E", ctx.DiskPercent.Value / 100.0));

            if (ctx?.LatencyMs.HasValue == true)
                cards.Add(Live("\uE968", "Network Latency", $"{ctx.LatencyMs} ms", ctx.LatencyMs > 150 ? "#F59E0B" : "#22C55E", Math.Min(1, ctx.LatencyMs.Value / 300.0)));

            if (ctx?.StoppedCriticalServices.Count > 0)
                cards.Add(Live("\uE90F", "Critical Services", $"{ctx.StoppedCriticalServices.Count} stopped", "#F97316", 0.9));

            if (snap?.ActiveUsers.HasValue == true)
                cards.Add(Live("\uE77B", "Open Sessions", $"{snap.ActiveUsers}", "#4F8CFF", Math.Min(1, snap.ActiveUsers.Value / 10.0)));

            cards.Add(Live("\uE1D2", "Security", ctx != null ? $"FW {(ctx.FirewallRunning ? "On" : "Off")} · AV {(ctx.AntivirusRunning ? "On" : "Off")}" : NotAvailable,
                ctx is { FirewallRunning: true, AntivirusRunning: true } ? "#22C55E" : "#F59E0B", ctx is { FirewallRunning: true, AntivirusRunning: true } ? 0.3 : 0.7));

            if (ctx?.RecentEventLogs.Count > 0)
                cards.Add(Live("\uE81C", "Latest Anomaly", Truncate(ctx.RecentEventLogs[0], 40), "#F59E0B", 0.6));

            return cards.Take(8).ToList();
        }

        private static AiLiveInsightCard Live(string icon, string title, string value, string color, double level) => new()
        {
            IconGlyph = icon,
            Title = title,
            Value = value,
            StatusColor = color,
            ActivityLevel = Math.Clamp(level, 0, 1)
        };

        private static List<AiSparklineVisual> BuildSparklines(ServerAnalysisContext? ctx, WmiSnapshot? snap, int health, int risk)
        {
            var trends = ctx?.Trends;
            return new List<AiSparklineVisual>
            {
                Spark("CPU", ctx?.CpuPercent.HasValue == true ? $"{ctx.CpuPercent}%" : "—", trends?.CpuTrend, BuildPoints(ctx?.CpuPercent, trends?.CpuTrend), MetricTrendColor(trends?.CpuTrend, false)),
                Spark("Memory", ctx?.RamPercent.HasValue == true ? $"{ctx.RamPercent:0}%" : "—", trends?.RamTrend, BuildPoints(ctx?.RamPercent, trends?.RamTrend), MetricTrendColor(trends?.RamTrend, false)),
                Spark("Disk", ctx?.DiskPercent.HasValue == true ? $"{ctx.DiskPercent:0}%" : "—", trends?.DiskTrend, BuildPoints(ctx?.DiskPercent, trends?.DiskTrend), MetricTrendColor(trends?.DiskTrend, false)),
                Spark("Health", $"{health}", trends?.HealthTrend, BuildPoints(health, trends?.HealthTrend), MetricTrendColor(trends?.HealthTrend, false)),
                Spark("Risk", $"{risk}", trends?.RiskTrend, BuildPoints(risk, trends?.RiskTrend), MetricTrendColor(trends?.RiskTrend, true)),
                Spark("Latency", ctx?.LatencyMs.HasValue == true ? $"{ctx.LatencyMs} ms" : snap?.PingMs >= 0 ? $"{snap.PingMs} ms" : "—", "Stable", BuildPoints(ctx?.LatencyMs, "Stable"), "#4F8CFF")
            };
        }

        private static AiSparklineVisual Spark(string label, string current, string? trend, IReadOnlyList<double> points, string color) => new()
        {
            Label = label,
            CurrentValue = current,
            TrendLabel = string.IsNullOrWhiteSpace(trend) ? "Stable" : trend,
            Points = points,
            AccentColor = color
        };

        private static IReadOnlyList<double> BuildPoints(double? current, string? trend)
        {
            var end = Math.Clamp((current ?? 50) / 100.0, 0.05, 1.0);
            var dir = ParseDirection(trend);
            var points = new double[6];
            for (var i = 0; i < 6; i++)
            {
                var t = i / 5.0;
                points[i] = dir switch
                {
                    AiDeltaDirection.Up => end * (0.4 + 0.6 * t),
                    AiDeltaDirection.Down => end * (1.0 - 0.5 * t),
                    _ => end * (0.85 + 0.15 * Math.Sin(i))
                };
            }
            points[^1] = end;
            return points;
        }

        private static IReadOnlyList<double> BuildPoints(int current, string? trend)
        {
            var end = Math.Clamp(current / 100.0, 0.05, 1.0);
            return BuildPoints((double)current, trend);
        }

        private static List<AiExpandedRiskItem> CollectRisks(
            ServerAnalysisContext? ctx,
            ServerIntelligence intelligence,
            AiSmartAnalysisReport? report,
            InsightSeverity severity)
        {
            var items = new List<AiExpandedRiskItem>();
            var color = severity switch
            {
                InsightSeverity.Critical => "#F97316",
                InsightSeverity.Warning => "#F59E0B",
                InsightSeverity.Info => "#94A3B8",
                _ => "#64748B"
            };

            if (ctx?.RuleFindings != null)
            {
                foreach (var finding in ctx.RuleFindings.Where(f => f.Severity == severity))
                {
                    items.Add(new AiExpandedRiskItem
                    {
                        Title = finding.Title,
                        Reason = finding.Evidence,
                        BusinessImpact = MapBusinessImpact(finding.Severity, finding.Category),
                        TechnicalImpact = finding.Evidence,
                        Confidence = ctx.RoleConfidence > 0 ? $"{ctx.RoleConfidence}%" : NotAvailable,
                        SeverityColor = color
                    });
                }
            }

            if (severity == InsightSeverity.Critical)
            {
                foreach (var ins in intelligence.Insights.Where(i => i.Severity == InsightSeverity.Critical))
                {
                    if (items.Any(x => x.Title == ins.Title)) continue;
                    items.Add(new AiExpandedRiskItem
                    {
                        Title = ins.Title,
                        Reason = ins.Description,
                        BusinessImpact = "Availability impact",
                        TechnicalImpact = ins.Description,
                        Confidence = intelligence.Confidence > 0 ? $"{intelligence.Confidence}%" : NotAvailable,
                        SeverityColor = color
                    });
                }
                foreach (var cf in report?.CriticalFindings ?? new List<AiFindingItem>())
                {
                    if (items.Any(x => x.Title == cf.Text)) continue;
                    items.Add(new AiExpandedRiskItem
                    {
                        Title = cf.Text,
                        Reason = cf.Evidence,
                        BusinessImpact = "Operational risk",
                        TechnicalImpact = cf.Evidence,
                        Confidence = intelligence.Confidence > 0 ? $"{intelligence.Confidence}%" : NotAvailable,
                        SeverityColor = color
                    });
                }
            }

            return items.Take(6).ToList();
        }

        private static (string Level, string Color) ResolveBusinessImpactLevel(int risk, int critical, int high)
        {
            if (critical > 0) return ("Critical", "#F97316");
            if (high > 0 || risk >= 70) return ("High", "#F59E0B");
            if (risk >= 40) return ("Medium", "#94A3B8");
            if (risk > 0) return ("Low", "#22C55E");
            return ("No Impact", "#64748B");
        }

        private static (string Label, string Color) ResolveInfrastructureStatus(ServerAnalysisContext? ctx, ServerIntelligence intelligence)
        {
            if (ctx == null) return ("Unknown", "#64748B");
            var critical = intelligence.Components.Count(c => MapComponentStatus(c.Status) == AiInfraStatus.Critical);
            var warn = intelligence.Components.Count(c => MapComponentStatus(c.Status) == AiInfraStatus.Warning);
            if (ctx.StoppedCriticalServices.Count > 0 || critical > 0 || !ctx.FirewallRunning)
                return ("Degraded", "#F97316");
            if (warn > 0 || ctx.RiskScore >= 60)
                return ("Warning", "#F59E0B");
            return ("Healthy", "#22C55E");
        }

        private static string EstimateFixTime(int critical, int high, AiSmartAnalysisReport? report)
        {
            if (critical > 0) return "15–60 min";
            if (high > 0) return "30–120 min";
            if (report?.RecommendedActions.Count > 0) return DurationFromPriority(report.RecommendedActions[0].Priority);
            return NotAvailable;
        }

        private static string MapBusinessImpact(InsightSeverity severity, string category) =>
            severity == InsightSeverity.Critical ? $"Critical · {category}" :
            severity == InsightSeverity.Warning ? $"Warning · {category}" : $"Info · {category}";

        private static int RiskReduction(string? priority) => priority?.ToUpperInvariant() switch
        {
            "HIGH" or "CRITICAL" => 24,
            "MEDIUM" => 12,
            "LOW" => 6,
            _ => 8
        };

        private static AiDeltaDirection ParseDirection(string? trend)
        {
            if (string.IsNullOrWhiteSpace(trend)) return AiDeltaDirection.Stable;
            var t = trend.ToLowerInvariant();
            if (t.Contains("up") || t.Contains("increas") || t.Contains("high") || t.Contains("degrad") || t.Contains("worse"))
                return AiDeltaDirection.Up;
            if (t.Contains("down") || t.Contains("decreas") || t.Contains("low") || t.Contains("improv") || t.Contains("better"))
                return AiDeltaDirection.Down;
            return AiDeltaDirection.Stable;
        }

        private static string TrendColor(string? trend, bool invert) =>
            ParseDirection(trend) switch
            {
                AiDeltaDirection.Up => invert ? "#F97316" : "#22C55E",
                AiDeltaDirection.Down => invert ? "#22C55E" : "#F97316",
                _ => "#94A3B8"
            };

        private static string MetricTrendColor(string? trend, bool invert) => TrendColor(trend, invert);

        private static string RiskColor(int risk) => risk >= 70 ? "#F97316" : risk >= 40 ? "#F59E0B" : "#22C55E";

        private static string PriorityColor(string? p) => p?.ToUpperInvariant() switch
        {
            "HIGH" or "CRITICAL" => "#F97316",
            "MEDIUM" => "#F59E0B",
            _ => "#64748B"
        };

        private static string StatusLabel(AiInfraStatus s) => s switch
        {
            AiInfraStatus.Healthy => "Healthy",
            AiInfraStatus.Warning => "Warning",
            AiInfraStatus.Critical => "Critical",
            _ => "Unknown"
        };

        private static string StatusColor(AiInfraStatus s) => s switch
        {
            AiInfraStatus.Healthy => "#22C55E",
            AiInfraStatus.Warning => "#F59E0B",
            AiInfraStatus.Critical => "#F97316",
            _ => "#64748B"
        };

        private static string ImpactLevelColor(string level) => level switch
        {
            "Critical" => "#F97316",
            "High" => "#F59E0B",
            "Medium" => "#94A3B8",
            "Low" => "#64748B",
            _ => "#22C55E"
        };

        private static double ImpactFill(string level) => level switch
        {
            "Critical" => 1.0,
            "High" => 0.75,
            "Medium" => 0.5,
            "Low" => 0.3,
            _ => 0.1
        };

        private static string DurationFromPriority(string? priority) => priority?.ToUpperInvariant() switch
        {
            "HIGH" or "CRITICAL" => "15–60 min",
            "MEDIUM" => "30–120 min",
            "LOW" => "Maint. window",
            _ => NotAvailable
        };

        private static string InferImprovement(string? priority, string text)
        {
            if (text.Contains("disk", StringComparison.OrdinalIgnoreCase)) return "Storage headroom";
            if (text.Contains("service", StringComparison.OrdinalIgnoreCase)) return "Service availability";
            if (text.Contains("firewall", StringComparison.OrdinalIgnoreCase)) return "Security posture";
            return priority?.Equals("HIGH", StringComparison.OrdinalIgnoreCase) == true ? "Risk reduction" : NotAvailable;
        }

        private static string InferPermissions(string text)
        {
            if (text.Contains("service", StringComparison.OrdinalIgnoreCase)) return "Local Admin";
            if (text.Contains("firewall", StringComparison.OrdinalIgnoreCase)) return "Security Admin";
            if (text.Contains("domain", StringComparison.OrdinalIgnoreCase)) return "Domain Admin";
            return "Administrator";
        }

        private static string ExtractCommandHint(string? evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence)) return NotAvailable;
            if (evidence.Contains("sc ", StringComparison.OrdinalIgnoreCase)
                || evidence.Contains("Get-", StringComparison.OrdinalIgnoreCase)
                || evidence.Contains("net ", StringComparison.OrdinalIgnoreCase))
                return Truncate(evidence, 60);
            return NotAvailable;
        }

        private static string FormatAnalysisAge(DateTime? utc)
        {
            if (!utc.HasValue) return NotAvailable;
            var age = DateTime.UtcNow - utc.Value.ToUniversalTime();
            if (age.TotalMinutes < 1) return "Just now";
            if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
            if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
            return $"{(int)age.TotalDays}d ago";
        }

        private static string Truncate(string? text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return NotAvailable;
            return text.Length <= max ? text : text[..max] + "…";
        }
    }
}
