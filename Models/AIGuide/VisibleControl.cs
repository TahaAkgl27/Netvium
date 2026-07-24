namespace Netvium.Models.AIGuide
{
    public sealed class VisibleControl
    {
        public string Type { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }
}
