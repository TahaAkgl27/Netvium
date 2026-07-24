using Netvium.Services.AIGuide.ExperiencePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.AIGuide.Tests;

public static class AIGuideTestHost
{
    static AIGuideTestHost()
    {
        KnowledgePackHost.ResetForTests(null);
        ExperiencePackHost.ResetForTests(null);
        if (!AIGuideBootstrap.TryEnsureReady(out var reason))
            throw new InvalidOperationException(reason ?? "Knowledge/Experience Pack failed to load for tests.");
    }

    public static void EnsureLoaded() { }
}
