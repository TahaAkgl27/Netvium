namespace Netvium.Services.AIGuide
{
    internal readonly struct OcrRegionResult
    {
        public OcrRegionResult(string text, double confidence)
        {
            Text = text ?? string.Empty;
            Confidence = Math.Clamp(confidence, 0, 1);
        }

        public string Text { get; }
        public double Confidence { get; }
    }
}
