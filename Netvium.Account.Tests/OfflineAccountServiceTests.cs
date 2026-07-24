using Netvium.Models;
using Netvium.Models.Account;
using Netvium.Services.Account;
using Netvium.Services.Account.DirectSql;
using Xunit;

namespace Netvium.Account.Tests;

public sealed class OfflineAccountServiceTests
{
    private const string Email = "offline@example.test";
    private const string Password = "StrongPass123";

    [Fact]
    public async Task Register_WhenDatabaseIsUnavailable_StagesAccountAndSucceeds()
    {
        var repository = new FakeRepository
        {
            RegisterResult = DirectRepositoryResult<bool>.Failure("DATABASE_UNAVAILABLE", "SQL connection failed: secret-host")
        };
        var offlineStore = new MemoryOfflineAccountStore();
        var service = Create(repository, offlineStore);

        var result = await service.RegisterAsync(Email, "Offline User", Password);

        Assert.True(result.Succeeded);
        Assert.NotNull(await offlineStore.VerifyAsync(Email, Password));
    }

    [Fact]
    public async Task Register_WhenOffline_StillEnforcesAccountValidation()
    {
        var repository = new FakeRepository
        {
            RegisterResult = DirectRepositoryResult<bool>.Failure("DATABASE_UNAVAILABLE", "offline")
        };
        var offlineStore = new MemoryOfflineAccountStore();
        var service = Create(repository, offlineStore);

        var result = await service.RegisterAsync("not-an-email", "X", "weak");

        Assert.False(result.Succeeded);
        Assert.Equal("REGISTRATION_INVALID", result.ErrorCode);
        Assert.Null(await offlineStore.VerifyAsync("not-an-email", "weak"));
        Assert.Equal(0, repository.RegisterCalls);
    }

    [Fact]
    public async Task Login_WhenDatabaseIsUnavailable_UsesVerifiedLocalAccount()
    {
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Failure("DATABASE_UNAVAILABLE", "SQL connection failed")
        };
        var offlineStore = new MemoryOfflineAccountStore();
        await offlineStore.StageRegistrationAsync(Email, "Offline User", Password);
        var tokenStore = new MemoryTokenStore();
        var service = Create(repository, offlineStore, tokenStore);

        var result = await service.LoginAsync(Email, Password);

        Assert.True(result.Succeeded);
        Assert.NotNull(service.CurrentSession);
        Assert.False(service.CurrentSession!.User.IsAdmin);
        Assert.StartsWith("offline:", tokenStore.Value);
    }

    [Fact]
    public async Task Login_WhenDatabaseIsUnavailableAndAccountIsUnknown_HidesSqlDetails()
    {
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Failure(
                "DATABASE_UNAVAILABLE", "Server=secret-host;Password=do-not-leak")
        };
        var service = Create(repository, new MemoryOfflineAccountStore());

        var result = await service.LoginAsync(Email, Password);

        Assert.False(result.Succeeded);
        Assert.Equal("DATABASE_UNAVAILABLE", result.ErrorCode);
        Assert.DoesNotContain("secret-host", result.ErrorMessage ?? string.Empty);
        Assert.DoesNotContain("Password", result.ErrorMessage ?? string.Empty);
        Assert.Contains("çevrimdışı", result.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task PendingRegistration_WhenDatabaseReturns_IsCreatedCentrally()
    {
        var user = CreateUser(isAdmin: true);
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Failure("AUTH_INVALID_CREDENTIALS", "invalid"),
            RegisterResult = DirectRepositoryResult<bool>.Success(true),
            LoginResultAfterRegistration = DirectRepositoryResult<DirectAuthentication>.Success(
                new DirectAuthentication(user, "central-refresh", DateTime.UtcNow.AddDays(30)))
        };
        var offlineStore = new MemoryOfflineAccountStore();
        await offlineStore.StageRegistrationAsync(Email, "Offline User", Password);
        var tokenStore = new MemoryTokenStore();
        var service = Create(repository, offlineStore, tokenStore);

        var result = await service.LoginAsync(Email, Password);

        Assert.True(result.Succeeded);
        Assert.Equal(1, repository.RegisterCalls);
        Assert.Equal(2, repository.LoginCalls);
        Assert.Equal("central-refresh", tokenStore.Value);
        Assert.False((await offlineStore.VerifyAsync(Email, Password))!.IsPendingRegistration);
    }

    [Fact]
    public async Task PaidAccount_WhenOfflineAndExpired_FallsBackToFreeWithoutAdminRights()
    {
        var expiredAdmin = CreateUser(isAdmin: true) with
        {
            PlanCode = "PREMIUM",
            PlanDisplayName = "Premium",
            LicenseExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Failure("DATABASE_UNAVAILABLE", "offline")
        };
        var offlineStore = new MemoryOfflineAccountStore();
        await offlineStore.CacheVerifiedAsync(expiredAdmin, Email, Password);
        var service = Create(repository, offlineStore);

        var result = await service.LoginAsync(Email, Password);

        Assert.True(result.Succeeded);
        Assert.Equal("FREE", result.Value!.PlanCode);
        Assert.False(result.Value.IsAdmin);
    }

    [Fact]
    public async Task SessionSnapshot_PersistsUserWithoutPassword_ForOfflineStartupRestore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "netvium-account-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var user = CreateUser(isAdmin: true) with
            {
                PlanCode = "PREMIUM",
                PlanDisplayName = "Premium"
            };
            var store = new DpapiOfflineAccountStore(directory);

            await store.UpdateUserAsync(user);

            var restored = await new DpapiOfflineAccountStore(directory).ReadMostRecentUserAsync();
            Assert.NotNull(restored);
            Assert.Equal(user.Id, restored!.Id);
            Assert.Equal("PREMIUM", restored.PlanCode);
            Assert.False(restored.IsAdmin);
            Assert.True(File.Exists(Path.Combine(directory, "offline-accounts.dat")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static AccountService Create(
        FakeRepository repository,
        MemoryOfflineAccountStore offlineStore,
        MemoryTokenStore? tokenStore = null) =>
        new(repository, tokenStore ?? new MemoryTokenStore(), new FixedDeviceIdentity(), offlineStore);

    private static AccountUser CreateUser(bool isAdmin = false) => new(
        Guid.NewGuid(), Email, "Offline User", true, "FREE", "Free", "ACTIVE", null, isAdmin);

    private sealed class FakeRepository : IDirectAccountRepository
    {
        public DirectRepositoryResult<bool> RegisterResult { get; set; } = DirectRepositoryResult<bool>.Success(true);
        public DirectRepositoryResult<DirectAuthentication> LoginResult { get; set; } =
            DirectRepositoryResult<DirectAuthentication>.Failure("AUTH_INVALID_CREDENTIALS", "invalid");
        public DirectRepositoryResult<DirectAuthentication>? LoginResultAfterRegistration { get; set; }
        public int RegisterCalls { get; private set; }
        public int LoginCalls { get; private set; }

        public Task<DirectRepositoryResult<bool>> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken)
        {
            RegisterCalls++;
            return Task.FromResult(RegisterResult);
        }

        public Task<DirectRepositoryResult<DirectAuthentication>> LoginAsync(string email, string password, string deviceIdentifier, string deviceName, CancellationToken cancellationToken)
        {
            LoginCalls++;
            return Task.FromResult(LoginCalls > 1 && LoginResultAfterRegistration is not null
                ? LoginResultAfterRegistration
                : LoginResult);
        }

        public Task<DirectRepositoryResult<DirectAuthentication>> RefreshAsync(string refreshToken, string deviceIdentifier, string deviceName, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<DirectAuthentication>.Failure("DATABASE_UNAVAILABLE", "offline"));
        public Task<DirectRepositoryResult<AccountUser>> GetAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Failure("DATABASE_UNAVAILABLE", "offline"));
        public Task<DirectRepositoryResult<AccountUser>> ActivatePlanAsync(Guid userId, string planCode, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Failure("DATABASE_UNAVAILABLE", "offline"));
        public Task<DirectRepositoryResult<AdminDashboardData>> GetAdminDashboardAsync(Guid adminUserId, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AdminDashboardData>.Failure("DATABASE_UNAVAILABLE", "offline"));
        public Task<DirectRepositoryResult<AdminActivationToken>> IssueActivationTokenAsync(Guid adminUserId, string planCode, int validDays, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AdminActivationToken>.Failure("DATABASE_UNAVAILABLE", "offline"));
        public Task<DirectRepositoryResult<AccountUser>> ChangeUserPlanAsync(Guid adminUserId, Guid userId, string planCode, int? durationDays, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Failure("DATABASE_UNAVAILABLE", "offline"));
        public Task<DirectRepositoryResult<AccountUser>> RedeemActivationTokenAsync(Guid userId, string activationCode, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Failure("DATABASE_UNAVAILABLE", "offline"));
        public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryTokenStore : IRefreshTokenStore
    {
        public string? Value { get; private set; }
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task WriteAsync(string refreshToken, CancellationToken cancellationToken = default) { Value = refreshToken; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { Value = null; return Task.CompletedTask; }
    }

    private sealed class FixedDeviceIdentity : IDeviceIdentityProvider
    {
        public Task<(string Identifier, string Name)> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(("device-id", "Test Device"));
    }

    private sealed class MemoryOfflineAccountStore : IOfflineAccountStore
    {
        private readonly Dictionary<string, Entry> _accounts = new(StringComparer.OrdinalIgnoreCase);

        public Task StageRegistrationAsync(string email, string displayName, string password, CancellationToken cancellationToken = default)
        {
            var user = new AccountUser(Guid.NewGuid(), email.Trim(), displayName.Trim(), false, "FREE", "Free", "ACTIVE", null, false);
            _accounts[Normalize(email)] = new Entry(user, password, true);
            return Task.CompletedTask;
        }

        public Task CacheVerifiedAsync(AccountUser user, string email, string password, CancellationToken cancellationToken = default)
        {
            _accounts[Normalize(email)] = new Entry(user, password, false);
            return Task.CompletedTask;
        }

        public Task<OfflineAccountCredential?> VerifyAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            if (!_accounts.TryGetValue(Normalize(email), out var entry) || entry.Password != password)
                return Task.FromResult<OfflineAccountCredential?>(null);
            var user = Sanitize(entry.User);
            return Task.FromResult<OfflineAccountCredential?>(new(
                user, user.Email, user.DisplayName, entry.Pending, entry.Pending ? entry.Password : null));
        }

        public Task<AccountUser?> ReadUserAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.TryGetValue(Normalize(normalizedEmail), out var entry) ? Sanitize(entry.User) : null);

        public Task<AccountUser?> ReadMostRecentUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.Values.Select(entry => Sanitize(entry.User)).FirstOrDefault());

        public Task MarkSynchronizedAsync(AccountUser user, string email, string password, CancellationToken cancellationToken = default)
        {
            _accounts[Normalize(email)] = new Entry(user, password, false);
            return Task.CompletedTask;
        }

        public Task UpdateUserAsync(AccountUser user, CancellationToken cancellationToken = default)
        {
            var match = _accounts.FirstOrDefault(item => item.Value.User.Id == user.Id);
            if (!string.IsNullOrEmpty(match.Key))
                _accounts[match.Key] = match.Value with { User = user };
            return Task.CompletedTask;
        }

        private static string Normalize(string email) => email.Trim().ToUpperInvariant();
        private static AccountUser Sanitize(AccountUser user)
        {
            var expired = user.LicenseExpiresAtUtc is { } expiry && expiry <= DateTime.UtcNow;
            return user with
            {
                IsAdmin = false,
                PlanCode = expired ? "FREE" : user.PlanCode,
                PlanDisplayName = expired ? "Free" : user.PlanDisplayName,
                LicenseStatus = expired ? "ACTIVE" : user.LicenseStatus,
                LicenseExpiresAtUtc = expired ? null : user.LicenseExpiresAtUtc
            };
        }

        private sealed record Entry(AccountUser User, string Password, bool Pending);
    }
}
