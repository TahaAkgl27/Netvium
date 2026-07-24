namespace Netvium.Services.Account.DirectSql;

public sealed class DirectUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<DirectRefreshToken> RefreshTokens { get; set; } = new List<DirectRefreshToken>();
    public ICollection<DirectUserLicense> Licenses { get; set; } = new List<DirectUserLicense>();
}

public sealed class DirectRefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string DeviceIdentifierHash { get; set; } = string.Empty;
    public string? CreatedByIpHash { get; set; }
    public string? RevokedByIpHash { get; set; }
    public DirectUser User { get; set; } = null!;
}

public sealed class DirectPlan
{
    public int Id { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public ICollection<DirectUserLicense> UserLicenses { get; set; } = new List<DirectUserLicense>();
}

public sealed class DirectUserLicense
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int PlanId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DirectUser User { get; set; } = null!;
    public DirectPlan Plan { get; set; } = null!;
}

public sealed class DirectUserDevice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string DeviceIdentifierHash { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DirectAuthAuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool IsSuccessful { get; set; }
    public string? DetailCode { get; set; }
    public string? IpAddressHash { get; set; }
    public string? DeviceIdentifierHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class DirectActivationToken
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public int PlanId { get; set; }
    public int PlanDurationDays { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
    public Guid? RedeemedByUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? Note { get; set; }
    public DirectPlan Plan { get; set; } = null!;
}

public sealed class DirectAccountUsageEvent
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
