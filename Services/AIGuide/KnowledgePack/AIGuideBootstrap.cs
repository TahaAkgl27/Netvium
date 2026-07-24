using Netvium.Helpers;
using Netvium.Services.AIGuide.ExperiencePack;

namespace Netvium.Services.AIGuide.KnowledgePack
{
    /// <summary>Loads and validates Knowledge Pack at startup. Fail-fast in Debug; disable in Release.</summary>
    public static class AIGuideBootstrap
    {
        private static readonly object Gate = new();
        private static bool _attempted;

        public static bool TryEnsureReady(out string? disableReason)
        {
            if (KnowledgePackHost.IsLoaded && ExperiencePackHost.IsLoaded)
            {
                disableReason = null;
                return true;
            }

            if (KnowledgePackHost.IsDisabled)
            {
                disableReason = KnowledgePackHost.DisableReason;
                return false;
            }

            if (ExperiencePackHost.IsDisabled)
            {
                disableReason = ExperiencePackHost.DisableReason;
                return false;
            }

            lock (Gate)
            {
                if (KnowledgePackHost.IsLoaded && ExperiencePackHost.IsLoaded)
                {
                    disableReason = null;
                    return true;
                }

                if (KnowledgePackHost.IsDisabled)
                {
                    disableReason = KnowledgePackHost.DisableReason;
                    return false;
                }

                if (ExperiencePackHost.IsDisabled)
                {
                    disableReason = ExperiencePackHost.DisableReason;
                    return false;
                }

                if (_attempted)
                {
                    disableReason = KnowledgePackHost.DisableReason ?? ExperiencePackHost.DisableReason;
                    return false;
                }

                _attempted = true;

                try
                {
                    KnowledgePackHost.Initialize();
                    ExperiencePackHost.Initialize();
                    disableReason = null;
                    return true;
                }
                catch (Exception ex)
                {
#if DEBUG
                    throw new InvalidOperationException(
                        "AI Guide KnowledgePack or ExperiencePack missing or invalid.",
                        ex);
#else
                    const string message = "AI Guide KnowledgePack or ExperiencePack missing or invalid.";
                    KnowledgePackHost.Disable(message);
                    ExperiencePackHost.Disable(message);
                    AppLogger.Warning(message, ex, category: "AIGuide", action: "KnowledgePack");
                    disableReason = message;
                    return false;
#endif
                }
            }
        }
    }
}
