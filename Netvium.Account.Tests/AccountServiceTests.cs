using Netvium.Models.Account;
using Netvium.Services.Account;
using Netvium.Services.Account.DirectSql;
using Xunit;

namespace Netvium.Account.Tests;

public sealed class AccountServiceTests
{
    private static readonly AccountUser User = new(
        Guid.NewGuid(), "user@example.test", "Test User", false, "FREE", "Free", "ACTIVE", null, false);

    [Fact]
    public async Task LoginSuccess_SetsMemorySession_AndStoresRefreshToken()
    {
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Success(
                new DirectAuthentication(User, "refresh-one", DateTime.UtcNow.AddDays(30)))
        };
        var store = new MemoryTokenStore();
        var service = Create(repository, store);
        var result = await service.LoginAsync("user@example.test", "StrongPass123");
        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(service.CurrentSession!.AccessToken));
        Assert.Equal("refresh-one", store.Value);
    }

    [Fact]
    public async Task LoginFailure_DoesNotCreateSession()
    {
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Failure("AUTH_INVALID_CREDENTIALS", "E-posta veya parola hatalı.")
        };
        var service = Create(repository, new MemoryTokenStore());
        var result = await service.LoginAsync("user@example.test", "WrongPass123");
        Assert.False(result.Succeeded);
        Assert.Null(service.CurrentSession);
        Assert.Equal("AUTH_INVALID_CREDENTIALS", result.ErrorCode);
    }

    [Fact]
    public async Task Refresh_RotatesStoredToken_AndUpdatesMemorySession()
    {
        var repository = new FakeRepository
        {
            RefreshResult = DirectRepositoryResult<DirectAuthentication>.Success(
                new DirectAuthentication(User, "refresh-two", DateTime.UtcNow.AddDays(30)))
        };
        var store = new MemoryTokenStore { Value = "refresh-one" };
        var service = Create(repository, store);
        var result = await service.RefreshAsync();
        Assert.True(result.Succeeded);
        Assert.Equal("refresh-two", store.Value);
        Assert.NotNull(service.CurrentSession);
    }

    [Fact]
    public async Task Logout_AlwaysClearsLocalSessionAndRefreshToken()
    {
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Success(
                new DirectAuthentication(User, "refresh-one", DateTime.UtcNow.AddDays(30)))
        };
        var store = new MemoryTokenStore();
        var service = Create(repository, store);
        await service.LoginAsync("user@example.test", "StrongPass123");
        await service.LogoutAsync();
        Assert.True(repository.LogoutCalled);
        Assert.Null(service.CurrentSession);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task DatabaseUnavailable_ReturnsControlledTurkishError()
    {
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Failure(
                "DATABASE_UNAVAILABLE", "NetviumIdentity veritabanına ulaşılamıyor. Sunucu bağlantı ayarlarını kontrol edin.")
        };
        var result = await Create(repository, new MemoryTokenStore()).LoginAsync("user@example.test", "StrongPass123");
        Assert.False(result.Succeeded);
        Assert.Equal("DATABASE_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("hesapla", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NetviumIdentity", result.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task Failure_DoesNotExposePasswordOrRefreshToken()
    {
        const string password = "SecretPassword123";
        const string token = "raw-refresh-token-secret";
        var repository = new FakeRepository
        {
            LoginResult = DirectRepositoryResult<DirectAuthentication>.Failure("DATABASE_UNAVAILABLE", "Bağlantı kurulamadı.")
        };
        var result = await Create(repository, new MemoryTokenStore { Value = token })
            .LoginAsync("user@example.test", password);
        Assert.DoesNotContain(password, result.ErrorMessage ?? string.Empty);
        Assert.DoesNotContain(token, result.ErrorMessage ?? string.Empty);
    }

    private static AccountService Create(FakeRepository repository, MemoryTokenStore store) =>
        new(repository, store, new FixedDeviceIdentity());

    private sealed class FakeRepository : IDirectAccountRepository
    {
        public DirectRepositoryResult<DirectAuthentication> LoginResult { get; set; } =
            DirectRepositoryResult<DirectAuthentication>.Failure("NOT_CONFIGURED", "not configured");
        public DirectRepositoryResult<DirectAuthentication> RefreshResult { get; set; } =
            DirectRepositoryResult<DirectAuthentication>.Failure("NOT_CONFIGURED", "not configured");
        public bool LogoutCalled { get; private set; }

        public Task<DirectRepositoryResult<bool>> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<bool>.Success(true));
        public Task<DirectRepositoryResult<DirectAuthentication>> LoginAsync(string email, string password, string deviceIdentifier, string deviceName, CancellationToken cancellationToken) =>
            Task.FromResult(LoginResult);
        public Task<DirectRepositoryResult<DirectAuthentication>> RefreshAsync(string refreshToken, string deviceIdentifier, string deviceName, CancellationToken cancellationToken) =>
            Task.FromResult(RefreshResult);
        public Task<DirectRepositoryResult<AccountUser>> GetAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Success(User));
        public Task<DirectRepositoryResult<AccountUser>> ActivatePlanAsync(Guid userId, string planCode, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Success(User));
        public Task<DirectRepositoryResult<AdminDashboardData>> GetAdminDashboardAsync(Guid adminUserId, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AdminDashboardData>.Failure("ADMIN_REQUIRED", "not configured"));
        public Task<DirectRepositoryResult<AdminActivationToken>> IssueActivationTokenAsync(Guid adminUserId, string planCode, int validDays, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AdminActivationToken>.Failure("ADMIN_REQUIRED", "not configured"));
        public Task<DirectRepositoryResult<AccountUser>> ChangeUserPlanAsync(Guid adminUserId, Guid userId, string planCode, int? durationDays, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Failure("ADMIN_REQUIRED", "not configured"));
        public Task<DirectRepositoryResult<AccountUser>> RedeemActivationTokenAsync(Guid userId, string activationCode, CancellationToken cancellationToken) =>
            Task.FromResult(DirectRepositoryResult<AccountUser>.Failure("TOKEN_INVALID", "not configured"));
        public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
        {
            LogoutCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryTokenStore : IRefreshTokenStore
    {
        public string? Value { get; set; }
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task WriteAsync(string refreshToken, CancellationToken cancellationToken = default) { Value = refreshToken; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { Value = null; return Task.CompletedTask; }
    }

    private sealed class FixedDeviceIdentity : IDeviceIdentityProvider
    {
        public Task<(string Identifier, string Name)> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(("device-id", "Test Device"));
    }
}
