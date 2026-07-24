using System.Data;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Netvium.Helpers;
using Netvium.Models.Account;

namespace Netvium.Services.Account.DirectSql;

public sealed record DirectRepositoryResult<T>(T? Value, string? ErrorCode, string? ErrorMessage)
{
    public bool Succeeded => ErrorCode is null;
    public static DirectRepositoryResult<T> Success(T value) => new(value, null, null);
    public static DirectRepositoryResult<T> Failure(string code, string message) => new(default, code, message);
}

public sealed record DirectAuthentication(AccountUser User, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

public interface IDirectAccountRepository
{
    Task<DirectRepositoryResult<bool>> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<DirectAuthentication>> LoginAsync(string email, string password, string deviceIdentifier, string deviceName, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<DirectAuthentication>> RefreshAsync(string refreshToken, string deviceIdentifier, string deviceName, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<AccountUser>> GetAccountAsync(Guid userId, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<AccountUser>> ActivatePlanAsync(Guid userId, string planCode, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<AdminDashboardData>> GetAdminDashboardAsync(Guid adminUserId, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<AdminActivationToken>> IssueActivationTokenAsync(Guid adminUserId, string planCode, int planDurationDays, string? note, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<AccountUser>> ChangeUserPlanAsync(Guid adminUserId, Guid userId, string planCode, int? durationDays, CancellationToken cancellationToken);
    Task<DirectRepositoryResult<AccountUser>> RedeemActivationTokenAsync(Guid userId, string activationCode, CancellationToken cancellationToken);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);
}

public sealed class DirectSqlAccountRepository : IDirectAccountRepository
{
    private const string InvalidCredentialsCode = "AUTH_INVALID_CREDENTIALS";
    private const string InvalidCredentialsMessage = "E-posta veya parola hatalı.";
    private readonly DbContextOptions<DirectIdentityDbContext> _dbOptions;
    private readonly PasswordHasher<DirectUser> _passwordHasher = new();

    public DirectSqlAccountRepository(string connectionString)
    {
        _dbOptions = new DbContextOptionsBuilder<DirectIdentityDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public async Task<DirectRepositoryResult<bool>> RegisterAsync(
        string email, string displayName, string password, CancellationToken cancellationToken)
    {
        email = (email ?? string.Empty).Trim();
        displayName = (displayName ?? string.Empty).Trim();
        if (!IsEmailShapeValid(email) || displayName.Length is < 2 or > 100 || !PasswordPolicyIsValid(password))
            return DirectRepositoryResult<bool>.Failure("REGISTRATION_INVALID", "Bilgileri kontrol edin. Parola en az 10 karakter, büyük/küçük harf ve rakam içermelidir.");

        try
        {
            await using var db = CreateContext();
            var normalizedEmail = NormalizeEmail(email);
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
                return DirectRepositoryResult<bool>.Failure("EMAIL_ALREADY_REGISTERED", "Bu e-posta ile kayıt oluşturulamıyor.");

            var freePlan = await db.Plans.SingleOrDefaultAsync(x => x.PlanCode == "FREE" && x.IsActive, cancellationToken);
            if (freePlan is null)
                return DirectRepositoryResult<bool>.Failure("SERVICE_CONFIGURATION_ERROR", "Aktif FREE planı bulunamadığı için hesap oluşturulamıyor.");

            var now = DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var user = new DirectUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                NormalizedEmail = normalizedEmail,
                DisplayName = displayName,
                IsActive = true,
                IsEmailVerified = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            db.Users.Add(user);
            db.UserLicenses.Add(new DirectUserLicense
            {
                Id = Guid.NewGuid(), UserId = user.Id, PlanId = freePlan.Id,
                Status = "ACTIVE", StartsAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.AuthAuditLogs.Add(Audit(user.Id, "REGISTER_SUCCESS", true, null, null, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return DirectRepositoryResult<bool>.Success(true);
        }
        catch (DbUpdateException ex) when (FindSqlException(ex)?.Number is 2601 or 2627)
        {
            return DirectRepositoryResult<bool>.Failure("EMAIL_ALREADY_REGISTERED", "Bu e-posta ile kayıt oluşturulamıyor.");
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<bool>(ex);
        }
    }

    public async Task<DirectRepositoryResult<DirectAuthentication>> LoginAsync(
        string email, string password, string deviceIdentifier, string deviceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(deviceIdentifier) || string.IsNullOrWhiteSpace(deviceName))
            return InvalidCredentials<DirectAuthentication>();

        try
        {
            await using var db = CreateContext();
            var now = DateTime.UtcNow;
            var normalizedEmail = NormalizeEmail(email);
            var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
            if (user is null)
            {
                db.AuthAuditLogs.Add(Audit(null, "LOGIN_FAILED", false, InvalidCredentialsCode, deviceIdentifier, now));
                await db.SaveChangesAsync(cancellationToken);
                return InvalidCredentials<DirectAuthentication>();
            }

            var passwordResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            var locked = user.LockoutEndUtc is { } lockout && lockout > now;
            if (!user.IsActive || locked || passwordResult == PasswordVerificationResult.Failed)
            {
                if (!locked && passwordResult == PasswordVerificationResult.Failed)
                {
                    user.FailedLoginCount++;
                    if (user.FailedLoginCount >= 5) user.LockoutEndUtc = now.AddMinutes(15);
                    user.UpdatedAtUtc = now;
                }
                db.AuthAuditLogs.Add(Audit(user.Id, "LOGIN_FAILED", false, InvalidCredentialsCode, deviceIdentifier, now));
                await db.SaveChangesAsync(cancellationToken);
                return InvalidCredentials<DirectAuthentication>();
            }

            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            user.LastLoginAtUtc = now;
            user.UpdatedAtUtc = now;
            await UpsertDeviceAsync(db, user.Id, deviceIdentifier.Trim(), deviceName.Trim(), now, cancellationToken);
            var authentication = await IssueAuthenticationAsync(
                db, user.Id, HashToken(deviceIdentifier.Trim()), now, cancellationToken);
            db.AuthAuditLogs.Add(Audit(user.Id, "LOGIN_SUCCESS", true, null, deviceIdentifier, now));
            db.AccountUsageEvents.Add(Usage(user.Id, "LOGIN", deviceName.Trim(), now));
            await db.SaveChangesAsync(cancellationToken);
            return authentication is null
                ? DirectRepositoryResult<DirectAuthentication>.Failure("ACCOUNT_NOT_FOUND", "Hesap bilgisi bulunamadı.")
                : DirectRepositoryResult<DirectAuthentication>.Success(authentication);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<DirectAuthentication>(ex);
        }
    }

    public async Task<DirectRepositoryResult<DirectAuthentication>> RefreshAsync(
        string refreshToken, string deviceIdentifier, string deviceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return InvalidRefresh();
        try
        {
            await using var db = CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var now = DateTime.UtcNow;
            var hash = HashToken(refreshToken);
            var stored = await db.RefreshTokens.Include(x => x.User)
                .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
            if (stored is null) return InvalidRefresh();

            if (stored.RevokedAtUtc is not null)
            {
                db.AuthAuditLogs.Add(AuditFromDeviceHash(stored.UserId, "TOKEN_REFRESH", false, "TOKEN_REUSE_DETECTED", stored.DeviceIdentifierHash, now));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return InvalidRefresh();
            }

            if (stored.ExpiresAtUtc <= now || !stored.User.IsActive)
            {
                stored.RevokedAtUtc = now;
                db.AuthAuditLogs.Add(AuditFromDeviceHash(stored.UserId, "TOKEN_REFRESH", false, "TOKEN_EXPIRED_OR_INACTIVE", stored.DeviceIdentifierHash, now));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return InvalidRefresh();
            }

            stored.RevokedAtUtc = now;
            var nextDeviceHash = string.IsNullOrWhiteSpace(deviceIdentifier)
                ? stored.DeviceIdentifierHash
                : HashToken(deviceIdentifier.Trim());
            var authentication = await IssueAuthenticationAsync(
                db, stored.UserId, nextDeviceHash, now, cancellationToken);
            stored.ReplacedByTokenId = db.RefreshTokens.Local.Last().Id;
            db.AuthAuditLogs.Add(AuditFromDeviceHash(stored.UserId, "TOKEN_REFRESH", true, null, nextDeviceHash, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return authentication is null ? InvalidRefresh() : DirectRepositoryResult<DirectAuthentication>.Success(authentication);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<DirectAuthentication>(ex);
        }
    }

    public async Task<DirectRepositoryResult<AccountUser>> GetAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = CreateContext();
            var account = await QueryAccount(db, userId).SingleOrDefaultAsync(cancellationToken);
            return account is null
                ? DirectRepositoryResult<AccountUser>.Failure("ACCOUNT_NOT_FOUND", "Hesap bulunamadı.")
                : DirectRepositoryResult<AccountUser>.Success(account);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<AccountUser>(ex);
        }
    }

    public async Task<DirectRepositoryResult<AccountUser>> ActivatePlanAsync(
        Guid userId, string planCode, CancellationToken cancellationToken)
    {
        planCode = (planCode ?? string.Empty).Trim().ToUpperInvariant();
        if (planCode is not ("PREMIUM" or "PREMIUM_PLUS"))
            return DirectRepositoryResult<AccountUser>.Failure("PLAN_INVALID", "Geçerli bir ücretli plan seçilmedi.");

        try
        {
            await using var db = CreateContext();
            var plan = await db.Plans.SingleOrDefaultAsync(
                x => x.PlanCode == planCode && x.IsActive, cancellationToken);
            if (plan is null)
                return DirectRepositoryResult<AccountUser>.Failure("PLAN_NOT_AVAILABLE", "Seçilen plan kullanıma açık değil.");

            var userExists = await db.Users.AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken);
            if (!userExists)
                return DirectRepositoryResult<AccountUser>.Failure("ACCOUNT_NOT_FOUND", "Hesap bulunamadı.");

            var now = DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var activeLicenses = await db.UserLicenses
                .Where(x => x.UserId == userId && x.Status == "ACTIVE")
                .ToListAsync(cancellationToken);

            foreach (var license in activeLicenses)
            {
                license.Status = "CANCELLED";
                license.CancelledAtUtc = now;
                license.UpdatedAtUtc = now;
            }

            db.UserLicenses.Add(new DirectUserLicense
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = plan.Id,
                Status = "ACTIVE",
                StartsAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.AuthAuditLogs.Add(Audit(userId, "PLAN_ACTIVATED", true, planCode, null, now));
            db.AccountUsageEvents.Add(Usage(userId, "PLAN_ACTIVATED", planCode, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var account = await QueryAccount(db, userId).SingleOrDefaultAsync(cancellationToken);
            return account is null
                ? DirectRepositoryResult<AccountUser>.Failure("ACCOUNT_NOT_FOUND", "Hesap bulunamadı.")
                : DirectRepositoryResult<AccountUser>.Success(account);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<AccountUser>(ex);
        }
    }

    public async Task<DirectRepositoryResult<AdminDashboardData>> GetAdminDashboardAsync(
        Guid adminUserId, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = CreateContext();
            if (!await IsAdminAsync(db, adminUserId, cancellationToken))
                return DirectRepositoryResult<AdminDashboardData>.Failure("ADMIN_REQUIRED", "Bu işlem için yönetici yetkisi gerekli.");

            var users = await db.Users.AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new AdminUserSummary(
                    x.Id, x.Email, x.DisplayName, x.IsActive, x.IsAdmin,
                    x.Licenses.Where(l => l.Status == "ACTIVE" && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(l => l.StartsAtUtc).Select(l => l.Plan.PlanCode).FirstOrDefault() ?? "FREE",
                    x.Licenses.Where(l => l.Status == "ACTIVE" && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(l => l.StartsAtUtc).Select(l => l.Plan.DisplayName).FirstOrDefault() ?? "Free",
                    x.Licenses.Where(l => l.Status == "ACTIVE" && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(l => l.StartsAtUtc).Select(l => l.ExpiresAtUtc).FirstOrDefault(),
                    x.LastLoginAtUtc,
                    db.UserDevices.Count(d => d.UserId == x.Id && d.IsActive)))
                .ToListAsync(cancellationToken);

            var tokens = await db.ActivationTokens.AsNoTracking().Include(x => x.Plan)
                .OrderByDescending(x => x.CreatedAtUtc).Take(100)
                .Select(x => new AdminActivationTokenSummary(x.Id, x.Plan.PlanCode, x.CreatedAtUtc,
                    x.PlanDurationDays, x.ExpiresAtUtc, x.RedeemedAtUtc, x.RevokedAtUtc, x.Note))
                .ToListAsync(cancellationToken);
            var usage = await db.AccountUsageEvents.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100)
                .Select(x => new AdminUsageEventSummary(x.Id, x.UserId, x.EventType, x.Detail, x.Quantity, x.CreatedAtUtc))
                .ToListAsync(cancellationToken);
            var audits = await db.AuthAuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100)
                .Select(x => new AdminAuditEventSummary(x.Id, x.UserId, x.EventType, x.IsSuccessful, x.DetailCode, x.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return DirectRepositoryResult<AdminDashboardData>.Success(new(
                users.Count, users.Count(x => x.IsActive), users.Count(x => x.PlanCode is "PREMIUM" or "PREMIUM_PLUS"),
                await db.UserDevices.CountAsync(x => x.IsActive, cancellationToken), users, tokens, usage, audits));
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<AdminDashboardData>(ex);
        }
    }

    public async Task<DirectRepositoryResult<AdminActivationToken>> IssueActivationTokenAsync(
        Guid adminUserId, string planCode, int planDurationDays, string? note, CancellationToken cancellationToken)
    {
        planCode = (planCode ?? string.Empty).Trim().ToUpperInvariant();
        if (planCode is not ("PREMIUM" or "PREMIUM_PLUS"))
            return DirectRepositoryResult<AdminActivationToken>.Failure("PLAN_INVALID", "Premium veya Premium+ planı seçin.");
        if (planDurationDays is < 1 or > 3650)
            return DirectRepositoryResult<AdminActivationToken>.Failure("DURATION_INVALID", "Plan süresi 1 ile 3650 gün arasında olmalı.");

        try
        {
            await using var db = CreateContext();
            if (!await IsAdminAsync(db, adminUserId, cancellationToken))
                return DirectRepositoryResult<AdminActivationToken>.Failure("ADMIN_REQUIRED", "Bu işlem için yönetici yetkisi gerekli.");
            var plan = await db.Plans.SingleOrDefaultAsync(x => x.PlanCode == planCode && x.IsActive, cancellationToken);
            if (plan is null)
                return DirectRepositoryResult<AdminActivationToken>.Failure("PLAN_NOT_AVAILABLE", "Seçilen plan kullanıma açık değil.");

            var now = DateTime.UtcNow;
            var raw = $"NV-{planCode}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";
            var expires = now.AddDays(30);
            db.ActivationTokens.Add(new DirectActivationToken
            {
                Id = Guid.NewGuid(), TokenHash = HashToken(raw), PlanId = plan.Id, CreatedByUserId = adminUserId,
                CreatedAtUtc = now, ExpiresAtUtc = expires, PlanDurationDays = planDurationDays,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });
            db.AuthAuditLogs.Add(Audit(adminUserId, "ACTIVATION_TOKEN_ISSUED", true, planCode, null, now));
            db.AccountUsageEvents.Add(Usage(adminUserId, "ACTIVATION_TOKEN_ISSUED", planCode, now));
            await db.SaveChangesAsync(cancellationToken);
            return DirectRepositoryResult<AdminActivationToken>.Success(new(raw, planCode, expires));
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<AdminActivationToken>(ex);
        }
    }

    public async Task<DirectRepositoryResult<AccountUser>> ChangeUserPlanAsync(
        Guid adminUserId, Guid userId, string planCode, int? durationDays, CancellationToken cancellationToken)
    {
        planCode = (planCode ?? string.Empty).Trim().ToUpperInvariant();
        if (planCode is not ("FREE" or "PREMIUM" or "PREMIUM_PLUS"))
            return DirectRepositoryResult<AccountUser>.Failure("PLAN_INVALID", "Geçerli bir plan seçin.");
        if (planCode != "FREE" && durationDays is not (>= 1 and <= 3650))
            return DirectRepositoryResult<AccountUser>.Failure("DURATION_INVALID", "Premium süresi 1 ile 3650 gün arasında olmalı.");

        try
        {
            await using var db = CreateContext();
            if (!await IsAdminAsync(db, adminUserId, cancellationToken))
                return DirectRepositoryResult<AccountUser>.Failure("ADMIN_REQUIRED", "Bu işlem için yönetici yetkisi gerekli.");

            var userExists = await db.Users.AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken);
            if (!userExists)
                return DirectRepositoryResult<AccountUser>.Failure("ACCOUNT_NOT_FOUND", "Kullanıcı bulunamadı veya aktif değil.");

            var plan = await db.Plans.SingleOrDefaultAsync(x => x.PlanCode == planCode && x.IsActive, cancellationToken);
            if (plan is null)
                return DirectRepositoryResult<AccountUser>.Failure("PLAN_NOT_AVAILABLE", "Seçilen plan kullanıma açık değil.");

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var now = DateTime.UtcNow;
            var activeLicenses = await db.UserLicenses.Where(x => x.UserId == userId && x.Status == "ACTIVE").ToListAsync(cancellationToken);
            foreach (var license in activeLicenses)
            {
                license.Status = "CANCELLED";
                license.CancelledAtUtc = now;
                license.UpdatedAtUtc = now;
            }

            db.UserLicenses.Add(new DirectUserLicense
            {
                Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Status = "ACTIVE",
                StartsAtUtc = now,
                ExpiresAtUtc = planCode == "FREE" ? null : now.AddDays(durationDays!.Value),
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.AuthAuditLogs.Add(Audit(userId, "ADMIN_PLAN_CHANGED", true, $"{planCode}:{durationDays?.ToString() ?? "UNLIMITED"}", null, now));
            db.AccountUsageEvents.Add(Usage(userId, "ADMIN_PLAN_CHANGED", planCode, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var account = await QueryAccount(db, userId).SingleOrDefaultAsync(cancellationToken);
            return account is null
                ? DirectRepositoryResult<AccountUser>.Failure("ACCOUNT_NOT_FOUND", "Hesap bulunamadı.")
                : DirectRepositoryResult<AccountUser>.Success(account);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<AccountUser>(ex);
        }
    }

    public async Task<DirectRepositoryResult<AccountUser>> RedeemActivationTokenAsync(
        Guid userId, string activationCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(activationCode))
            return DirectRepositoryResult<AccountUser>.Failure("TOKEN_INVALID", "Aktivasyon kodu geçersiz.");
        try
        {
            await using var db = CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var now = DateTime.UtcNow;
            var token = await db.ActivationTokens.Include(x => x.Plan).SingleOrDefaultAsync(
                x => x.TokenHash == HashToken(activationCode.Trim()), cancellationToken);
            if (token is null || token.RedeemedAtUtc is not null || token.RevokedAtUtc is not null ||
                token.ExpiresAtUtc is { } expires && expires <= now)
                return DirectRepositoryResult<AccountUser>.Failure("TOKEN_INVALID", "Aktivasyon kodu geçersiz, kullanılmış veya süresi dolmuş.");

            var activeLicenses = await db.UserLicenses.Where(x => x.UserId == userId && x.Status == "ACTIVE").ToListAsync(cancellationToken);
            foreach (var license in activeLicenses)
            {
                license.Status = "CANCELLED";
                license.CancelledAtUtc = now;
                license.UpdatedAtUtc = now;
            }
            db.UserLicenses.Add(new DirectUserLicense
            {
                Id = Guid.NewGuid(), UserId = userId, PlanId = token.PlanId, Status = "ACTIVE",
                StartsAtUtc = now, ExpiresAtUtc = now.AddDays(token.PlanDurationDays),
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            token.RedeemedAtUtc = now;
            token.RedeemedByUserId = userId;
            db.AuthAuditLogs.Add(Audit(userId, "ACTIVATION_TOKEN_REDEEMED", true, token.Plan.PlanCode, null, now));
            db.AccountUsageEvents.Add(Usage(userId, "ACTIVATION_TOKEN_REDEEMED", token.Plan.PlanCode, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var account = await QueryAccount(db, userId).SingleOrDefaultAsync(cancellationToken);
            return account is null
                ? DirectRepositoryResult<AccountUser>.Failure("ACCOUNT_NOT_FOUND", "Hesap bulunamadı.")
                : DirectRepositoryResult<AccountUser>.Success(account);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            return DatabaseFailure<AccountUser>(ex);
        }
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        try
        {
            await using var db = CreateContext();
            var now = DateTime.UtcNow;
            var hash = HashToken(refreshToken);
            var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
            if (stored is not null && stored.RevokedAtUtc is null) stored.RevokedAtUtc = now;
            db.AuthAuditLogs.Add(stored is null
                ? Audit(null, "LOGOUT", false, "TOKEN_NOT_FOUND", null, now)
                : AuditFromDeviceHash(stored.UserId, "LOGOUT", true, null, stored.DeviceIdentifierHash, now));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            // Yerel oturum her durumda temizleneceği için uzak revoke en iyi çabadır.
        }
    }

    private DirectIdentityDbContext CreateContext() => new(_dbOptions);

    private static Task<bool> IsAdminAsync(
        DirectIdentityDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(x => x.Id == userId && x.IsActive && x.IsAdmin, cancellationToken);

    private static async Task<DirectAuthentication?> IssueAuthenticationAsync(
        DirectIdentityDbContext db, Guid userId, string deviceIdentifierHash,
        DateTime now, CancellationToken cancellationToken)
    {
        var account = await QueryAccount(db, userId).SingleOrDefaultAsync(cancellationToken);
        if (account is null) return null;
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expires = now.AddDays(30);
        db.RefreshTokens.Add(new DirectRefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, TokenHash = HashToken(raw),
            CreatedAtUtc = now, ExpiresAtUtc = expires,
            DeviceIdentifierHash = deviceIdentifierHash
        });
        return new(account, raw, expires);
    }

    private static IQueryable<AccountUser> QueryAccount(DirectIdentityDbContext db, Guid userId) =>
        db.Users.AsNoTracking().Where(x => x.Id == userId && x.IsActive)
            .Select(x => new AccountUser(
                x.Id, x.Email, x.DisplayName, x.IsEmailVerified,
                x.Licenses.Where(l => l.Status == "ACTIVE" && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(l => l.StartsAtUtc).Select(l => l.Plan.PlanCode).FirstOrDefault() ?? "FREE",
                x.Licenses.Where(l => l.Status == "ACTIVE" && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(l => l.StartsAtUtc).Select(l => l.Plan.DisplayName).FirstOrDefault() ?? "Free",
                x.Licenses.Where(l => l.Status == "ACTIVE" && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(l => l.StartsAtUtc).Select(l => l.Status).FirstOrDefault() ?? "INACTIVE",
                x.Licenses.Where(l => l.Status == "ACTIVE" && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(l => l.StartsAtUtc).Select(l => l.ExpiresAtUtc).FirstOrDefault(),
                x.IsAdmin));

    private static async Task UpsertDeviceAsync(
        DirectIdentityDbContext db, Guid userId, string identifier, string name, DateTime now, CancellationToken cancellationToken)
    {
        var deviceHash = HashToken(identifier);
        var device = await db.UserDevices.SingleOrDefaultAsync(
            x => x.UserId == userId && x.DeviceIdentifierHash == deviceHash, cancellationToken);
        if (device is null)
            db.UserDevices.Add(new DirectUserDevice
            {
                Id = Guid.NewGuid(), UserId = userId, DeviceIdentifierHash = deviceHash,
                DeviceName = name, FirstSeenAtUtc = now, LastSeenAtUtc = now, IsActive = true
            });
        else
        {
            device.DeviceName = name;
            device.LastSeenAtUtc = now;
            device.IsActive = true;
        }
    }

    private static DirectAuthAuditLog Audit(Guid? userId, string eventType, bool success, string? failureCode, string? deviceIdentifier, DateTime now) =>
        new()
        {
            UserId = userId, EventType = eventType, IsSuccessful = success,
            DetailCode = failureCode, DeviceIdentifierHash = string.IsNullOrWhiteSpace(deviceIdentifier) ? null : HashToken(deviceIdentifier),
            CreatedAtUtc = now
        };

    private static DirectAuthAuditLog AuditFromDeviceHash(Guid? userId, string eventType, bool success, string? failureCode, string? deviceIdentifierHash, DateTime now) =>
        new()
        {
            UserId = userId, EventType = eventType, IsSuccessful = success,
            DetailCode = failureCode, DeviceIdentifierHash = deviceIdentifierHash,
            CreatedAtUtc = now
        };

    private static DirectAccountUsageEvent Usage(Guid? userId, string eventType, string? detail, DateTime now) =>
        new()
        {
            UserId = userId,
            EventType = eventType,
            Detail = detail,
            Quantity = 1,
            CreatedAtUtc = now
        };

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToUpperInvariant();
    private static bool IsEmailShapeValid(string email) => email.Length is > 3 and <= 320 && email.Contains('@') && !email.Contains(' ');
    private static bool PasswordPolicyIsValid(string? password) => password is { Length: >= 10 }
        && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);
    private static string HashToken(string raw) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
    private static DirectRepositoryResult<T> InvalidCredentials<T>() => DirectRepositoryResult<T>.Failure(InvalidCredentialsCode, InvalidCredentialsMessage);
    private static DirectRepositoryResult<DirectAuthentication> InvalidRefresh() =>
        DirectRepositoryResult<DirectAuthentication>.Failure("REFRESH_TOKEN_INVALID", "Oturum yenilenemedi.");
    private static DirectRepositoryResult<T> DatabaseFailure<T>(Exception exception)
    {
        AppLogger.Error("NetviumIdentity account query failed.", exception, category: "Identity", action: "DatabaseQuery");

        var sqlException = FindSqlException(exception);
        var errorCode = sqlException?.Number switch
        {
            18456 => "DATABASE_AUTH_FAILED",
            229 => "DATABASE_PERMISSION_DENIED",
            208 or 207 => "DATABASE_SCHEMA_INVALID",
            _ => "DATABASE_UNAVAILABLE"
        };
        var message = sqlException switch
        {
            { Number: 18456 } =>
                "SQL kimlik doğrulaması başarısız. SQL kullanıcı adı ve parolasını kontrol edin.",
            { Number: 229 } =>
                "SQL kullanıcısının NetviumIdentity tablolarına erişim yetkisi yok.",
            { Number: 208 or 207 } =>
                $"NetviumIdentity şeması uygulamayla uyumlu değil: {GetSqlErrorSummary(sqlException)}",
            _ when exception is TimeoutException =>
                "Hesap veritabanı sorgusu zaman aşımına uğradı.",
            _ =>
                sqlException is not null
                    ? $"Hesap veritabanı sorgulanamadı (SQL hata kodu: {sqlException.Number}). Şemayı ve tablo izinlerini kontrol edin."
                    : "Hesap veritabanı sorgulanamadı. Sunucu bağlantısı ve NetviumIdentity şemasını kontrol edin."
        };

        return DirectRepositoryResult<T>.Failure(errorCode, message);
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
                return sqlException;
        }

        return null;
    }

    private static string GetSqlErrorSummary(SqlException exception)
    {
        var firstLine = exception.Message
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();

        return string.IsNullOrWhiteSpace(firstLine)
            ? $"SQL hata kodu {exception.Number}."
            : firstLine;
    }
    private static bool IsDatabaseException(Exception ex) =>
        ex is SqlException or InvalidOperationException or TimeoutException or DbUpdateException;
}
