using Netvium.Models;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Builds structured runtime context from vision observation and Netvium telemetry.
    /// Observation-only — never controls the environment.
    /// </summary>
    public sealed class ContextAwarenessEngine
    {
        public RuntimeContext Build(AIGuideContextBuildInput input)
        {
            var serverContext = BuildServerContext(input);
            var applicationContext = BuildApplicationContext(input.Observation);
            var metricsContext = BuildMetricsContext(input.Snapshot);
            var role = ServerRoleDetector.Detect(input.Snapshot);
            var confidence = ComputeConfidence(applicationContext, serverContext, metricsContext, role);

            return new RuntimeContext
            {
                Server = serverContext,
                Application = applicationContext,
                DetectedRole = role,
                Metrics = metricsContext,
                AnalysisTimeUtc = input.AnalysisTimeUtc,
                Confidence = confidence
            };
        }

        private static ServerContext BuildServerContext(AIGuideContextBuildInput input)
        {
            var server = input.Server;
            var snap = input.Snapshot;

            var connectionType = ResolveConnectionType(input.CaptureSource, snap);
            var connectionStatus = ResolveConnectionStatus(input.RdpSessionActive, snap);

            if (server == null && snap == null)
            {
                return new ServerContext
                {
                    ConnectionType = connectionType,
                    ConnectionStatus = connectionStatus
                };
            }

            return new ServerContext
            {
                ServerName = server?.Name,
                IpAddress = FirstNonEmpty(server?.Ip, snap?.Ip, snap?.IPAddresses.FirstOrDefault()),
                Hostname = server?.Name,
                ConnectedUsername = FirstNonEmpty(server?.Username, snap?.Username, snap?.LastUser),
                Domain = snap?.DomainName,
                OperatingSystem = snap?.OSName,
                WindowsVersion = BuildWindowsVersion(snap),
                ConnectionType = connectionType,
                ConnectionStatus = connectionStatus,
                IsAvailable = server != null || snap?.Connected == true
            };
        }

        private static Models.AIGuide.ApplicationContext BuildApplicationContext(VisionObservation? observation)
        {
            if (observation == null)
            {
                return new Models.AIGuide.ApplicationContext { IsAvailable = false };
            }

            var application = string.IsNullOrWhiteSpace(observation.Application)
                ? null
                : observation.Application.Trim();

            return new Models.AIGuide.ApplicationContext
            {
                DetectedApplication = application,
                WindowTitle = string.IsNullOrWhiteSpace(observation.WindowTitle) ? null : observation.WindowTitle.Trim(),
                ScreenType = string.IsNullOrWhiteSpace(observation.ScreenType) ? null : observation.ScreenType.Trim(),
                DetectedSoftware = observation.DetectedSoftware
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                VisionConfidence = Math.Clamp(observation.Confidence, 0, 1),
                IsAvailable = !string.IsNullOrWhiteSpace(application)
                    || !string.IsNullOrWhiteSpace(observation.WindowTitle)
                    || observation.DetectedSoftware.Count > 0
            };
        }

        private static LiveMetricsContext BuildMetricsContext(WmiSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                return new LiveMetricsContext();
            }

            if (!snapshot.Connected)
            {
                return new LiveMetricsContext
                {
                    IsAvailable = true,
                    OnlineStatus = "Offline",
                    DataSource = snapshot.DataSource ?? "None",
                    LastRefreshUtc = snapshot.CollectedAt.ToUniversalTime()
                };
            }

            return new LiveMetricsContext
            {
                IsAvailable = true,
                OnlineStatus = "Online",
                CpuPercent = snapshot.CpuPct,
                RamPercent = snapshot.RamUsedPct,
                DiskPercent = snapshot.DiskUsedPct,
                Uptime = snapshot.Uptime?.ToString(@"d\.hh\:mm\:ss"),
                LastRefreshUtc = snapshot.CollectedAt.ToUniversalTime(),
                DataSource = snapshot.DataSource ?? SnapshotDataSources.Wmi
            };
        }

        private static ContextConfidence ComputeConfidence(
            Models.AIGuide.ApplicationContext application,
            ServerContext server,
            LiveMetricsContext metrics,
            DetectedServerRole role)
        {
            var visionConfidence = application.IsAvailable ? application.VisionConfidence : 0;

            var contextPoints = 0.0;
            var contextMax = 0.0;

            if (server.IsAvailable)
            {
                contextMax += 0.25;
                contextPoints += 0.25;
            }

            if (!string.IsNullOrWhiteSpace(server.ServerName))
            {
                contextMax += 0.1;
                contextPoints += 0.1;
            }

            if (!string.IsNullOrWhiteSpace(server.OperatingSystem))
            {
                contextMax += 0.15;
                contextPoints += 0.15;
            }

            if (metrics.IsAvailable && metrics.OnlineStatus == "Online")
            {
                contextMax += 0.25;
                contextPoints += 0.25;
            }
            else if (metrics.IsAvailable && metrics.OnlineStatus == "Offline")
            {
                contextMax += 0.1;
                contextPoints += 0.05;
            }

            if (role.Role != DetectedServerRoleKind.Unknown)
            {
                contextMax += 0.25;
                contextPoints += 0.25 * role.Confidence;
            }

            var contextConfidenceScore = contextMax > 0 ? contextPoints / contextMax : 0;

            var overall = metrics.IsAvailable
                ? (visionConfidence * 0.45) + (contextConfidenceScore * 0.35) + (role.Confidence * 0.2)
                : (visionConfidence * 0.65) + (contextConfidenceScore * 0.35);

            overall = Math.Clamp(overall, 0, 1);

            return new ContextConfidence
            {
                VisionConfidence = visionConfidence,
                ContextConfidenceScore = Math.Clamp(contextConfidenceScore, 0, 1),
                OverallGuidanceConfidence = overall,
                VisionLabel = ContextConfidence.ToLabel(visionConfidence),
                ContextLabel = ContextConfidence.ToLabel(contextConfidenceScore),
                OverallLabel = ContextConfidence.ToLabel(overall)
            };
        }

        private static string ResolveConnectionType(ScreenCaptureSource captureSource, WmiSnapshot? snapshot)
        {
            if (captureSource == ScreenCaptureSource.RdpSession)
                return "RDP";

            if (snapshot == null)
                return "None";

            return snapshot.DataSource switch
            {
                SnapshotDataSources.Agent => "Agent",
                SnapshotDataSources.Wmi => "WMI",
                _ when snapshot.Connected => "WMI",
                _ => "None"
            };
        }

        private static string ResolveConnectionStatus(bool rdpSessionActive, WmiSnapshot? snapshot)
        {
            if (rdpSessionActive)
                return "Connected";

            if (snapshot == null)
                return "Unknown";

            return snapshot.Connected ? "Connected" : "Offline";
        }

        private static string? BuildWindowsVersion(WmiSnapshot? snapshot)
        {
            if (snapshot == null)
                return null;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(snapshot.OSVersion))
                parts.Add(snapshot.OSVersion.Trim());
            if (!string.IsNullOrWhiteSpace(snapshot.OSBuildNumber))
                parts.Add($"Build {snapshot.OSBuildNumber.Trim()}");

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }
    }
}
