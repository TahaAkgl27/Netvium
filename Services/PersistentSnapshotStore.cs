using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services
{
    /// <summary>
    /// DPAPI-encrypted persistent server snapshots (%LocalAppData%\Netvium\snapshots\*.nsnap).
    /// </summary>
    public static class PersistentSnapshotStore
    {
        private const string EncryptedPrefix = "ensnap:";
        private const string FormatVersion = "1";

        private static readonly string SnapshotDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Netvium", "snapshots");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(3);

        private static readonly object DebounceGate = new();
        private static CancellationTokenSource? _debounceCts;
        private static readonly ConcurrentDictionary<string, PersistentServerSnapshot> Pending =
            new(StringComparer.OrdinalIgnoreCase);

        static PersistentSnapshotStore()
        {
            try
            {
                if (!Directory.Exists(SnapshotDir))
                    Directory.CreateDirectory(SnapshotDir);
            }
            catch (Exception ex) { AppLogger.Error("Failed to initialize snapshot directory.", ex); }
        }

        public static string HashKey(string serverKey)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serverKey))).ToLowerInvariant();
        }

        public static async Task<PersistentServerSnapshot?> LoadAsync(string serverKey)
        {
            try
            {
                var path = GetSnapshotPath(serverKey);
                if (!File.Exists(path))
                    return await TryMigrateFromLegacyAsync(serverKey, scheduleSave: true).ConfigureAwait(false);

                var content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content))
                    return null;

                string json;
                if (content.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                {
                    json = CredentialProtector.Unprotect(content.Substring(EncryptedPrefix.Length));
                    if (string.IsNullOrWhiteSpace(json))
                        return null;
                }
                else
                {
                    json = content;
                }

                var snapshot = JsonSerializer.Deserialize<PersistentServerSnapshot>(json, JsonOpts);
                if (snapshot == null)
                    return null;

                NormalizeSnapshotKind(snapshot);

                var expectedHash = HashKey(serverKey);
                if (!string.IsNullOrEmpty(snapshot.ServerKeyHash) &&
                    !string.Equals(snapshot.ServerKeyHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warning("Snapshot key hash mismatch; ignoring file.", category: "Snapshot", action: "Load");
                    return null;
                }

                AppLogger.Debug($"Snapshot loaded (key=#{expectedHash[..12]})", category: "Snapshot", action: "Load");
                SnapshotInspector.LogInspect(serverKey, snapshot, "Persistent");

                if (SnapshotMergeHelper.IsBasicProbeSnapshot(snapshot))
                {
                    var upgraded = await TryUpgradeProbeFromLegacyAsync(serverKey, snapshot).ConfigureAwait(false);
                    if (upgraded != null)
                        snapshot = upgraded;
                }

                return snapshot;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Corrupt or unreadable snapshot ignored.", ex, category: "Snapshot", action: "Load");
                return null;
            }
        }

        public static void ScheduleSave(string serverKey, PersistentServerSnapshot snapshot)
        {
            snapshot.ServerKeyHash = HashKey(serverKey);
            snapshot.LastUpdatedUtc = DateTime.UtcNow;
            snapshot.Version = FormatVersion;

            Pending.AddOrUpdate(serverKey, snapshot, (_, existing) => Merge(existing, snapshot));

            lock (DebounceGate)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;
                _ = FlushDebouncedAsync(token);
            }
        }

        public static async Task SaveImmediatelyAsync(string serverKey, PersistentServerSnapshot snapshot)
        {
            Pending.TryRemove(serverKey, out _);
            snapshot.ServerKeyHash = HashKey(serverKey);
            snapshot.LastUpdatedUtc = DateTime.UtcNow;
            snapshot.Version = FormatVersion;
            await WriteInternalAsync(serverKey, snapshot).ConfigureAwait(false);
        }

        public static bool HasSnapshotFile(string serverKey) =>
            File.Exists(GetSnapshotPath(serverKey));

        public static async Task FlushAsync()
        {
            lock (DebounceGate)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }

            foreach (var pair in Pending.ToArray())
            {
                if (Pending.TryRemove(pair.Key, out var snap))
                    await WriteInternalAsync(pair.Key, snap).ConfigureAwait(false);
            }
        }

        public static async Task<PersistentServerSnapshot?> TryLoadLegacyOnlyAsync(string serverKey)
        {
            if (HasSnapshotFile(serverKey))
                return null;
            return await TryMigrateFromLegacyAsync(serverKey, scheduleSave: true).ConfigureAwait(false);
        }

        private static async Task FlushDebouncedAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(DebounceDelay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            foreach (var pair in Pending)
            {
                if (token.IsCancellationRequested)
                    break;

                if (Pending.TryRemove(pair.Key, out var snap))
                    await WriteInternalAsync(pair.Key, snap).ConfigureAwait(false);
            }
        }

        private static async Task WriteInternalAsync(string serverKey, PersistentServerSnapshot snapshot)
        {
            try
            {
                var path = GetSnapshotPath(serverKey);
                var masked = MaskSnapshot(snapshot);
                var json = JsonSerializer.Serialize(masked, JsonOpts);
                var protectedPayload = EncryptedPrefix + CredentialProtector.Protect(json);
                var temp = path + ".tmp";

                await File.WriteAllTextAsync(temp, protectedPayload).ConfigureAwait(false);

                if (File.Exists(path))
                    File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
                else
                    File.Move(temp, path);

                AppLogger.Debug($"Snapshot saved (key=#{HashKey(serverKey)[..12]})", category: "Snapshot", action: "Save");
                SnapshotInspector.LogInspect(serverKey, snapshot, "Disk");
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Snapshot write failed.", ex, category: "Snapshot", action: "Save");
            }
        }

        private static PersistentServerSnapshot MaskSnapshot(PersistentServerSnapshot source)
        {
            return new PersistentServerSnapshot
            {
                Version = source.Version,
                ServerKeyHash = source.ServerKeyHash,
                LastUpdatedUtc = source.LastUpdatedUtc,
                SnapshotKind = source.SnapshotKind,
                SectionTimestamps = source.SectionTimestamps,
                Metrics = SnapshotSecurityService.MaskSensitiveFields(source.Metrics),
                Services = source.Services,
                Timeline = source.Timeline,
                AiInsights = source.AiInsights,
                RuleInsights = source.RuleInsights,
                SystemStatusComponents = source.SystemStatusComponents,
                DetectedRole = source.DetectedRole,
                IntelligenceConfidence = source.IntelligenceConfidence
            };
        }

        private static PersistentServerSnapshot Merge(PersistentServerSnapshot existing, PersistentServerSnapshot incoming) =>
            SnapshotMergeHelper.MergePersistentForDiskSave(existing, incoming);

        private static void NormalizeSnapshotKind(PersistentServerSnapshot snapshot)
        {
            if (SnapshotMergeHelper.IsFullSnapshot(snapshot))
                snapshot.SnapshotKind = SnapshotKinds.FullWmi;
            else if (string.IsNullOrWhiteSpace(snapshot.SnapshotKind))
                snapshot.SnapshotKind = SnapshotKinds.BasicProbe;
        }

        private static async Task<PersistentServerSnapshot?> TryUpgradeProbeFromLegacyAsync(
            string serverKey,
            PersistentServerSnapshot probeOnDisk)
        {
            if (!SnapshotMergeHelper.IsBasicProbeSnapshot(probeOnDisk)
                || SnapshotMergeHelper.IsFullSnapshot(probeOnDisk))
                return null;

            var legacy = await LoadLegacyWmiSnapshotAsync(serverKey).ConfigureAwait(false);
            if (legacy == null || !SnapshotMergeHelper.IsFullWmiMetrics(legacy))
                return null;

            var full = new PersistentServerSnapshot
            {
                SnapshotKind = SnapshotKinds.FullWmi,
                Metrics = legacy,
                Services = legacy.Services?.ToList() ?? new(),
                Timeline = legacy.Timeline?.ToList() ?? new(),
                SectionTimestamps = probeOnDisk.SectionTimestamps ?? new SnapshotSectionTimestamps
                {
                    MetricsUtc = legacy.CollectedAt.ToUniversalTime(),
                    ServicesUtc = legacy.CollectedAt.ToUniversalTime(),
                    OsInfoUtc = legacy.CollectedAt.ToUniversalTime()
                }
            };

            var merged = SnapshotMergeHelper.MergePersistent(full, probeOnDisk);
            await SaveImmediatelyAsync(serverKey, merged).ConfigureAwait(false);
            AppLogger.Debug(
                $"Probe snapshot upgraded from legacy WMI (key=#{HashKey(serverKey)[..12]})",
                category: "Snapshot",
                action: "UpgradeLegacy");
            SnapshotInspector.LogInspect(serverKey, merged, "UpgradeLegacy");
            return merged;
        }

        private static async Task<WmiSnapshot?> LoadLegacyWmiSnapshotAsync(string serverKey)
        {
            try
            {
                var parts = serverKey.Split(':');
                if (parts.Length < 3)
                    return null;

                var ip = parts[0];
                var user = parts[2];
                var legacyCacheKey = $"{ip}|{user}";
                var snap = await CacheManager.LoadAsync<WmiSnapshot>(legacyCacheKey).ConfigureAwait(false);
                if (snap != null)
                    return snap;

                return await WmiCollector.LoadFromDiskAsync(ip, user).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Legacy WMI load failed.", ex, category: "Snapshot", action: "UpgradeLegacy");
                return null;
            }
        }

        private static string GetSnapshotPath(string serverKey) =>
            Path.Combine(SnapshotDir, HashKey(serverKey) + ".nsnap");

        private static async Task<PersistentServerSnapshot?> TryMigrateFromLegacyAsync(string serverKey, bool scheduleSave)
        {
            try
            {
                var snap = await LoadLegacyWmiSnapshotAsync(serverKey).ConfigureAwait(false);
                if (snap == null)
                    return null;

                var migrated = new PersistentServerSnapshot
                {
                    SnapshotKind = SnapshotKinds.FullWmi,
                    Metrics = snap,
                    Services = snap.Services ?? new(),
                    Timeline = snap.Timeline ?? new(),
                    SectionTimestamps = new SnapshotSectionTimestamps
                    {
                        MetricsUtc = snap.CollectedAt.ToUniversalTime(),
                        ServicesUtc = snap.CollectedAt.ToUniversalTime(),
                        TimelineUtc = snap.CollectedAt.ToUniversalTime(),
                        OsInfoUtc = snap.CollectedAt.ToUniversalTime(),
                        GeneralInfoUtc = snap.CollectedAt.ToUniversalTime()
                    }
                };

                if (scheduleSave)
                    await SaveImmediatelyAsync(serverKey, migrated).ConfigureAwait(false);
                return migrated;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Legacy cache migration failed.", ex, category: "Snapshot", action: "Migrate");
            }

            return null;
        }
    }
}
