using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Netvium.Identity.Api.Contracts;
using Netvium.Identity.Api.Domain;
using Netvium.Identity.Api.Infrastructure;
using Netvium.Identity.Api.Security;

namespace Netvium.Identity.Api.Services;

public sealed record ServiceResult<T>(T? Value, ApiError? Error)
{
    public bool Succeeded => Error is null;
    public static ServiceResult<T> Ok(T value) => new(value, null);
    public static ServiceResult<T> Fail(string code, string message) => new(default, new ApiError(code, message));
}

public interface IAuthService
{
    Task<ServiceResult<RegistrationResponse>> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task<ServiceResult<AuthResponse>> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> LogoutAsync(LogoutRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task<ServiceResult<AccountResponse>> GetAccountAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class AuthService(
    IdentityDbContext db,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IAuthService
{
    private const string InvalidCredentialsCode = "AUTH_INVALID_CREDENTIALS";
    private const string InvalidCredentialsMessage = "E-posta veya parola hatalı.";

    public async Task<ServiceResult<RegistrationResponse>> RegisterAsync(
        RegisterRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var email = request.Email?.Trim() ?? string.Empty;
        var normalizedEmail = NormalizeEmail(email);
        var displayName = request.DisplayName?.Trim() ?? string.Empty;

        if (!IsEmailShapeValid(email) || displayName.Length is < 2 or > 120 || !PasswordPolicy.IsValid(request.Password))
        {
            await AuditAsync(null, "REGISTER_FAILED", false, "VALIDATION_FAILED", ipAddress, null, now, cancellationToken);
            return ServiceResult<RegistrationResponse>.Fail(
                "REGISTRATION_INVALID",
                "Bilgileri kontrol edin. Parola en az 10 karakter, büyük/küçük harf ve rakam içermelidir.");
        }

        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            await AuditAsync(null, "REGISTER_FAILED", false, "EMAIL_EXISTS", ipAddress, null, now, cancellationToken);
            return ServiceResult<RegistrationResponse>.Fail("EMAIL_ALREADY_REGISTERED", "Bu e-posta ile kayıt oluşturulamıyor.");
        }

        var freePlan = await db.Plans.SingleOrDefaultAsync(x => x.Code == "FREE" && x.IsActive, cancellationToken);
        if (freePlan is null)
        {
            await AuditAsync(null, "REGISTER_FAILED", false, "FREE_PLAN_MISSING", ipAddress, null, now, cancellationToken);
            return ServiceResult<RegistrationResponse>.Fail("SERVICE_CONFIGURATION_ERROR", "Hesap şu anda oluşturulamıyor.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new User
            {
                Id = Guid.NewGuid(), Email = email, NormalizedEmail = normalizedEmail,
                DisplayName = displayName, IsActive = true, IsEmailVerified = false,
                CreatedAtUtc = now, UpdatedAtUtc = now
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            db.Users.Add(user);
            db.UserLicenses.Add(new UserLicense
            {
                Id = Guid.NewGuid(), UserId = user.Id, PlanId = freePlan.Id,
                Status = "ACTIVE", StartsAtUtc = now, CreatedAtUtc = now
            });
            db.AuthAuditLogs.Add(NewAudit(user.Id, "REGISTER_SUCCESS", true, null, ipAddress, null, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<RegistrationResponse>.Ok(new(user.Id, user.Email, user.DisplayName));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            await AuditAsync(null, "REGISTER_FAILED", false, "PERSISTENCE_FAILED", ipAddress, null, now, cancellationToken);
            return ServiceResult<RegistrationResponse>.Fail("EMAIL_ALREADY_REGISTERED", "Bu e-posta ile kayıt oluşturulamıyor.");
        }
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var normalizedEmail = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password) ||
            string.IsNullOrWhiteSpace(request.DeviceIdentifier) || string.IsNullOrWhiteSpace(request.DeviceName))
        {
            await AuditAsync(null, "LOGIN_FAILED", false, InvalidCredentialsCode, ipAddress, request.DeviceIdentifier, now, cancellationToken);
            return InvalidCredentials<AuthResponse>();
        }
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            await AuditAsync(null, "LOGIN_FAILED", false, InvalidCredentialsCode, ipAddress, request.DeviceIdentifier, now, cancellationToken);
            return InvalidCredentials<AuthResponse>();
        }

        var passwordResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        var locked = user.LockoutEndUtc is { } lockout && lockout > now;
        if (!user.IsActive || locked || passwordResult == PasswordVerificationResult.Failed)
        {
            if (!locked && passwordResult == PasswordVerificationResult.Failed)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= 5)
                    user.LockoutEndUtc = now.AddMinutes(15);
                user.UpdatedAtUtc = now;
            }
            db.AuthAuditLogs.Add(NewAudit(user.Id, "LOGIN_FAILED", false, InvalidCredentialsCode, ipAddress, request.DeviceIdentifier, now));
            await db.SaveChangesAsync(cancellationToken);
            return InvalidCredentials<AuthResponse>();
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        user.UpdatedAtUtc = now;
        await UpsertDeviceAsync(user.Id, request.DeviceIdentifier.Trim(), request.DeviceName.Trim(), now, cancellationToken);
        var response = await IssueTokenPairAsync(user, Guid.NewGuid(), request.DeviceIdentifier.Trim(), request.DeviceName.Trim(), now, cancellationToken);
        db.AuthAuditLogs.Add(NewAudit(user.Id, "LOGIN_SUCCESS", true, null, ipAddress, request.DeviceIdentifier, now));
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<AuthResponse>.Ok(response);
    }

    public async Task<ServiceResult<AuthResponse>> RefreshAsync(
        RefreshRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ServiceResult<AuthResponse>.Fail("REFRESH_TOKEN_INVALID", "Oturum yenilenemedi.");
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (stored is null)
            return ServiceResult<AuthResponse>.Fail("REFRESH_TOKEN_INVALID", "Oturum yenilenemedi.");

        if (stored.UsedAtUtc is not null || stored.RevokedAtUtc is not null)
        {
            await RevokeFamilyAsync(stored.TokenFamilyId, now, cancellationToken);
            db.AuthAuditLogs.Add(NewAudit(stored.UserId, "TOKEN_REFRESH", false, "TOKEN_REUSE_DETECTED", ipAddress, stored.DeviceIdentifier, now));
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<AuthResponse>.Fail("REFRESH_TOKEN_INVALID", "Oturum yenilenemedi.");
        }

        if (stored.ExpiresAtUtc <= now || !stored.User.IsActive)
        {
            stored.RevokedAtUtc = now;
            db.AuthAuditLogs.Add(NewAudit(stored.UserId, "TOKEN_REFRESH", false, "TOKEN_EXPIRED_OR_INACTIVE", ipAddress, stored.DeviceIdentifier, now));
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<AuthResponse>.Fail("REFRESH_TOKEN_INVALID", "Oturum yenilenemedi.");
        }

        stored.UsedAtUtc = now;
        stored.RevokedAtUtc = now;
        var response = await IssueTokenPairAsync(stored.User, stored.TokenFamilyId, stored.DeviceIdentifier, stored.DeviceName, now, cancellationToken);
        stored.ReplacedByTokenId = db.RefreshTokens.Local.Last().Id;
        db.AuthAuditLogs.Add(NewAudit(stored.UserId, "TOKEN_REFRESH", true, null, ipAddress, stored.DeviceIdentifier, now));
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<AuthResponse>.Ok(response);
    }

    public async Task<ServiceResult<bool>> LogoutAsync(
        LogoutRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await AuditAsync(null, "LOGOUT", false, "TOKEN_NOT_FOUND", ipAddress, null, now, cancellationToken);
            return ServiceResult<bool>.Ok(true);
        }
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (stored is not null && stored.RevokedAtUtc is null)
            stored.RevokedAtUtc = now;
        db.AuthAuditLogs.Add(NewAudit(stored?.UserId, "LOGOUT", stored is not null, stored is null ? "TOKEN_NOT_FOUND" : null, ipAddress, stored?.DeviceIdentifier, now));
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<AccountResponse>> GetAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await db.Users.AsNoTracking().Where(x => x.Id == userId && x.IsActive)
            .Select(x => new AccountResponse(
                x.Id, x.Email, x.DisplayName, x.IsEmailVerified,
                x.Licenses.Where(l => l.Status == "ACTIVE").OrderByDescending(l => l.StartsAtUtc).Select(l => l.Plan.Code).FirstOrDefault() ?? "FREE",
                x.Licenses.Where(l => l.Status == "ACTIVE").OrderByDescending(l => l.StartsAtUtc).Select(l => l.Plan.DisplayName).FirstOrDefault() ?? "Free",
                x.Licenses.Where(l => l.Status == "ACTIVE").OrderByDescending(l => l.StartsAtUtc).Select(l => l.Status).FirstOrDefault() ?? "INACTIVE",
                x.Licenses.Where(l => l.Status == "ACTIVE").OrderByDescending(l => l.StartsAtUtc).Select(l => l.ExpiresAtUtc).FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);
        return account is null
            ? ServiceResult<AccountResponse>.Fail("ACCOUNT_NOT_FOUND", "Hesap bulunamadı.")
            : ServiceResult<AccountResponse>.Ok(account);
    }

    private async Task<AuthResponse> IssueTokenPairAsync(User user, Guid familyId, string deviceIdentifier, string deviceName, DateTime now, CancellationToken cancellationToken)
    {
        var access = tokenService.CreateAccessToken(user);
        var refresh = tokenService.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = refresh.Hash, TokenFamilyId = familyId,
            CreatedAtUtc = now, ExpiresAtUtc = refresh.ExpiresAtUtc,
            DeviceIdentifier = deviceIdentifier, DeviceName = deviceName
        });
        var account = await GetAccountAsync(user.Id, cancellationToken);
        return new AuthResponse(access.Token, access.ExpiresAtUtc, refresh.RawToken, refresh.ExpiresAtUtc, account.Value!);
    }

    private async Task UpsertDeviceAsync(Guid userId, string identifier, string name, DateTime now, CancellationToken cancellationToken)
    {
        var device = await db.UserDevices.SingleOrDefaultAsync(x => x.UserId == userId && x.DeviceIdentifier == identifier, cancellationToken);
        if (device is null)
            db.UserDevices.Add(new UserDevice { Id = Guid.NewGuid(), UserId = userId, DeviceIdentifier = identifier, DeviceName = name, FirstSeenAtUtc = now, LastSeenAtUtc = now, IsActive = true });
        else
        {
            device.DeviceName = name;
            device.LastSeenAtUtc = now;
            device.IsActive = true;
        }
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens.Where(x => x.TokenFamilyId == familyId && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.RevokedAtUtc = now;
    }

    private async Task AuditAsync(Guid? userId, string eventType, bool success, string? failureCode, string? ipAddress, string? deviceIdentifier, DateTime now, CancellationToken cancellationToken)
    {
        db.AuthAuditLogs.Add(NewAudit(userId, eventType, success, failureCode, ipAddress, deviceIdentifier, now));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AuthAuditLog NewAudit(Guid? userId, string eventType, bool success, string? failureCode, string? ipAddress, string? deviceIdentifier, DateTime now) =>
        new() { Id = Guid.NewGuid(), UserId = userId, EventType = eventType, Success = success, FailureCode = failureCode, IpAddress = ipAddress, DeviceIdentifier = deviceIdentifier, OccurredAtUtc = now };

    private static ServiceResult<T> InvalidCredentials<T>() => ServiceResult<T>.Fail(InvalidCredentialsCode, InvalidCredentialsMessage);
    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToUpperInvariant();
    private static bool IsEmailShapeValid(string email) => email.Length is > 3 and <= 320 && email.Contains('@') && !email.Contains(' ');
}
