using System;

namespace Netvium.Helpers
{
    /// <summary>
    /// Limits how often a hot-path callback may trigger UI work.
    /// </summary>
    public sealed class UiUpdateThrottle
    {
        private DateTime _lastUtc = DateTime.MinValue;
        private readonly TimeSpan _minInterval;

        public UiUpdateThrottle(TimeSpan minInterval) => _minInterval = minInterval;

        public bool ShouldUpdate()
        {
            var now = DateTime.UtcNow;
            if (now - _lastUtc < _minInterval)
                return false;

            _lastUtc = now;
            return true;
        }

        public void Reset() => _lastUtc = DateTime.MinValue;
    }
}
