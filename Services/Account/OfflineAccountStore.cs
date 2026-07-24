using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Netvium.Helpers;
using Netvium.Models.Account;

namespace Netvium.Services.Account;

public sealed record OfflineAccountCredential(
    AccountUser User,
    string Email,
    string DisplayName,
    bool IsPendingRegistration,
    string? PendingPassword);

public interface IOfflineAccountStore
{
    Task StageRegistrationAsync(string email, string displayName, string password, CancellationToken cancellationToken = default);
    Task CacheVerifiedAsync(AccountUser user, string email, string password, CancellationToken cancellationToken = default);
    Task<OfflineAccountCredential?> VerifyAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AccountUser?> ReadUserAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<AccountUser?> ReadMostRecentUserAsync(CancellationToken cancellationToken = default);
    Task MarkSynchronizedAsync(AccountUser user, string email, string password, CancellationToken cancellationToken = default);
    Task UpdateUserAsync(AccountUser user, CancellationToken cancellationToken = default);
}

public sealed class NullOfflineAccountStore : IOfflineAccountStore
{
    public static NullOfflineAccountStore Instance { get; } = new();
    private NullOfflineAccountStore() { }
    public Task StageRegistrationAsync(string email, string displayName, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CacheVerifiedAsync(AccountUser user, string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<OfflineAccountCredential?> VerifyAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult<OfflineAccountCredential?>(null);
    public Task<AccountUser?> ReadUserAsync(string normalizedEmail, CancellationToken cancellationToken = default) => Task.FromResult<AccountUser?>(null);
    public Task<AccountUser?> ReadMostRecentUserAsync(CancellationToken cancellationToken = default) => Task.FromResult<AccountUser?>(null);
    public Task MarkSynchronizedAsync(AccountUser user, string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateUserAsync(AccountUser user, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// User-bound DPAPI cache used only when the central account database is temporarily unavailable.
/// Passwords are verified with PBKDF2. A pending registration password is retained only inside the
/// DPAPI-protected payload until it can be synchronized, then removed immediately.
/// </summary>
public sealed class DpapiOfflineAccountStore : IOfflineAccountStore
{
    private const int Iterations = 210_000;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DpapiOfflineAccountStore(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netvium", "Identity");
        _path = Path.Combine(directory, "offline-accounts.dat");
    }

    public async Task StageRegistrationAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = new AccountUser(
            Guid.NewGuid(), email.Trim(), displayName.Trim(), false,
            "FREE", "Free", "ACTIVE", null, false);
        await UpsertAsync(user, normalizedEmail, password, pendingPassword: password, cancellationToken);
    }

    public Task CacheVerifiedAsync(
        AccountUser user,
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(user, NormalizeEmail(email), password, pendingPassword: null, cancellationToken);

    public Task MarkSynchronizedAsync(
        AccountUser user,
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(user, NormalizeEmail(email), password, pendingPassword: null, cancellationToken);

    public async Task<OfflineAccountCredential?> VerifyAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken);
            var record = document.Accounts.FirstOrDefault(item => item.NormalizedEmail == normalizedEmail);
            if (record is null || !VerifyPassword(password, record))
                return null;

            return new OfflineAccountCredential(
                SanitizeForOffline(record.User),
                record.User.Email,
                record.User.DisplayName,
                !string.IsNullOrEmpty(record.PendingPassword),
                record.PendingPassword);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountUser?> ReadUserAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken);
            var record = document.Accounts.FirstOrDefault(item =>
                item.NormalizedEmail == NormalizeEmail(normalizedEmail));
            if (record is not null)
                return SanitizeForOffline(record.User);

            return document.LastSessionUser is not null
                   && NormalizeEmail(document.LastSessionUser.Email) == NormalizeEmail(normalizedEmail)
                ? SanitizeForOffline(document.LastSessionUser)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountUser?> ReadMostRecentUserAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken);
            var record = document.Accounts
                .OrderByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefault();

            if (document.LastSessionUser is not null
                && (record is null || document.LastSessionUpdatedAtUtc >= record.UpdatedAtUtc))
            {
                return SanitizeForOffline(document.LastSessionUser);
            }

            return record is null
                ? document.LastSessionUser is null
                    ? null
                    : SanitizeForOffline(document.LastSessionUser)
                : SanitizeForOffline(record.User);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateUserAsync(AccountUser user, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken);
            var index = document.Accounts.FindIndex(item => item.User.Id == user.Id);
            if (index >= 0)
                document.Accounts[index].User = user;

            // Keep a password-free, DPAPI-protected snapshot for automatic session
            // restoration when SQL is temporarily unavailable at the next startup.
            document.LastSessionUser = user;
            document.LastSessionUpdatedAtUtc = DateTime.UtcNow;
            await WriteDocumentUnsafeAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpsertAsync(
        AccountUser user,
        string normalizedEmail,
        string password,
        string? pendingPassword,
        CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt, Iterations);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken);
            document.Accounts.RemoveAll(item => item.NormalizedEmail == normalizedEmail);
            document.Accounts.Add(new StoredOfflineAccount
            {
                NormalizedEmail = normalizedEmail,
                User = user,
                PasswordSalt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash),
                PasswordIterations = Iterations,
                PendingPassword = pendingPassword,
                UpdatedAtUtc = DateTime.UtcNow
            });
            document.LastSessionUser = user;
            document.LastSessionUpdatedAtUtc = DateTime.UtcNow;
            if (document.Accounts.Count > 5)
                document.Accounts = document.Accounts.OrderByDescending(item => item.UpdatedAtUtc).Take(5).ToList();
            await WriteDocumentUnsafeAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OfflineAccountDocument> ReadDocumentUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new OfflineAccountDocument();
        try
        {
            var protectedJson = await File.ReadAllTextAsync(_path, cancellationToken);
            if (!CredentialProtector.TryUnprotect(protectedJson, out var json))
                return new OfflineAccountDocument();
            return JsonSerializer.Deserialize<OfflineAccountDocument>(json, JsonOptions)
                   ?? new OfflineAccountDocument();
        }
        catch
        {
            return new OfflineAccountDocument();
        }
    }

    private async Task WriteDocumentUnsafeAsync(
        OfflineAccountDocument document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, CredentialProtector.Protect(json), Encoding.UTF8, cancellationToken);
        File.Move(temp, _path, true);
    }

    private static bool VerifyPassword(string password, StoredOfflineAccount record)
    {
        try
        {
            var salt = Convert.FromBase64String(record.PasswordSalt);
            var expected = Convert.FromBase64String(record.PasswordHash);
            var actual = HashPassword(password, salt, record.PasswordIterations);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    private static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToUpperInvariant();

    private static AccountUser SanitizeForOffline(AccountUser user)
    {
        var expired = user.LicenseExpiresAtUtc is { } expiry && expiry <= DateTime.UtcNow;
        return user with
        {
            PlanCode = expired ? "FREE" : user.PlanCode,
            PlanDisplayName = expired ? "Free" : user.PlanDisplayName,
            LicenseStatus = expired ? "ACTIVE" : user.LicenseStatus,
            LicenseExpiresAtUtc = expired ? null : user.LicenseExpiresAtUtc,
            IsAdmin = false
        };
    }

    private sealed class OfflineAccountDocument
    {
        public List<StoredOfflineAccount> Accounts { get; set; } = [];
        public AccountUser? LastSessionUser { get; set; }
        public DateTime LastSessionUpdatedAtUtc { get; set; }
    }

    private sealed class StoredOfflineAccount
    {
        public string NormalizedEmail { get; set; } = string.Empty;
        public AccountUser User { get; set; } = new(
            Guid.Empty, string.Empty, string.Empty, false, "FREE", "Free", "ACTIVE", null, false);
        public string PasswordSalt { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public int PasswordIterations { get; set; } = Iterations;
        public string? PendingPassword { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
