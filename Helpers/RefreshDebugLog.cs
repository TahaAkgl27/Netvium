using Netvium.Helpers.Logging;
using Netvium.Services;

namespace Netvium.Helpers
{
    internal static class RefreshDebugLog
    {
        private static string Prefix(string serverKey) =>
            $"key=#{PersistentSnapshotStore.HashKey(serverKey)[..12]}";

        public static void Started(string serverKey) =>
            AppLogger.Debug($"Refresh started: {Prefix(serverKey)}", category: "Refresh", action: "Start");

        public static void FastMetricsCompleted(string serverKey) =>
            AppLogger.Debug($"Fast metrics completed: {Prefix(serverKey)}", category: "Refresh", action: "FastMetrics");

        public static void ServicesCompleted(string serverKey) =>
            AppLogger.Debug($"Services completed: {Prefix(serverKey)}", category: "Refresh", action: "Services");

        public static void EventLogCompleted(string serverKey, bool timeout = false) =>
            AppLogger.Debug($"EventLog {(timeout ? "timeout" : "completed")}: {Prefix(serverKey)}", category: "Refresh", action: "EventLog");

        public static void AiSkipped(string serverKey, string reason) =>
            AppLogger.Debug($"AI skipped ({reason}): {Prefix(serverKey)}", category: "Refresh", action: "AiSkip");

        public static void AiUpdated(string serverKey) =>
            AppLogger.Debug($"AI updated: {Prefix(serverKey)}", category: "Refresh", action: "Ai");

        public static void SnapshotSaved(string serverKey) =>
            AppLogger.Debug($"Snapshot saved: {Prefix(serverKey)}", category: "Refresh", action: "SnapshotSave");

        public static void ProgressCompleted(string serverKey) =>
            AppLogger.Debug($"Progress completed: {Prefix(serverKey)}", category: "Refresh", action: "ProgressDone");

        public static void ProgressFailed(string serverKey) =>
            AppLogger.Debug($"Progress failed: {Prefix(serverKey)}", category: "Refresh", action: "ProgressFail");

        public static void ProgressHidden(string serverKey) =>
            AppLogger.Debug($"Progress hidden: {Prefix(serverKey)}", category: "Refresh", action: "ProgressHide");

        public static void NetworkSample(string serverKey, bool rxDelta, bool txDelta, bool downloadBps, bool uploadBps) =>
            AppLogger.Debug(
                $"Network sample: {Prefix(serverKey)}, rxDelta={rxDelta}, txDelta={txDelta}, downloadBps={downloadBps}, uploadBps={uploadBps}",
                category: "Network", action: "Sample");

        public static void ChartDraw(string serverKey, int count, int? cpu, double? ram, double? disk, double width, double height, bool draw) =>
            AppLogger.Debug(
                $"Chart sample: {Prefix(serverKey)}, count={count}, cpu={cpu?.ToString() ?? "null"}, ram={ram?.ToString("0") ?? "null"}, disk={disk?.ToString("0") ?? "null"}, width={width:0}, height={height:0}, draw={draw}",
                category: "Chart", action: "Draw");

        public static void ChartSampleAdded(string serverKey, bool cpu, bool ram, bool disk, int count) =>
            AppLogger.Debug($"Chart sample added: {Prefix(serverKey)}, cpu={cpu}, ram={ram}, disk={disk}, count={count}",
                category: "Chart", action: "Sample");

        public static void ChartUpdate(string serverKey, int samples, bool draw) =>
            AppLogger.Debug($"Chart update: {Prefix(serverKey)}, samples={samples}, draw={draw}",
                category: "Chart", action: "Update");

        public static void LiveMetricsApplied(
            string serverKey,
            bool cpu,
            bool ram,
            bool disk,
            bool network,
            bool uptime,
            bool ip,
            int chartSamples) =>
            AppLogger.Debug(
                $"Live metrics applied: {Prefix(serverKey)}, cpu={cpu}, ram={ram}, disk={disk}, network={network}, uptime={uptime}, ip={ip}, chartSamples={chartSamples}",
                category: "Dashboard", action: "LiveMetrics");

        public static void BottomCardsApplied(string serverKey, string network, string uptime, string ip) =>
            AppLogger.Debug(
                $"Bottom cards applied: {Prefix(serverKey)}, network={network}, uptime={uptime}, ip={ip}",
                category: "Dashboard", action: "BottomCards");

        public static void AiQuality(string serverKey, int inputSections, int outputCards) =>
            AppLogger.Debug($"AI quality: {Prefix(serverKey)}, inputSections={inputSections}, outputCards={outputCards}",
                category: "AI", action: "Quality");
    }
}
