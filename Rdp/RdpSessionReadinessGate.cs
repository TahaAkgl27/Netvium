namespace Netvium.Rdp
{
    public enum RdpReadinessPhase
    {
        Disconnected,
        Connecting,
        Connected,
        FirstFrameReady,
        StableReady,
        Cancelled
    }

    /// <summary>
    /// Tracks RDP connection lifecycle. Session usability is granted on connect;
    /// optional frame capture probe only affects AI Guide capture readiness.
    /// </summary>
    public sealed class RdpSessionReadinessGate
    {
        public static readonly TimeSpan ConnectStartDebounce = TimeSpan.FromMilliseconds(300);
        public static readonly TimeSpan PostFirstFrameDebounce = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan MaxFrameWait = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

        public RdpReadinessPhase Phase { get; private set; } = RdpReadinessPhase.Disconnected;

        public DateTime? ConnectionStartedUtc { get; private set; }

        public DateTime? FirstNonBlankFrameUtc { get; private set; }

        public bool IsStableReady => Phase == RdpReadinessPhase.StableReady;

        public bool IsReadyForCapture => IsStableReady;

        public bool IsSessionUsable =>
            Phase == RdpReadinessPhase.Connected
            || Phase == RdpReadinessPhase.FirstFrameReady
            || Phase == RdpReadinessPhase.StableReady;

        public bool IsWarmingUp => Phase == RdpReadinessPhase.Connecting;

        public bool BlocksAiGuide => !IsReadyForCapture && IsWarmingUp;

        public bool BlocksCaptureDebounce =>
            ConnectionStartedUtc.HasValue
            && DateTime.UtcNow - ConnectionStartedUtc.Value < ConnectStartDebounce;

        public void MarkConnecting()
        {
            ConnectionStartedUtc = DateTime.UtcNow;
            FirstNonBlankFrameUtc = null;
            Phase = RdpReadinessPhase.Connecting;
        }

        public void MarkConnected(string? host) => MarkSessionUsable(host);

        public void MarkSessionUsable(string? host)
        {
            Phase = RdpReadinessPhase.Connected;
            RdpConnectionLogger.RdpConnectedEvent(host);
        }

        public void MarkCancelled()
        {
            Phase = RdpReadinessPhase.Cancelled;
        }

        public void MarkStableReady(string? host)
        {
            Phase = RdpReadinessPhase.StableReady;
            RdpConnectionLogger.StableReadyReached(host);
            RdpConnectionLogger.AiGuideResumed(host);
        }

        public void Reset()
        {
            Phase = RdpReadinessPhase.Disconnected;
            ConnectionStartedUtc = null;
            FirstNonBlankFrameUtc = null;
        }

        /// <summary>
        /// Optional background probe for AI Guide capture. Never affects session usability.
        /// </summary>
        public async Task<bool> TryCaptureProbeAsync(
            string? host,
            Func<Task<RdpWarmupSnapshot>> readSnapshotAsync,
            Func<RdpWarmupSnapshot, Task<bool>> probeNonBlankFrameAsync,
            CancellationToken ct)
        {
            RdpConnectionLogger.RdpReadyWaitStarted(host);

            var waitStarted = DateTime.UtcNow;

            while (DateTime.UtcNow - waitStarted < MaxFrameWait)
            {
                ct.ThrowIfCancellationRequested();

                var snap = await readSnapshotAsync().ConfigureAwait(false);

                if (ConnectionStartedUtc.HasValue
                    && DateTime.UtcNow - ConnectionStartedUtc.Value < ConnectStartDebounce)
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    continue;
                }

                if (!IsVisiblyConnected(snap))
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    continue;
                }

                if (!FirstNonBlankFrameUtc.HasValue)
                {
                    var nonBlank = await probeNonBlankFrameAsync(snap).ConfigureAwait(false);
                    if (!nonBlank)
                    {
                        await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                        continue;
                    }

                    FirstNonBlankFrameUtc = DateTime.UtcNow;
                    Phase = RdpReadinessPhase.FirstFrameReady;
                    RdpConnectionLogger.FirstFrameDetected(host);
                    RdpConnectionLogger.FirstNonBlankFrameDetected(host);
                }

                if (PostFirstFrameDebounce > TimeSpan.Zero
                    && DateTime.UtcNow - FirstNonBlankFrameUtc!.Value < PostFirstFrameDebounce)
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    continue;
                }

                MarkStableReady(host);
                RdpConnectionLogger.FirstFrameReady(host);
                RdpConnectionLogger.AiGuideEnabled(host);
                return true;
            }

            RdpConnectionLogger.RdpReadyTimeout(host);
            return false;
        }

        private static bool IsVisiblyConnected(RdpWarmupSnapshot snap) =>
            snap.IsConnected
            && snap.HasNonZeroSize
            && snap.IsVisible
            && (snap.State == RdpState.LoginComplete || snap.State == RdpState.Connected);
    }

    public readonly struct RdpWarmupSnapshot
    {
        public bool IsConnected { get; init; }
        public RdpState State { get; init; }
        public int? ClientState { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public bool IsVisible { get; init; }
        public bool HasHostFocus { get; init; }
        public nint HostHandle { get; init; }

        public bool HasNonZeroSize => Width >= 200 && Height >= 200;
    }
}
