using Netvium.Models.Account;

namespace Netvium.Services.Account.DirectSql;

/// <summary>
/// Keeps the account UI usable before a central SQL connection has been configured.
/// AccountService handles these controlled failures by using its encrypted offline store.
/// </summary>
public sealed class UnavailableAccountRepository : IDirectAccountRepository
{
    private const string Message = "Hesap veritabanı bağlantısı yapılandırılmamış.";

    public Task<DirectRepositoryResult<bool>> RegisterAsync(
        string email, string displayName, string password, CancellationToken cancellationToken) =>
        Unavailable<bool>();

    public Task<DirectRepositoryResult<DirectAuthentication>> LoginAsync(
        string email, string password, string deviceIdentifier, string deviceName, CancellationToken cancellationToken) =>
        Unavailable<DirectAuthentication>();

    public Task<DirectRepositoryResult<DirectAuthentication>> RefreshAsync(
        string refreshToken, string deviceIdentifier, string deviceName, CancellationToken cancellationToken) =>
        Unavailable<DirectAuthentication>();

    public Task<DirectRepositoryResult<AccountUser>> GetAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        Unavailable<AccountUser>();

    public Task<DirectRepositoryResult<AccountUser>> ActivatePlanAsync(
        Guid userId, string planCode, CancellationToken cancellationToken) =>
        Unavailable<AccountUser>();

    public Task<DirectRepositoryResult<AdminDashboardData>> GetAdminDashboardAsync(
        Guid adminUserId, CancellationToken cancellationToken) =>
        Unavailable<AdminDashboardData>();

    public Task<DirectRepositoryResult<AdminActivationToken>> IssueActivationTokenAsync(
        Guid adminUserId, string planCode, int planDurationDays, string? note, CancellationToken cancellationToken) =>
        Unavailable<AdminActivationToken>();

    public Task<DirectRepositoryResult<AccountUser>> ChangeUserPlanAsync(
        Guid adminUserId, Guid userId, string planCode, int? durationDays, CancellationToken cancellationToken) =>
        Unavailable<AccountUser>();

    public Task<DirectRepositoryResult<AccountUser>> RedeemActivationTokenAsync(
        Guid userId, string activationCode, CancellationToken cancellationToken) =>
        Unavailable<AccountUser>();

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;

    private static Task<DirectRepositoryResult<T>> Unavailable<T>() =>
        Task.FromResult(DirectRepositoryResult<T>.Failure("DATABASE_UNAVAILABLE", Message));
}
