namespace Netvium.Models.AIGuide
{
    public sealed class VisibleError
    {
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }
}
