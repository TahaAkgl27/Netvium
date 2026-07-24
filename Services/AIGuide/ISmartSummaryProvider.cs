using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public interface ISmartSummaryProvider
    {
        Task<SmartScreenSummary> GenerateAsync(
            SmartSummaryRequest request,
            GuidanceProviderOptions options,
            CancellationToken cancellationToken = default);
    }
}
