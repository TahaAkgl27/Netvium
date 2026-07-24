using System.Net.Mail;
using System.Security.Cryptography;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Models.Account;
using Netvium.Services.Account.DirectSql;

namespace Netvium.Services.Account;

public sealed class AccountService(
    IDirectAccountRepository repository,
    IRefreshTokenStore refreshTokenStore,
    IDeviceIdentityProvider deviceIdentityProvider,
    IOfflineAccountStore? offlineAccountStore = null) : IAccountService
{
    private const string OfflineRefreshPrefix = "offline:";
    private const string OfflineSessionPrefix = "offline-session:";
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly IOfflineAccountStore _offlineStore = offlineAccountStore ?? NullOfflineAccountStore.Instance;
    public AccountSession? CurrentSession { get; private set; }

    public async Task<AccountResult<bool>> RegisterAsync(
        string email, string displayName, string password, CancellationToken cancellationToken = default)
    {
        if (!RegistrationIsValid(email, displayName, password))
            return AccountResult<bool>.Failure("REGISTRATION_INVALID",
                "Bilgileri kontrol edin. Parola en az 10 karakter, büyük/küçük harf ve rakam içermelidir.", AccountResultStatus.ValidationFailed);

        var result = await repository.RegisterAsync(email, displayName, password, cancellationToken);
        if (result.Succeeded)
            return AccountResult<bool>.Success(true, AccountResultStatus.CreatedOnline);

        if (result.ErrorCode == "DATABASE_UNAVAILABLE")
        {
            await _offlineStore.StageRegistrationAsync(email, displayName, password, cancellationToken);
            return AccountResult<bool>.Success(true, AccountResultStatus.StagedOffline);
        }

        var status = result.ErrorCode == "EMAIL_ALREADY_REGISTERED" ? AccountResultStatus.AlreadyExists : AccountResultStatus.Failed;
        return AccountResult<bool>.Failure(result.ErrorCode!, SafeFailureMessage(result.ErrorCode, result.ErrorMessage), status);
    }
    public async Task<AccountResult<AccountUser>> LoginAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        var device = await deviceIdentityProvider.GetAsync(cancellationToken);
        var cached = await _offlineStore.VerifyAsync(email, password, cancellationToken);
        var result = await repository.LoginAsync(email, password, device.Identifier, device.Name, cancellationToken);

        // A registration created while offline is synchronized on the first online login.
        if (!result.Succeeded
            && result.ErrorCode == "AUTH_INVALID_CREDENTIALS"
            && cached is { IsPendingRegistration: true, PendingPassword: not null })
        {
            var registration = await repository.RegisterAsync(
                cached.Email, cached.DisplayName, cached.PendingPassword, cancellationToken);
            if (registration.Succeeded || registration.ErrorCode == "EMAIL_ALREADY_REGISTERED")
                result = await repository.LoginAsync(email, password, device.Identifier, device.Name, cancellationToken);
            else if (registration.ErrorCode == "DATABASE_UNAVAILABLE")
                return await AcceptOfflineAuthenticationAsync(cached.User, cached.Email, cancellationToken);
        }

        if (result.Succeeded)
        {
            var accepted = await AcceptAuthenticationAsync(result.Value!, cancellationToken);
            if (accepted.Succeeded)
                await _offlineStore.MarkSynchronizedAsync(result.Value!.User, email, password, cancellationToken);
            return accepted;
        }

        if (result.ErrorCode == "DATABASE_UNAVAILABLE" && cached is not null)
            return await AcceptOfflineAuthenticationAsync(cached.User, cached.Email, cancellationToken);

        return Failure(result);
    }

    public async Task<AccountResult<AccountUser>> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var token = await refreshTokenStore.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return AccountResult<AccountUser>.Failure("SESSION_NOT_FOUND", "Kayıtlı oturum bulunamadı.");

        if (token.StartsWith(OfflineRefreshPrefix, StringComparison.Ordinal))
        {
            var email = token[OfflineRefreshPrefix.Length..];
            var user = await _offlineStore.ReadUserAsync(email, cancellationToken);
            return user is null
                ? await ClearAndFailAsync("SESSION_NOT_FOUND", "Kayıtlı çevrimdışı oturum bulunamadı.", cancellationToken)
                : await AcceptOfflineAuthenticationAsync(user, email, cancellationToken);
        }

        return await RefreshCoreAsync(token, cancellationToken);
    }

    public async Task<AccountResult<AccountUser>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsOfflineSession())
        {
            var session = CurrentSession!;
            CurrentSession = session with { AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(12) };
            return AccountResult<AccountUser>.Success(CurrentSession.User);
        }

        var token = await refreshTokenStore.ReadAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(token)
            ? await ClearAndFailAsync("SESSION_NOT_FOUND", "Oturumunuz sona erdi.", cancellationToken)
            : await RefreshCoreAsync(token, cancellationToken);
    }

    public async Task<AccountResult<AccountUser>> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null)
            return AccountResult<AccountUser>.Failure("AUTH_REQUIRED", "Oturum gerekli.");
        if (IsOfflineSession(session))
            return AccountResult<AccountUser>.Success(session.User);
        if (session.AccessTokenExpiresAtUtc <= DateTime.UtcNow.AddSeconds(30))
            return await RefreshAsync(cancellationToken);

        var result = await repository.GetAccountAsync(session.User.Id, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.ErrorCode == "DATABASE_UNAVAILABLE")
                return AccountResult<AccountUser>.Success(session.User);
            if (result.ErrorCode == "ACCOUNT_NOT_FOUND")
                return await ClearAndFailAsync("AUTH_REQUIRED", "Oturumunuz sona erdi.", cancellationToken);
            return Failure(result);
        }
        CurrentSession = session with { User = result.Value! };
        await _offlineStore.UpdateUserAsync(result.Value!, cancellationToken);
        return AccountResult<AccountUser>.Success(result.Value!);
    }

    public async Task<AccountResult<AccountUser>> ActivatePlanAsync(
        LicensingPlanTier plan, CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null)
            return AccountResult<AccountUser>.Failure("AUTH_REQUIRED", "Plan etkinleştirmek için giriş yapın.");
        if (IsOfflineSession(session)) return OfflineWriteFailure();

        var planCode = plan switch
        {
            LicensingPlanTier.Premium => "PREMIUM",
            LicensingPlanTier.PremiumPlus => "PREMIUM_PLUS",
            _ => "FREE"
        };
        if (planCode == "FREE")
            return AccountResult<AccountUser>.Failure("PLAN_INVALID", "Geçerli bir ücretli plan seçilmedi.");

        var result = await repository.ActivatePlanAsync(session.User.Id, planCode, cancellationToken);
        if (!result.Succeeded) return Failure(result);
        CurrentSession = session with { User = result.Value! };
        await _offlineStore.UpdateUserAsync(result.Value!, cancellationToken);
        return AccountResult<AccountUser>.Success(result.Value!);
    }

    public async Task<AccountResult<AdminDashboardData>> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null || !session.User.IsAdmin || IsOfflineSession(session))
            return AccountResult<AdminDashboardData>.Failure("ADMIN_REQUIRED", "Bu işlem için çevrimiçi yönetici oturumu gerekli.");
        var result = await repository.GetAdminDashboardAsync(session.User.Id, cancellationToken);
        return result.Succeeded
            ? AccountResult<AdminDashboardData>.Success(result.Value!)
            : AccountResult<AdminDashboardData>.Failure(result.ErrorCode!, SafeFailureMessage(result.ErrorCode, result.ErrorMessage));
    }

    public async Task<AccountResult<AdminActivationToken>> IssueActivationTokenAsync(
        LicensingPlanTier plan, int validDays, string? note, CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null || !session.User.IsAdmin || IsOfflineSession(session))
            return AccountResult<AdminActivationToken>.Failure("ADMIN_REQUIRED", "Bu işlem için çevrimiçi yönetici oturumu gerekli.");
        var code = plan == LicensingPlanTier.PremiumPlus ? "PREMIUM_PLUS" : "PREMIUM";
        var result = await repository.IssueActivationTokenAsync(session.User.Id, code, validDays, note, cancellationToken);
        return result.Succeeded
            ? AccountResult<AdminActivationToken>.Success(result.Value!)
            : AccountResult<AdminActivationToken>.Failure(result.ErrorCode!, SafeFailureMessage(result.ErrorCode, result.ErrorMessage));
    }

    public async Task<AccountResult<AccountUser>> ChangeUserPlanAsync(
        Guid userId, LicensingPlanTier plan, int? durationDays, CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null || !session.User.IsAdmin)
            return AccountResult<AccountUser>.Failure("ADMIN_REQUIRED", "Bu işlem için yönetici yetkisi gerekli.");
        if (IsOfflineSession(session)) return OfflineWriteFailure();

        var planCode = plan switch
        {
            LicensingPlanTier.Premium => "PREMIUM",
            LicensingPlanTier.PremiumPlus => "PREMIUM_PLUS",
            _ => "FREE"
        };
        var result = await repository.ChangeUserPlanAsync(session.User.Id, userId, planCode, durationDays, cancellationToken);
        if (!result.Succeeded) return Failure(result);
        if (userId == session.User.Id)
            CurrentSession = session with { User = result.Value! };
        await _offlineStore.UpdateUserAsync(result.Value!, cancellationToken);
        return AccountResult<AccountUser>.Success(result.Value!);
    }

    public async Task<AccountResult<AccountUser>> RedeemActivationTokenAsync(
        string activationCode, CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null)
            return AccountResult<AccountUser>.Failure("AUTH_REQUIRED", "Aktivasyon için giriş yapın.");
        if (IsOfflineSession(session)) return OfflineWriteFailure();
        var result = await repository.RedeemActivationTokenAsync(session.User.Id, activationCode, cancellationToken);
        if (!result.Succeeded) return Failure(result);
        CurrentSession = session with { User = result.Value! };
        await _offlineStore.UpdateUserAsync(result.Value!, cancellationToken);
        return AccountResult<AccountUser>.Success(result.Value!);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            var token = await refreshTokenStore.ReadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(token)
                && !token.StartsWith(OfflineRefreshPrefix, StringComparison.Ordinal))
                await repository.LogoutAsync(token, cancellationToken);
        }
        finally
        {
            CurrentSession = null;
            await refreshTokenStore.ClearAsync(CancellationToken.None);
            _sessionGate.Release();
        }
    }

    private async Task<AccountResult<AccountUser>> RefreshCoreAsync(string token, CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            var device = await deviceIdentityProvider.GetAsync(cancellationToken);
            var result = await repository.RefreshAsync(token, device.Identifier, device.Name, cancellationToken);
            if (!result.Succeeded)
            {
                if (result.ErrorCode == "DATABASE_UNAVAILABLE")
                {
                    var cached = await _offlineStore.ReadMostRecentUserAsync(cancellationToken);
                    if (cached is not null)
                        return await AcceptOfflineAuthenticationUnsafeAsync(cached, cached.Email, cancellationToken);
                }

                CurrentSession = null;
                if (result.ErrorCode != "DATABASE_UNAVAILABLE")
                    await refreshTokenStore.ClearAsync(CancellationToken.None);
                return Failure(result);
            }
            var accepted = await AcceptAuthenticationUnsafeAsync(result.Value!, cancellationToken);
            await _offlineStore.UpdateUserAsync(result.Value!.User, cancellationToken);
            return accepted;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task<AccountResult<AccountUser>> AcceptAuthenticationAsync(
        DirectAuthentication authentication, CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try { return await AcceptAuthenticationUnsafeAsync(authentication, cancellationToken); }
        finally { _sessionGate.Release(); }
    }

    private async Task<AccountResult<AccountUser>> AcceptAuthenticationUnsafeAsync(
        DirectAuthentication authentication, CancellationToken cancellationToken)
    {
        await refreshTokenStore.WriteAsync(authentication.RefreshToken, cancellationToken);
        var localSessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        CurrentSession = new AccountSession(localSessionKey, DateTime.UtcNow.AddMinutes(15), authentication.User);
        await TryPersistSessionSnapshotAsync(authentication.User, cancellationToken);
        return AccountResult<AccountUser>.Success(authentication.User);
    }

    private async Task TryPersistSessionSnapshotAsync(AccountUser user, CancellationToken cancellationToken)
    {
        try
        {
            await _offlineStore.UpdateUserAsync(user, cancellationToken);
        }
        catch (Exception ex)
        {
            // The online login remains valid even if the optional offline snapshot
            // cannot be written. The refresh token is still protected separately.
            AppLogger.Warning($"Account session snapshot could not be persisted: {ex.Message}",
                category: "Identity", action: "PersistSession");
        }
    }

    private async Task<AccountResult<AccountUser>> AcceptOfflineAuthenticationAsync(
        AccountUser user, string email, CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try { return await AcceptOfflineAuthenticationUnsafeAsync(user, email, cancellationToken); }
        finally { _sessionGate.Release(); }
    }

    private async Task<AccountResult<AccountUser>> AcceptOfflineAuthenticationUnsafeAsync(
        AccountUser user, string email, CancellationToken cancellationToken)
    {
        var marker = OfflineRefreshPrefix + NormalizeEmail(email);
        await refreshTokenStore.WriteAsync(marker, cancellationToken);
        CurrentSession = new AccountSession(
            OfflineSessionPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            DateTime.UtcNow.AddHours(12),
            user with { IsAdmin = false });
        return AccountResult<AccountUser>.Success(CurrentSession.User);
    }

    private async Task<AccountResult<AccountUser>> ClearAndFailAsync(
        string code, string message, CancellationToken cancellationToken)
    {
        CurrentSession = null;
        await refreshTokenStore.ClearAsync(cancellationToken);
        return AccountResult<AccountUser>.Failure(code, message);
    }

    private bool IsOfflineSession() => CurrentSession is not null && IsOfflineSession(CurrentSession);
    private static bool IsOfflineSession(AccountSession session) =>
        session.AccessToken.StartsWith(OfflineSessionPrefix, StringComparison.Ordinal);

    private static bool RegistrationIsValid(string email, string displayName, string password)
    {
        var normalizedEmail = (email ?? string.Empty).Trim();
        var validEmail = MailAddress.TryCreate(normalizedEmail, out var address)
                         && string.Equals(address.Address, normalizedEmail, StringComparison.OrdinalIgnoreCase);
        var nameLength = (displayName ?? string.Empty).Trim().Length;
        return validEmail && nameLength is >= 2 and <= 100
               && password is { Length: >= 10 }
               && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);
    }

    private static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToUpperInvariant();

    private static AccountResult<AccountUser> OfflineWriteFailure() =>
        AccountResult<AccountUser>.Failure(
            "ACCOUNT_OFFLINE",
            "Bu işlem hesap sunucusuna bağlantı gerektirir. Bağlantı geldiğinde tekrar deneyin.");

    private static AccountResult<AccountUser> Failure<T>(DirectRepositoryResult<T> result) =>
        AccountResult<AccountUser>.Failure(
            result.ErrorCode ?? "REQUEST_FAILED",
            SafeFailureMessage(result.ErrorCode, result.ErrorMessage));

    private static string SafeFailureMessage(string? code, string? message) =>
        code == "DATABASE_UNAVAILABLE"
            ? "Hesap hizmetine şu anda ulaşılamıyor. Bu cihazda daha önce doğrulanmış bir hesapla çevrimdışı giriş yapabilirsiniz."
            : message ?? "İşlem tamamlanamadı.";
}

public static class AccountComposition
{
    private static IAccountService? _current;
    public static IAccountService Current => _current ?? throw new InvalidOperationException("Account service has not been initialized.");

    public static IAccountService Initialize(IdentitySqlConnectionOptions options)
    {
        var repository = new DirectSqlAccountRepository(options.BuildConnectionString());
        return _current = new AccountService(
            repository,
            new DpapiRefreshTokenStore(),
            new DpapiDeviceIdentityProvider(),
            new DpapiOfflineAccountStore());
    }

    public static IAccountService InitializeOffline()
    {
        return _current = new AccountService(
            new UnavailableAccountRepository(),
            new DpapiRefreshTokenStore(),
            new DpapiDeviceIdentityProvider(),
            new DpapiOfflineAccountStore());
    }
}
