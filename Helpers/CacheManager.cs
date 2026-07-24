using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Netvium.Models;
using Netvium.Services;

namespace Netvium.Helpers
{
    public static class CacheManager
    {
        private const string EncryptedPrefix = "encache:";
        private const int MaxCacheAgeDays = 7;

        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Netvium", "cache");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        static CacheManager()
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);
            }
            catch (Exception ex) { AppLogger.Error("Failed to initialize cache directory.", ex); }
        }

        public static async Task SaveAsync<T>(string key, T data)
        {
            try
            {
                var path = GetCachePath(key);
                object payload = data!;
                if (data is WmiSnapshot snap)
                    payload = SnapshotSecurityService.MaskSensitiveFields(snap);

                var json = JsonSerializer.Serialize(payload, JsonOpts);
                var protectedPayload = EncryptedPrefix + CredentialProtector.Protect(json);
                var temp = path + ".tmp";
                await File.WriteAllTextAsync(temp, protectedPayload).ConfigureAwait(false);

                if (File.Exists(path))
                    File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
                else
                    File.Move(temp, path);

                AppLogger.Info(
                    $"Cache saved (encrypted=true, format=ncache, key={MaskCacheKey(key)}, dir={CacheDir})",
                    category: "Cache",
                    action: "Save");
            }
            catch (Exception ex)
            {
                AppLogger.Error(
                    $"Cache write failed (key={MaskCacheKey(key)}).",
                    ex,
                    category: "Cache",
                    action: "Save");
            }
        }

        public static async Task<T?> LoadAsync<T>(string key)
        {
            try
            {
                var path = GetCachePath(key);
                if (!File.Exists(path))
                    return await TryLoadLegacyPlainJsonAsync<T>(key).ConfigureAwait(false);

                var content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content))
                    return default;

                if (content.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                {
                    var json = CredentialProtector.Unprotect(content.Substring(EncryptedPrefix.Length));
                    if (string.IsNullOrWhiteSpace(json))
                        return default;
                    return JsonSerializer.Deserialize<T>(json);
                }

                return JsonSerializer.Deserialize<T>(content);
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    $"Cache read failed (key={MaskCacheKey(key)}); treating as miss.",
                    ex,
                    category: "Cache",
                    action: "Load");
                return default;
            }
        }

        private static async Task<T?> TryLoadLegacyPlainJsonAsync<T>(string key)
        {
            var legacyPath = GetLegacyCachePath(key);
            if (!File.Exists(legacyPath))
                return default;

            try
            {
                var json = await File.ReadAllTextAsync(legacyPath).ConfigureAwait(false);
                var value = JsonSerializer.Deserialize<T>(json);
                if (value != null)
                    _ = SaveAsync(key, value);
                return value;
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    $"Legacy cache read failed (key={MaskCacheKey(key)}).",
                    ex,
                    category: "Cache",
                    action: "LoadLegacy");
                return default;
            }
        }

        public static void PurgeExpired()
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                    return;

                var cutoff = DateTime.UtcNow.AddDays(-MaxCacheAgeDays);
                foreach (var file in Directory.GetFiles(CacheDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc < cutoff)
                            File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"Could not purge cache file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Cache purge failed.", ex, category: "Cache", action: "Purge");
            }
        }

        private static string GetCachePath(string key) => Path.Combine(CacheDir, GetSafeFileName(key) + ".ncache");

        private static string GetLegacyCachePath(string key) => Path.Combine(CacheDir, GetSafeFileName(key) + ".json");

        private static string GetSafeFileName(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return key;
        }

        private static string MaskCacheKey(string key)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 12);
            return $"#{hash}";
        }
    }
}
