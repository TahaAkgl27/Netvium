using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services
{
    public static class SnapshotSecurityService
    {
        public const int MaxSnapshotCount = 30;
        public const int MaxSnapshotAgeDays = 7;
        private const string EncryptedPrefix = "enc:";

        public static List<WmiSnapshot> PrepareForPersistence(IEnumerable<WmiSnapshot>? snapshots, bool encryptSnapshots)
        {
            if (snapshots == null)
                return new List<WmiSnapshot>();

            var now = DateTime.UtcNow;
            var ordered = snapshots
                .Where(s => s != null)
                .Where(s => (now - s.CollectedAt.ToUniversalTime()).TotalDays <= MaxSnapshotAgeDays)
                .OrderBy(s => s.CollectedAt)
                .ToList();

            while (ordered.Count > MaxSnapshotCount)
                ordered.RemoveAt(0);

            return ordered.Select(MaskSensitiveFields).ToList();
        }

        public static List<string>? EncryptSnapshotsForStorage(IReadOnlyList<WmiSnapshot> snapshots, bool encryptSnapshots)
        {
            if (!encryptSnapshots || snapshots.Count == 0)
                return null;

            var result = new List<string>(snapshots.Count);
            foreach (var snap in snapshots)
            {
                var json = JsonSerializer.Serialize(snap);
                result.Add(EncryptedPrefix + CredentialProtector.Protect(json));
            }
            return result;
        }

        public static List<WmiSnapshot> DecryptSnapshotsFromStorage(
            List<WmiSnapshot>? plain,
            List<string>? encrypted)
        {
            if (encrypted != null && encrypted.Count > 0)
            {
                var list = new List<WmiSnapshot>(encrypted.Count);
                foreach (var blob in encrypted)
                {
                    try
                    {
                        var payload = blob.StartsWith(EncryptedPrefix, StringComparison.Ordinal)
                            ? blob.Substring(EncryptedPrefix.Length)
                            : blob;
                        var json = CredentialProtector.Unprotect(payload);
                        var snap = JsonSerializer.Deserialize<WmiSnapshot>(json);
                        if (snap != null)
                            list.Add(snap);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning("Failed to decrypt WMI snapshot.", ex, category: "Snapshot", action: "Decrypt");
                    }
                }
                return ApplyRetention(list);
            }

            return ApplyRetention(plain ?? new List<WmiSnapshot>());
        }

        public static List<WmiSnapshot> ApplyRetention(IEnumerable<WmiSnapshot> snapshots)
        {
            var now = DateTime.UtcNow;
            var list = snapshots
                .Where(s => (now - s.CollectedAt.ToUniversalTime()).TotalDays <= MaxSnapshotAgeDays)
                .OrderBy(s => s.CollectedAt)
                .ToList();

            while (list.Count > MaxSnapshotCount)
                list.RemoveAt(0);

            return list;
        }

        public static void TrimHistoryInPlace(List<WmiSnapshot> history)
        {
            if (history == null || history.Count == 0)
                return;

            var trimmed = ApplyRetention(history);
            history.Clear();
            history.AddRange(trimmed);
        }

        public static WmiSnapshot MaskSensitiveFields(WmiSnapshot source)
        {
            var copy = JsonSerializer.Deserialize<WmiSnapshot>(JsonSerializer.Serialize(source))
                       ?? new WmiSnapshot();

            copy.Username = MaskIdentity(copy.Username);
            copy.DomainName = MaskIdentity(copy.DomainName);
            copy.LastUser = MaskIdentity(copy.LastUser);
            copy.LastLogon = MaskGeneric(copy.LastLogon);
            copy.LastSession = MaskGeneric(copy.LastSession);

            copy.Sessions = copy.Sessions
                .Select(s => s with
                {
                    User = MaskIdentity(s.User) ?? "user",
                    Logon = MaskGeneric(s.Logon) ?? s.Logon
                })
                .ToList();

            copy.TopProcesses = copy.TopProcesses
                .Select(p => p with { User = MaskIdentity(p.User) ?? "user" })
                .ToList();

            copy.Timeline = copy.Timeline
                .Select(t => t with { Message = RedactMessage(t.Message) })
                .ToList();

            return copy;
        }

        private static string? MaskIdentity(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (value.Contains('\\'))
            {
                var parts = value.Split('\\', 2);
                return $"{MaskToken(parts[0])}\\{MaskToken(parts[1])}";
            }

            if (value.Contains('@'))
            {
                var parts = value.Split('@', 2);
                return $"{MaskToken(parts[0])}@{parts[1]}";
            }

            return MaskToken(value);
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "***";
            if (token.Length <= 2)
                return "***";
            return token[0] + new string('*', Math.Min(token.Length - 1, 6));
        }

        private static string? MaskGeneric(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            return "***";
        }

        private static string RedactMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            var redacted = Regex.Replace(message, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", "***@***");
            redacted = Regex.Replace(redacted, @"\b[A-Za-z0-9_\\.-]{3,}\b", m =>
            {
                var v = m.Value;
                if (v.Contains('\\'))
                    return MaskIdentity(v) ?? "***";
                return v.Length > 3 ? v[..2] + "***" : "***";
            });
            return redacted;
        }
    }
}
