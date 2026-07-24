using Netvium.Models;
using Netvium.Models.Account;

namespace Netvium.Services.Account;

public interface IAccountService
{
    AccountSession? CurrentSession { get; }
    Task<AccountResult<bool>> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountUser>> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountUser>> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
    Task<AccountResult<AccountUser>> RefreshAsync(CancellationToken cancellationToken = default);
    Task<AccountResult<AccountUser>> GetMeAsync(CancellationToken cancellationToken = default);
    Task<AccountResult<AccountUser>> ActivatePlanAsync(LicensingPlanTier plan, CancellationToken cancellationToken = default);
    Task<AccountResult<AdminDashboardData>> GetAdminDashboardAsync(CancellationToken cancellationToken = default);
    Task<AccountResult<AdminActivationToken>> IssueActivationTokenAsync(LicensingPlanTier plan, int validDays, string? note, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountUser>> ChangeUserPlanAsync(Guid userId, LicensingPlanTier plan, int? durationDays, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountUser>> RedeemActivationTokenAsync(string activationCode, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public interface IRefreshTokenStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IDeviceIdentityProvider
{
    Task<(string Identifier, string Name)> GetAsync(CancellationToken cancellationToken = default);
}
