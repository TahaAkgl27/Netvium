using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Netvium.Models;
using Netvium.Helpers;
using Netvium.Rdp;

namespace Netvium.Services
{
    public static class StorageService
    {
        internal const string PasswordPrefix = "dpapi:";

        public static string GetConnectionsPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Netvium");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "connections.json");
        }

        public static async Task<(List<ServerInfo> Servers, List<string> Folders, HashSet<string> Favorites)> LoadConnectionsAsync(ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory)
        {
            var servers = new List<ServerInfo>();
            var folders = new List<string>();
            var favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var encryptSnapshots = SettingsService.Instance.Current.EncryptSnapshots;
            var encryptCredentials = SettingsService.Instance.Current.EncryptedCredentialStorage;
            var needsPasswordMigration = false;

            try
            {
                var path = GetConnectionsPath();
                if (!File.Exists(path))
                    return (servers, folders, favorites);

                var json = await ReadConnectionsJsonAsync(path).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return (servers, folders, favorites);

                try
                {
                    var data = JsonSerializer.Deserialize<PersistedData>(json);
                    if (data != null)
                    {
                        if (data.Servers != null)
                        {
                            foreach (var it in data.Servers)
                            {
                                if (string.IsNullOrWhiteSpace(it.Name) || string.IsNullOrWhiteSpace(it.Ip)) continue;

                                var storedPassword = it.Password ?? "";
                                var decodeResult = DecodeStoredPasswordResult(storedPassword);
                                if (encryptCredentials && IsLegacyPlaintextPassword(storedPassword))
                                    needsPasswordMigration = true;

                                var s = new ServerInfo(it.Name, it.Ip, it.Username ?? "", decodeResult.Password, it.Folder ?? "Sahsi", it.Port);
                                s.StoredPasswordUnavailable = decodeResult.DecodeFailed;
                                s.LastStatus = it.LastStatus;
                                s.ReadabilityMode = it.ReadabilityMode;
                                s.ResolutionProfile = it.ReadabilityMode != RdpReadabilityMode.Comfort
                                    ? RdpViewportSizing.MigrateFromReadabilityMode(it.ReadabilityMode)
                                    : it.ResolutionProfile;
                                if (it.Scale > 0) s.Scale = it.Scale;
                                servers.Add(s);

                                var snapshots = SnapshotSecurityService.DecryptSnapshotsFromStorage(
                                    it.Snapshots,
                                    encryptSnapshots ? it.EncryptedSnapshots : null);

                                if (snapshots.Count > 0)
                                {
                                    var canonicalKey = ServerSnapshotKey.FromServer(s);
                                    var history = serverHistory.GetOrAdd(canonicalKey, _ => new List<WmiSnapshot>());
                                    lock (history)
                                    {
                                        history.Clear();
                                        history.AddRange(snapshots);
                                    }
                                }
                            }
                        }
                        if (data.Folders != null) folders.AddRange(data.Folders);
                        if (data.FavoriteServerKeys != null)
                        {
                            foreach (var key in data.FavoriteServerKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
                                favorites.Add(key);
                        }

                        if (needsPasswordMigration && servers.Count > 0)
                        {
                            await ConnectionStorageWriter.SaveImmediatelyAsync(servers, folders, serverHistory, favorites)
                                .ConfigureAwait(false);
                        }

                        return (servers, folders, favorites);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning("Failed to parse connections.json (new format).", ex,
                        category: "Storage", action: "Load");
                }

                try
                {
                    var items = JsonSerializer.Deserialize<PersistedServer[]>(json) ?? Array.Empty<PersistedServer>();
                    foreach (var it in items)
                    {
                        if (string.IsNullOrWhiteSpace(it.Name) || string.IsNullOrWhiteSpace(it.Ip)) continue;

                        var storedPassword = it.Password ?? "";
                        if (encryptCredentials && IsLegacyPlaintextPassword(storedPassword))
                            needsPasswordMigration = true;

                        var decodeResult = DecodeStoredPasswordResult(storedPassword);
                        var s = new ServerInfo(it.Name, it.Ip, it.Username ?? "", decodeResult.Password, it.Folder ?? "Sahsi", it.Port);
                        s.StoredPasswordUnavailable = decodeResult.DecodeFailed;
                        s.LastStatus = it.LastStatus;
                        servers.Add(s);
                    }

                    if (needsPasswordMigration && servers.Count > 0)
                    {
                        await ConnectionStorageWriter.SaveImmediatelyAsync(servers, folders, serverHistory, favorites)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to parse connections.json (legacy format).", ex, category: "Storage", action: "Load");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load connections.", ex, category: "Storage", action: "Load");
            }

            return (servers, folders, favorites);
        }

        private static async Task<string?> ReadConnectionsJsonAsync(string path)
        {
            try
            {
                return await File.ReadAllTextAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("connections.json unreadable; trying backup.", ex, category: "Storage", action: "Load");
            }

            var backupPath = path + ".bak";
            if (!File.Exists(backupPath))
                return null;

            try
            {
                AppLogger.Warning("Loading connections from backup file.", category: "Storage", action: "LoadBackup");
                return await File.ReadAllTextAsync(backupPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load connections backup.", ex, category: "Storage", action: "LoadBackup");
                return null;
            }
        }

        public static Task SaveConnectionsAsync(
            IEnumerable<ServerInfo> servers,
            IEnumerable<string> folders,
            ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            IEnumerable<string>? favoriteServerKeys = null)
        {
            ConnectionStorageWriter.QueueSave(servers, folders, serverHistory, favoriteServerKeys);
            return Task.CompletedTask;
        }

        public static Task SaveConnectionsImmediatelyAsync(
            IEnumerable<ServerInfo> servers,
            IEnumerable<string> folders,
            ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            IEnumerable<string>? favoriteServerKeys = null)
        {
            return ConnectionStorageWriter.SaveImmediatelyAsync(servers, folders, serverHistory, favoriteServerKeys);
        }

        public static async Task FlushConnectionsOnShutdownAsync(
            IEnumerable<ServerInfo> servers,
            IEnumerable<string> folders,
            ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            IEnumerable<string>? favoriteServerKeys = null)
        {
            ConnectionStorageWriter.CancelPendingDebounce();
            await SaveConnectionsImmediatelyAsync(servers, folders, serverHistory, favoriteServerKeys).ConfigureAwait(false);
        }

        internal static string EncodeStoredPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return password;

            if (!SettingsService.Instance.Current.EncryptedCredentialStorage)
                return password;

            return PasswordPrefix + CredentialProtector.Protect(password);
        }

        internal static string DecodeStoredPassword(string stored) =>
            DecodeStoredPasswordResult(stored).Password;

        internal sealed class StoredPasswordDecodeResult
        {
            public string Password { get; init; } = string.Empty;
            public bool DecodeFailed { get; init; }
        }

        internal static StoredPasswordDecodeResult DecodeStoredPasswordResult(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return new StoredPasswordDecodeResult { Password = stored };

            if (!stored.StartsWith(PasswordPrefix, StringComparison.Ordinal))
                return new StoredPasswordDecodeResult { Password = stored };

            var payload = stored.Substring(PasswordPrefix.Length);
            if (CredentialProtector.TryUnprotect(payload, out var plain) && !string.IsNullOrEmpty(plain))
                return new StoredPasswordDecodeResult { Password = plain };

            AppLogger.Warning(
                "Stored password could not be decrypted for a saved connection.",
                category: "Storage",
                action: "DecodePassword");

            return new StoredPasswordDecodeResult { DecodeFailed = true };
        }

        internal static bool IsLegacyPlaintextPassword(string stored) =>
            !string.IsNullOrEmpty(stored) && !stored.StartsWith(PasswordPrefix, StringComparison.Ordinal);
    }

    public class PersistedData
    {
        public List<PersistedServer>? Servers { get; set; }
        public List<string>? Folders { get; set; }
        public List<string>? FavoriteServerKeys { get; set; }
    }

    public class PersistedServer
    {
        public string? Name { get; set; }
        public string? Ip { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Folder { get; set; }
        public int Port { get; set; }
        public int Scale { get; set; } = 100;
        public RdpReadabilityMode ReadabilityMode { get; set; } = RdpReadabilityMode.Comfort;
        public Rdp.RdpResolutionProfile ResolutionProfile { get; set; } = Rdp.RdpResolutionProfile.Balanced;
        public HealthBadge LastStatus { get; set; }
        public List<WmiSnapshot>? Snapshots { get; set; }
        public List<string>? EncryptedSnapshots { get; set; }
    }
}
