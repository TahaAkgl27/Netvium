namespace Netvium.Models.AIGuide
{
    public sealed class VisibleTextBlock
    {
        public string Text { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }
}
