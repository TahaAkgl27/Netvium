namespace Netvium.Models.Account;

public sealed record AdminDashboardData(
    int TotalUsers, int ActiveUsers, int ActivePremiumUsers, int ActiveDevices,
    IReadOnlyList<AdminUserSummary> Users, IReadOnlyList<AdminActivationTokenSummary> Tokens,
    IReadOnlyList<AdminUsageEventSummary> UsageEvents, IReadOnlyList<AdminAuditEventSummary> AuditEvents);

public sealed record AdminUserSummary(Guid Id, string Email, string DisplayName, bool IsActive, bool IsAdmin,
    string PlanCode, string PlanDisplayName, DateTime? LicenseExpiresAtUtc, DateTime? LastLoginAtUtc, int DeviceCount)
{
    public string RemainingPlanTime => LicenseExpiresAtUtc is not { } expiresAt
        ? (PlanCode == "FREE" ? "Süresiz" : "Süresiz")
        : expiresAt <= DateTime.UtcNow
            ? "Süresi doldu"
            : $"{Math.Ceiling((expiresAt - DateTime.UtcNow).TotalDays)} gün kaldı";
}
public sealed record AdminActivationTokenSummary(Guid Id, string PlanCode, DateTime CreatedAtUtc,
    int PlanDurationDays, DateTime? ExpiresAtUtc, DateTime? RedeemedAtUtc, DateTime? RevokedAtUtc, string? Note);
public sealed record AdminUsageEventSummary(long Id, Guid? UserId, string EventType, string? Detail, int Quantity, DateTime CreatedAtUtc);
public sealed record AdminAuditEventSummary(long Id, Guid? UserId, string EventType, bool IsSuccessful, string? DetailCode, DateTime CreatedAtUtc);
public sealed record AdminActivationToken(string Code, string PlanCode, DateTime? ExpiresAtUtc);
