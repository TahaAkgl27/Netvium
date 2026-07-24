using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services
{
    /// <summary>
    /// Serializes, debounces, and atomically writes connections.json.
    /// </summary>
    internal static class ConnectionStorageWriter
    {
        private static readonly SemaphoreSlim WriteLock = new(1, 1);
        private static readonly object DebounceGate = new();
        private static CancellationTokenSource? _debounceCts;
        private static PendingSave? _pendingSave;

        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(1000);

        private sealed class PendingSave
        {
            public required List<ServerInfo> Servers { get; init; }
            public required List<string> Folders { get; init; }
            public required ConcurrentDictionary<string, List<WmiSnapshot>> ServerHistory { get; init; }
            public IEnumerable<string>? FavoriteServerKeys { get; init; }
        }

        public static void CancelPendingDebounce()
        {
            lock (DebounceGate)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }
        }

        public static void QueueSave(
            IEnumerable<ServerInfo> servers,
            IEnumerable<string> folders,
            ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            IEnumerable<string>? favoriteServerKeys = null)
        {
            lock (DebounceGate)
            {
                _pendingSave = new PendingSave
                {
                    Servers = servers.ToList(),
                    Folders = folders.ToList(),
                    ServerHistory = serverHistory,
                    FavoriteServerKeys = favoriteServerKeys?.ToList()
                };

                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;
                _ = FlushDebouncedAsync(token);
            }
        }

        public static async Task SaveImmediatelyAsync(
            IEnumerable<ServerInfo> servers,
            IEnumerable<string> folders,
            ConcurrentDictionary<string, List<WmiSnapshot>> serverHistory,
            IEnumerable<string>? favoriteServerKeys = null)
        {
            var pending = new PendingSave
            {
                Servers = servers.ToList(),
                Folders = folders.ToList(),
                ServerHistory = serverHistory,
                FavoriteServerKeys = favoriteServerKeys?.ToList()
            };

            await WriteInternalAsync(pending).ConfigureAwait(false);
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

            PendingSave? pending;
            lock (DebounceGate)
            {
                pending = _pendingSave;
                _pendingSave = null;
            }

            if (pending == null)
                return;

            await WriteInternalAsync(pending).ConfigureAwait(false);
        }

        private static async Task WriteInternalAsync(PendingSave pending)
        {
            await WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var encryptSnapshots = SettingsService.Instance.Current.EncryptSnapshots;
                var path = StorageService.GetConnectionsPath();
                var tempPath = path + ".tmp";
                var backupPath = path + ".bak";

                var data = new PersistedData
                {
                    Servers = pending.Servers.Select(s =>
                    {
                        List<WmiSnapshot>? history = null;
                        foreach (var key in ServerSnapshotKey.HistoryLookupKeys(s))
                        {
                            if (pending.ServerHistory.TryGetValue(key, out history) && history.Count > 0)
                                break;
                        }

                        history ??= new List<WmiSnapshot>();
                        var prepared = SnapshotSecurityService.PrepareForPersistence(history, encryptSnapshots);
                        List<WmiSnapshot>? plain = encryptSnapshots ? null : prepared;
                        List<string>? encrypted = SnapshotSecurityService.EncryptSnapshotsForStorage(prepared, encryptSnapshots);

                        return new PersistedServer
                        {
                            Name = s.Name,
                            Ip = s.Ip,
                            Username = s.Username,
                            Password = StorageService.EncodeStoredPassword(s.Password),
                            Folder = s.Folder,
                            Port = s.Port,
                            Scale = s.Scale > 0 ? s.Scale : 100,
                            ReadabilityMode = s.ReadabilityMode,
                            ResolutionProfile = s.ResolutionProfile,
                            LastStatus = s.LastStatus,
                            Snapshots = plain,
                            EncryptedSnapshots = encrypted
                        };
                    }).ToList(),
                    Folders = pending.Folders.Distinct().ToList(),
                    FavoriteServerKeys = pending.FavoriteServerKeys?.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning("File.Replace failed; falling back to move.", ex, category: "Storage", action: "Save");
                        File.Copy(tempPath, path, overwrite: true);
                        try { File.Delete(tempPath); } catch (Exception delEx) { AppLogger.Error("Failed to delete temp connections file.", delEx); }
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save connections.", ex, category: "Storage", action: "Save");
            }
            finally
            {
                WriteLock.Release();
            }
        }
    }
}
