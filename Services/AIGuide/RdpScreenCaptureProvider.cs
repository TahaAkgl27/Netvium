using Netvium.Helpers;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Multi-tier RDP capture: surface → PrintWindow host → container → app window → visible screen.
    /// All strategies use pre-resolved bounds/handles; capture runs off the UI thread.
    /// </summary>
    internal sealed class RdpScreenCaptureProvider
    {
        public const int PrimaryMaxAttempts = 2;
        public const int RetryDelayMs = 200;

        public const string BlankCaptureUserMessage =
            "RDP screen could not be captured. Click inside the RDP session and press Ctrl+M again.";
        public const string BlankCaptureSafeDetail =
            "The remote desktop may be minimized, covered, or not in focus. Click inside the RDP session and retry.";

        private sealed record CaptureStrategy(
            string Method,
            CapturePhysicalRect? Bounds,
            nint WindowHandle,
            bool IsPrimary,
            bool IsFallback);

        public async Task<RdpCaptureResult> CaptureWithRetriesAsync(
            ScreenCaptureOptions options,
            CancellationToken ct)
        {
            if (options.IsRdpSessionConnected?.Invoke() == false)
            {
                return RdpCaptureResult.Failure(
                    AIGuideFailureStage.Capture,
                    "RDP session is not connected.",
                    "Connect to the remote session before analyzing.",
                    options);
            }

            var strategies = BuildStrategies(options);
            if (strategies.Count == 0)
            {
                return RdpCaptureResult.Failure(
                    AIGuideFailureStage.CaptureTargetResolve,
                    "RDP capture bounds are unavailable.",
                    "Retry after the RDP session is visible.",
                    options);
            }

            RdpCaptureResult? lastResult = null;
            var pipelineAttempt = 0;

            foreach (var strategy in strategies)
            {
                ct.ThrowIfCancellationRequested();
                pipelineAttempt++;

                var maxAttempts = strategy.IsPrimary ? PrimaryMaxAttempts : 1;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();

                    var once = await Task.Run(
                        () => CaptureWithStrategy(options, strategy, pipelineAttempt, attempt),
                        ct).ConfigureAwait(false);

                    lastResult = once;

                    if (once.Success)
                    {
                        AIGuideAnalysisLogger.CaptureCompleted(
                            0,
                            once.Bounds.Width,
                            once.Bounds.Height,
                            once.PngBytes?.Length ?? 0,
                            once.Method ?? strategy.Method);
                        return once;
                    }

                    if (once.Diagnostics != null)
                    {
                        AIGuideAnalysisLogger.CaptureBlank(
                            0,
                            once.Diagnostics.BlankScore,
                            once.Method ?? strategy.Method,
                            once.Diagnostics.BlankReason);
                    }

                    if (strategy.IsFallback)
                    {
                        AIGuideAnalysisLogger.CaptureFallbackAttempted(
                            strategy.Method,
                            once.Diagnostics?.BlankScore ?? 100,
                            once.Diagnostics?.BlankReason ?? "blank");
                    }

                    if (attempt < maxAttempts)
                        await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false);
                }
            }

            AIGuideAnalysisLogger.CaptureFailed(
                AIGuideFailureStage.BlankCapture.ToString(),
                lastResult?.Method ?? "none",
                lastResult?.Diagnostics?.BlankScore ?? 100,
                timedOut: false);

            return RdpCaptureResult.AllStrategiesFailed(lastResult, options);
        }

        private static List<CaptureStrategy> BuildStrategies(ScreenCaptureOptions options)
        {
            var list = new List<CaptureStrategy>();

            if (options.ResolvedBounds != null)
            {
                list.Add(new CaptureStrategy(
                    "CopyFromScreen:RdpSurface",
                    ClipToWindow(options.ResolvedBounds.Value, options.ClipWindowBounds),
                    0,
                    IsPrimary: true,
                    IsFallback: false));
            }

            if (options.RdpHostHandle != 0)
            {
                list.Add(new CaptureStrategy(
                    "PrintWindow:RdpHost",
                    options.ResolvedBounds ?? options.ParentBounds,
                    options.RdpHostHandle,
                    IsPrimary: false,
                    IsFallback: true));
            }

            if (options.ParentBounds != null)
            {
                list.Add(new CaptureStrategy(
                    "CopyFromScreen:RdpContainer",
                    ClipToWindow(options.ParentBounds.Value, options.ClipWindowBounds),
                    0,
                    IsPrimary: false,
                    IsFallback: true));
            }

            if (options.ClipWindowBounds != null)
            {
                list.Add(new CaptureStrategy(
                    "CopyFromScreen:AppWindow",
                    options.ClipWindowBounds,
                    0,
                    IsPrimary: false,
                    IsFallback: true));
            }

            if (options.AppWindowHandle != 0)
            {
                list.Add(new CaptureStrategy(
                    "PrintWindow:AppWindow",
                    options.ClipWindowBounds,
                    options.AppWindowHandle,
                    IsPrimary: false,
                    IsFallback: true));
            }

            if (options.VisibleScreenBounds != null)
            {
                var visible = options.ClipWindowBounds != null
                    ? CaptureBoundsHelper.IntersectRects(options.VisibleScreenBounds.Value, options.ClipWindowBounds.Value)
                      ?? options.VisibleScreenBounds
                    : options.VisibleScreenBounds;

                if (visible != null)
                {
                    list.Add(new CaptureStrategy(
                        "CopyFromScreen:VisibleScreen",
                        visible,
                        0,
                        IsPrimary: false,
                        IsFallback: true));
                }
            }

            return list;
        }

        private static CapturePhysicalRect? ClipToWindow(
            CapturePhysicalRect target,
            CapturePhysicalRect? clipWindow) =>
            clipWindow == null ? target : CaptureBoundsHelper.IntersectRects(target, clipWindow.Value);

        private static RdpCaptureResult CaptureWithStrategy(
            ScreenCaptureOptions options,
            CaptureStrategy strategy,
            int pipelineAttempt,
            int attempt)
        {
            byte[]? pngBytes = null;
            CapturePhysicalRect bounds = default;

            if (strategy.Method.StartsWith("PrintWindow", StringComparison.Ordinal))
            {
                bounds = strategy.Bounds ?? default;
                pngBytes = RdpWindowCaptureHelper.TryPrintWindow(strategy.WindowHandle, strategy.Bounds);
                if (strategy.Bounds != null)
                    bounds = strategy.Bounds.Value;
                else if (pngBytes != null && bounds.Width < 8)
                    bounds = new CapturePhysicalRect(0, 0, 1280, 720);
            }
            else if (strategy.Bounds != null)
            {
                bounds = strategy.Bounds.Value;
                pngBytes = RdpWindowCaptureHelper.TryCopyFromScreen(bounds);
            }

            if (pngBytes == null || pngBytes.Length == 0)
            {
                return RdpCaptureResult.Failure(
                    AIGuideFailureStage.Capture,
                    "RDP screen capture returned no image data.",
                    "Retry after clicking inside the RDP session.",
                    options,
                    pipelineAttempt,
                    strategy.IsFallback,
                    bounds.Width >= 8 ? bounds : null,
                    strategy.Method);
            }

            var blank = CaptureImageAnalyzer.Analyze(pngBytes);
            var diagnostics = BuildDiagnostics(
                options,
                bounds,
                pngBytes,
                strategy.Method,
                pipelineAttempt,
                strategy.IsFallback,
                blank,
                failureStage: string.Empty);

            if (blank.IsBlank)
            {
                return new RdpCaptureResult
                {
                    Success = false,
                    PngBytes = pngBytes,
                    Bounds = bounds,
                    Method = strategy.Method,
                    FailureMessage = BlankCaptureUserMessage,
                    FailureStage = AIGuideFailureStage.BlankCapture,
                    Diagnostics = BuildDiagnostics(
                        options,
                        bounds,
                        pngBytes,
                        strategy.Method,
                        pipelineAttempt,
                        strategy.IsFallback,
                        blank,
                        AIGuideFailureStage.BlankCapture.ToString()),
                    AttemptCount = pipelineAttempt,
                    UsedParentBounds = strategy.IsFallback
                };
            }

            return RdpCaptureResult.FromCapture(
                pngBytes,
                bounds,
                strategy.Method,
                diagnostics,
                pipelineAttempt,
                strategy.IsFallback);
        }

        internal static AIGuideCaptureDiagnostics BuildDiagnostics(
            ScreenCaptureOptions options,
            CapturePhysicalRect rect,
            byte[] pngBytes,
            string method,
            int attempt,
            bool usedFallback,
            BlankDetectionResult blank,
            string failureStage) =>
            new()
            {
                Source = options.Source,
                Bounds = rect.Width >= 8 ? $"{rect.X},{rect.Y} {rect.Width}x{rect.Height}" : "unknown",
                DpiScale = options.DpiScale,
                Width = Math.Max(rect.Width, 0),
                Height = Math.Max(rect.Height, 0),
                Bytes = pngBytes.Length,
                DarkPercent = blank.DarkPercent,
                WhitePercent = blank.WhitePercent,
                BlankScore = blank.BlankScore,
                BlankReason = blank.Reason,
                CaptureMethod = method,
                TargetElement = string.IsNullOrWhiteSpace(options.TargetElementName)
                    ? "RdpContentGrid"
                    : options.TargetElementName,
                AttemptCount = attempt,
                UsedParentBounds = usedFallback,
                RdpState = options.RdpStateLabel,
                FallbackUsed = usedFallback,
                FailureStageLabel = failureStage
            };
    }

    internal sealed class RdpCaptureResult
    {
        public bool Success { get; init; }
        public byte[]? PngBytes { get; init; }
        public CapturePhysicalRect Bounds { get; init; }
        public string? Method { get; init; }
        public string? FailureMessage { get; init; }
        public AIGuideFailureStage FailureStage { get; init; } = AIGuideFailureStage.None;
        public AIGuideCaptureDiagnostics? Diagnostics { get; init; }
        public int AttemptCount { get; init; }
        public bool UsedParentBounds { get; init; }

        public static RdpCaptureResult FromCapture(
            byte[] png,
            CapturePhysicalRect bounds,
            string? method,
            AIGuideCaptureDiagnostics? diagnostics,
            int attempt,
            bool usedParentBounds) =>
            new()
            {
                Success = true,
                PngBytes = png,
                Bounds = bounds,
                Method = method,
                Diagnostics = diagnostics,
                AttemptCount = attempt,
                UsedParentBounds = usedParentBounds
            };

        public static RdpCaptureResult Failure(
            AIGuideFailureStage stage,
            string userMessage,
            string safeDetail,
            ScreenCaptureOptions options,
            int attempt = 0,
            bool usedParentBounds = false,
            CapturePhysicalRect? bounds = null,
            string? method = null) =>
            new()
            {
                Success = false,
                FailureMessage = userMessage,
                FailureStage = stage,
                Bounds = bounds ?? default,
                Method = method,
                AttemptCount = attempt,
                UsedParentBounds = usedParentBounds,
                Diagnostics = bounds == null
                    ? null
                    : new AIGuideCaptureDiagnostics
                    {
                        Source = options.Source,
                        Bounds = $"{bounds.Value.X},{bounds.Value.Y} {bounds.Value.Width}x{bounds.Value.Height}",
                        DpiScale = options.DpiScale,
                        Width = bounds.Value.Width,
                        Height = bounds.Value.Height,
                        CaptureMethod = method ?? "CopyFromScreen",
                        TargetElement = options.TargetElementName,
                        AttemptCount = attempt,
                        UsedParentBounds = usedParentBounds,
                        RdpState = options.RdpStateLabel,
                        FailureStageLabel = stage.ToString()
                    }
            };

        public static RdpCaptureResult AllStrategiesFailed(RdpCaptureResult? last, ScreenCaptureOptions options)
        {
            var diagnostics = last?.Diagnostics ?? new AIGuideCaptureDiagnostics
            {
                Source = options.Source,
                TargetElement = options.TargetElementName,
                CaptureMethod = last?.Method ?? "pipeline",
                AttemptCount = last?.AttemptCount ?? 0,
                UsedParentBounds = true,
                RdpState = options.RdpStateLabel,
                FallbackUsed = true,
                BlankScore = last?.Diagnostics?.BlankScore ?? 100,
                BlankReason = last?.Diagnostics?.BlankReason ?? "all-strategies-blank",
                FailureStageLabel = AIGuideFailureStage.BlankCapture.ToString()
            };

            return new RdpCaptureResult
            {
                Success = false,
                PngBytes = last?.PngBytes,
                Bounds = last?.Bounds ?? default,
                Method = last?.Method ?? "pipeline",
                FailureMessage = RdpScreenCaptureProvider.BlankCaptureUserMessage,
                FailureStage = AIGuideFailureStage.BlankCapture,
                Diagnostics = diagnostics,
                AttemptCount = last?.AttemptCount ?? 0,
                UsedParentBounds = true
            };
        }
    }
}
