namespace Netvium.Models.Account;

public sealed record AccountUser(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsEmailVerified,
    string PlanCode,
    string PlanDisplayName,
    string LicenseStatus,
    DateTime? LicenseExpiresAtUtc,
    bool IsAdmin);

public sealed record AccountSession(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    AccountUser User);

public enum AccountResultStatus
{
    Succeeded,
    CreatedOnline,
    StagedOffline,
    Failed,
    AlreadyExists,
    ValidationFailed
}

public sealed record AccountResult<T>(T? Value, string? ErrorCode, string? ErrorMessage, AccountResultStatus Status = AccountResultStatus.Succeeded)
{
    public bool Succeeded => ErrorCode is null;
    public static AccountResult<T> Success(T value, AccountResultStatus status = AccountResultStatus.Succeeded) => new(value, null, null, status);
    public static AccountResult<T> Failure(string code, string message, AccountResultStatus status = AccountResultStatus.Failed) => new(default, code, message, status);
}