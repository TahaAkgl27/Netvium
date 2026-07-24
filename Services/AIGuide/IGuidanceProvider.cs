using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public interface IGuidanceProvider
    {
        Task<GuidanceResponse> GenerateAsync(
            GuidanceRequest request,
            GuidanceProviderOptions options,
            CancellationToken cancellationToken = default);
    }
}
