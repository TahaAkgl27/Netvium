namespace Netvium.Identity.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed record IdentityConfigurationState(bool DatabaseConfigured, bool JwtConfigured)
{
    public bool IsConfigured => DatabaseConfigured && JwtConfigured;
}
