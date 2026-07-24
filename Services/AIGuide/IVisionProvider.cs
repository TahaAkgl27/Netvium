using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public interface IVisionProvider
    {
        Task<VisionAnalysisResult> AnalyzeAsync(
            VisionRequest request,
            VisionProviderOptions options,
            CancellationToken cancellationToken = default);
    }
}
