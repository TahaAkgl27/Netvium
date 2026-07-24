namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Structured observation JSON returned by the vision model (observation-only).
    /// </summary>
    public sealed class VisionObservation
    {
        public string Application { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public List<string> DetectedSoftware { get; set; } = new();
        public string ScreenType { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public List<VisibleControl> VisibleControls { get; set; } = new();
        public List<VisibleTextBlock> VisibleText { get; set; } = new();
        public List<VisibleError> VisibleErrors { get; set; } = new();
        public List<VisibleError> VisibleWarnings { get; set; } = new();
        public string CurrentTaskHint { get; set; } = string.Empty;
        public List<string> Limitations { get; set; } = new();
        public VisionSafetyInfo Safety { get; set; } = new() { ReadOnlyAnalysis = true, PerformedActions = false };
    }
}
