using System;
using System.Collections.Generic;
using Netvium.Models;

namespace Netvium.Helpers
{
    public sealed class NetworkThroughputTracker
    {
        private readonly Dictionary<string, NetworkCounterSample> _lastByKey = new(StringComparer.OrdinalIgnoreCase);

        public NetworkThroughputResult Record(
            string serverKey,
            ulong bytesReceived,
            ulong bytesSent,
            DateTime timestampUtc,
            bool countersAvailable)
        {
            if (!countersAvailable)
            {
                RefreshDebugLog.NetworkSample(serverKey, false, false, false, false);
                return new NetworkThroughputResult
                {
                    State = NetworkDisplayState.Unavailable,
                    CountersAvailable = false
                };
            }

            if (!_lastByKey.TryGetValue(serverKey, out var previous))
            {
                _lastByKey[serverKey] = new NetworkCounterSample(bytesReceived, bytesSent, timestampUtc);
                RefreshDebugLog.NetworkSample(serverKey, true, true, true, true);
                return new NetworkThroughputResult
                {
                    State = NetworkDisplayState.Ready,
                    DownloadBps = 0,
                    UploadBps = 0,
                    CountersAvailable = true
                };
            }

            var elapsed = (timestampUtc - previous.TimestampUtc).TotalSeconds;
            if (elapsed < 0.5)
            {
                RefreshDebugLog.NetworkSample(serverKey, true, true, false, false);
                return new NetworkThroughputResult
                {
                    State = NetworkDisplayState.Collecting,
                    CountersAvailable = true
                };
            }

            var rxDelta = Delta(previous.BytesReceived, bytesReceived);
            var txDelta = Delta(previous.BytesSent, bytesSent);
            _lastByKey[serverKey] = new NetworkCounterSample(bytesReceived, bytesSent, timestampUtc);

            var downloadBps = rxDelta / elapsed;
            var uploadBps = txDelta / elapsed;

            RefreshDebugLog.NetworkSample(
                serverKey,
                rxDelta > 0,
                txDelta > 0,
                downloadBps > 0,
                uploadBps > 0);

            return new NetworkThroughputResult
            {
                State = NetworkDisplayState.Ready,
                DownloadBps = downloadBps,
                UploadBps = uploadBps,
                CountersAvailable = true
            };
        }

        public void Reset(string? serverKey)
        {
            if (string.IsNullOrEmpty(serverKey))
            {
                _lastByKey.Clear();
                return;
            }

            _lastByKey.Remove(serverKey);
        }

        private static ulong Delta(ulong previous, ulong current) =>
            current >= previous ? current - previous : ulong.MaxValue - previous + current + 1;

        private readonly record struct NetworkCounterSample(ulong BytesReceived, ulong BytesSent, DateTime TimestampUtc);
    }

    public sealed class NetworkThroughputResult
    {
        public NetworkDisplayState State { get; init; } = NetworkDisplayState.Idle;
        public double? DownloadBps { get; init; }
        public double? UploadBps { get; init; }
        public bool CountersAvailable { get; init; }
    }
}
