using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Lightweight non-blank frame detection for RDP readiness — not used for AI Guide capture.
    /// </summary>
    internal static class RdpFrameProbe
    {
        public static Task<bool> IsNonBlankFrameAsync(Rdp.RdpWarmupSnapshot snapshot, CancellationToken ct)
        {
            if (snapshot.HostHandle == nint.Zero || !snapshot.HasNonZeroSize)
                return Task.FromResult(false);

            var probeW = Math.Min(snapshot.Width, 320);
            var probeH = Math.Min(snapshot.Height, 240);

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var bounds = new CapturePhysicalRect
                {
                    X = 0,
                    Y = 0,
                    Width = probeW,
                    Height = probeH
                };

                var bytes = RdpWindowCaptureHelper.TryPrintWindow(snapshot.HostHandle, bounds);
                if (bytes == null || bytes.Length == 0)
                    return false;

                return !CaptureImageAnalyzer.IsMostlyBlank(bytes);
            }, ct);
        }
    }
}
