using Netvium.Models.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.KnowledgePack
{
    /// <summary>Singleton host for loaded Knowledge Pack. Fail-fast on missing pack.</summary>
    public static class KnowledgePackHost
    {
        private static KnowledgePackBundle? _current;
        private static readonly object Gate = new();
        private static bool _disabled;
        private static string? _disableReason;

        public static bool IsLoaded => _current != null;
        public static bool IsDisabled => _disabled;
        public static string? DisableReason => _disableReason;

        public static KnowledgePackBundle Current
        {
            get
            {
                if (_disabled)
                    throw new InvalidOperationException(_disableReason ?? "AI Guide is disabled due to KnowledgePack error.");

                if (_current != null)
                    return _current;

                lock (Gate)
                {
                    if (_current != null)
                        return _current;

                    _current = KnowledgePackLoader.Load();
                    return _current;
                }
            }
        }

        public static void Initialize(string? rootDirectory = null)
        {
            lock (Gate)
            {
                _disabled = false;
                _disableReason = null;
                _current = KnowledgePackLoader.Load(rootDirectory);
            }
        }

        public static void Disable(string reason)
        {
            lock (Gate)
            {
                _disabled = true;
                _disableReason = reason;
                _current = null;
            }
        }

        public static void ResetForTests(KnowledgePackBundle? bundle = null)
        {
            lock (Gate)
            {
                _disabled = false;
                _disableReason = null;
                _current = bundle;
            }
        }
    }
}
