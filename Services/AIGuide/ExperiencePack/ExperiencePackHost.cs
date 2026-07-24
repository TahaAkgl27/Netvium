using Netvium.Models.AIGuide.ExperiencePack;

namespace Netvium.Services.AIGuide.ExperiencePack
{
    /// <summary>Singleton host for loaded Experience Pack.</summary>
    public static class ExperiencePackHost
    {
        private static ExperiencePackBundle? _current;
        private static readonly object Gate = new();
        private static bool _disabled;
        private static string? _disableReason;

        public static bool IsLoaded => _current != null;
        public static bool IsDisabled => _disabled;
        public static string? DisableReason => _disableReason;

        public static ExperiencePackBundle Current
        {
            get
            {
                if (_disabled)
                    throw new InvalidOperationException(_disableReason ?? "AI Guide Experience Pack is disabled.");

                if (_current != null)
                    return _current;

                lock (Gate)
                {
                    if (_current != null)
                        return _current;

                    _current = ExperiencePackLoader.Load();
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
                _current = ExperiencePackLoader.Load(rootDirectory);
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

        public static void ResetForTests(ExperiencePackBundle? bundle = null)
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
