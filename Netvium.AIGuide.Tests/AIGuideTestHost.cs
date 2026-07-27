using Netvium.Services.AIGuide.ExperiencePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.AIGuide.Tests;

public static class AIGuideTestHost
{
    static AIGuideTestHost()
    {
        try
        {
            // A different test type may initialize the composition root first. Do not
            // clear an already loaded pack because the bootstrap attempt is one-shot.
            if (!KnowledgePackHost.IsLoaded || !ExperiencePackHost.IsLoaded)
            {
                KnowledgePackHost.Initialize();
                ExperiencePackHost.Initialize();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Knowledge/Experience Pack failed to load for tests.", ex);
        }
    }

    public static void EnsureLoaded() { }
}
