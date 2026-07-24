using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services
{
    public static class AnalysisSnapshotPersistence
    {
        public sealed class SaveOutcome
        {
            public bool Success { get; init; }
            public bool WasMerged { get; init; }
            public PersistentServerSnapshot? Snapshot { get; init; }
            public IReadOnlyList<string> SectionsWritten { get; init; } = Array.Empty<string>();
            public IReadOnlyList<string> SectionsPreserved { get; init; } = Array.Empty<string>();
        }

        public sealed class AnalysisBatchStats
        {
            public int Analyzed;
            public int Saved;
            public int Merged;
            public int Failed;
            public int RamCached;

            public void Record(SaveOutcome? outcome, bool ramCached)
            {
                Analyzed++;
                if (outcome?.Success == true)
                {
                    Saved++;
                    if (outcome.WasMerged) Merged++;
                }
                else
                {
                    Failed++;
                }

                if (ramCached) RamCached++;
            }
        }

        public static async Task<SaveOutcome> SaveMergedAsync(string serverKey, PersistentServerSnapshot incoming)
        {
            try
            {
                var sectionsWritten = AnalysisSnapshotMapper.DescribeSections(incoming);
                var existing = await PersistentSnapshotStore.LoadAsync(serverKey).ConfigureAwait(false);
                var wasMerged = existing != null;
                var preserved = wasMerged
                    ? AnalysisSnapshotMapper.DescribePreservedSections(existing!, incoming)
                    : Array.Empty<string>();

                var merged = wasMerged
                    ? SnapshotMergeHelper.MergePersistent(existing!, incoming)
                    : incoming;

                await PersistentSnapshotStore.SaveImmediatelyAsync(serverKey, merged).ConfigureAwait(false);

                var hash = PersistentSnapshotStore.HashKey(serverKey)[..12];
                var incomingProbe = SnapshotMergeHelper.IsBasicProbeSnapshot(incoming);
                var baselineFull = existing != null && SnapshotMergeHelper.IsFullSnapshot(existing);
                SnapshotInspector.LogInspect(serverKey, merged, "AnalysisSave");

                AppLogger.Debug(
                    $"Analysis snapshot saved: key=#{hash}, sections={string.Join(",", sectionsWritten)}, basicProbe={incomingProbe}",
                    category: "Snapshot",
                    action: "AnalysisSave");

                if (wasMerged && preserved.Count > 0)
                {
                    AppLogger.Debug(
                        $"Analysis snapshot merged: key=#{hash}, preserved={string.Join(",", preserved)}",
                        category: "Snapshot",
                        action: "AnalysisMerge");
                }

                if (incomingProbe && baselineFull)
                {
                    AppLogger.Debug(
                        $"Analysis snapshot patched: key=#{hash}, fullSnapshotPreserved=true",
                        category: "Snapshot",
                        action: "AnalysisPatch");
                }

                return new SaveOutcome
                {
                    Success = true,
                    WasMerged = wasMerged,
                    Snapshot = merged,
                    SectionsWritten = sectionsWritten,
                    SectionsPreserved = preserved
                };
            }
            catch (Exception ex)
            {
                var hash = PersistentSnapshotStore.HashKey(serverKey)[..12];
                AppLogger.Warning($"Analysis snapshot save failed: key=#{hash}", ex, category: "Snapshot", action: "AnalysisSave");
                return new SaveOutcome { Success = false };
            }
        }
    }
}
