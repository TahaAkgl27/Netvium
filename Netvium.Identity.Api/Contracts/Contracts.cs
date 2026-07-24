using System.ComponentModel.DataAnnotations;

namespace Netvium.Identity.Api.Contracts;

public sealed record RegisterRequest(
    [property: Required, EmailAddress, MaxLength(320)] string Email,
    [property: Required, MaxLength(120)] string DisplayName,
    [property: Required, MaxLength(256)] string Password);

public sealed record LoginRequest(
    [property: Required, EmailAddress, MaxLength(320)] string Email,
    [property: Required, MaxLength(256)] string Password,
    [property: Required, MaxLength(200)] string DeviceIdentifier,
    [property: Required, MaxLength(200)] string DeviceName);

public sealed record RefreshRequest([property: Required, MaxLength(512)] string RefreshToken);
public sealed record LogoutRequest([property: Required, MaxLength(512)] string RefreshToken);

public sealed record AccountResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsEmailVerified,
    string PlanCode,
    string PlanDisplayName,
    string LicenseStatus,
    DateTime? LicenseExpiresAtUtc);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    AccountResponse Account);

public sealed record RegistrationResponse(Guid Id, string Email, string DisplayName);
public sealed record ApiError(string Code, string Message);
