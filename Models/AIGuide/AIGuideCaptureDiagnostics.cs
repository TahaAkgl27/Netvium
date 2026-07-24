namespace Netvium.Models.AIGuide
{
    public sealed class AIGuideCaptureDiagnostics
    {
        public ScreenCaptureSource Source { get; init; }
        public string Bounds { get; init; } = string.Empty;
        public double DpiScale { get; init; } = 1.0;
        public int Width { get; init; }
        public int Height { get; init; }
        public int Bytes { get; init; }
        public double DarkPercent { get; init; }
        public double WhitePercent { get; init; }
        public string CaptureMethod { get; init; } = string.Empty;
        public string TargetElement { get; init; } = string.Empty;
        public int AttemptCount { get; init; }
        public bool UsedParentBounds { get; init; }
        public bool FallbackUsed { get; init; }
        public string RdpState { get; init; } = string.Empty;
        public double BlankScore { get; init; }
        public string BlankReason { get; init; } = string.Empty;
        public string FailureStageLabel { get; init; } = string.Empty;

        public string ToDiagnosticText() =>
            $"Source: {Source}\r\n" +
            $"Target: {TargetElement}\r\n" +
            $"Bounds: {Bounds}\r\n" +
            $"DPI scale: {DpiScale:0.##}\r\n" +
            $"Size: {Width}x{Height}\r\n" +
            $"Bytes: {Bytes}\r\n" +
            $"Dark/black: {DarkPercent:0.#}%\r\n" +
            $"White: {WhitePercent:0.#}%\r\n" +
            $"Blank score: {BlankScore:0.#}\r\n" +
            $"Blank reason: {BlankReason}\r\n" +
            $"Method: {CaptureMethod}\r\n" +
            $"Attempts: {AttemptCount}\r\n" +
            $"Parent bounds fallback: {(UsedParentBounds ? "yes" : "no")}\r\n" +
            $"Fallback used: {(FallbackUsed ? "yes" : "no")}\r\n" +
            $"Failure stage: {FailureStageLabel}\r\n" +
            $"RDP state: {RdpState}";
    }
}
