using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Netvium.Identity.Api.Configuration;
using Netvium.Identity.Api.Contracts;
using Netvium.Identity.Api.Domain;
using Netvium.Identity.Api.Infrastructure;
using Netvium.Identity.Api.Security;
using Netvium.Identity.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var connectionString = builder.Configuration.GetConnectionString("NetviumIdentity");
var configuredJwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var databaseConfigured = !string.IsNullOrWhiteSpace(connectionString);
var jwtConfigured = !string.IsNullOrWhiteSpace(configuredJwt.Issuer)
    && !string.IsNullOrWhiteSpace(configuredJwt.Audience)
    && Encoding.UTF8.GetByteCount(configuredJwt.SigningKey ?? string.Empty) >= 32;

if (!jwtConfigured)
{
    configuredJwt.Issuer = "unconfigured";
    configuredJwt.Audience = "unconfigured";
    configuredJwt.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
configuredJwt.AccessTokenMinutes = 15;
configuredJwt.RefreshTokenDays = 30;

builder.Services.AddSingleton(new IdentityConfigurationState(databaseConfigured, jwtConfigured));
builder.Services.Configure<JwtOptions>(options =>
{
    options.Issuer = configuredJwt.Issuer;
    options.Audience = configuredJwt.Audience;
    options.SigningKey = configuredJwt.SigningKey!;
    options.AccessTokenMinutes = 15;
    options.RefreshTokenDays = 30;
});
builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(connectionString ?? string.Empty));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISchemaContractValidator, SchemaContractValidator>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuredJwt.SigningKey!)),
        ValidateIssuer = true,
        ValidIssuer = configuredJwt.Issuer,
        ValidateAudience = true,
        ValidAudience = configuredJwt.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ApiError("AUTH_REQUIRED", "Oturum gerekli."));
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
        await context.HttpContext.Response.WriteAsJsonAsync(new ApiError("RATE_LIMITED", "Çok fazla istek gönderildi."), cancellationToken);
    options.AddPolicy("register", context => PerIp(context, 5, TimeSpan.FromMinutes(10)));
    options.AddPolicy("login", context => PerIp(context, 10, TimeSpan.FromMinutes(5)));
    options.AddPolicy("refresh", context => PerIp(context, 30, TimeSpan.FromMinutes(5)));
});
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var badRequest = error is BadHttpRequestException;
    context.Response.StatusCode = badRequest ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(badRequest
        ? new ApiError("REQUEST_INVALID", "İstek biçimi geçersiz.")
        : new ApiError("INTERNAL_ERROR", "İşlem tamamlanamadı."));
}));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (IdentityConfigurationState state, ISchemaContractValidator validator, CancellationToken cancellationToken) =>
{
    if (!state.IsConfigured)
        return Results.Json(new { status = "unhealthy", code = "IDENTITY_NOT_CONFIGURED" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    var schema = await validator.ValidateAsync(cancellationToken);
    return schema.IsValid
        ? Results.Ok(new { status = "healthy", database = "connected", schema = "valid" })
        : Results.Json(new { status = "unhealthy", code = "DATABASE_OR_SCHEMA_UNAVAILABLE" }, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

var auth = app.MapGroup("/api/auth");
auth.MapPost("/register", async (RegisterRequest request, HttpContext context, IdentityConfigurationState state, ISchemaContractValidator schema, IAuthService service, CancellationToken cancellationToken) =>
{
    var unavailable = await EnsureReadyAsync(state, schema, cancellationToken);
    if (unavailable is not null) return unavailable;
    var result = await service.RegisterAsync(request, RemoteIp(context), cancellationToken);
    return result.Succeeded
        ? Results.Created($"/api/account/{result.Value!.Id}", result.Value)
        : ErrorResult(result.Error!);
}).RequireRateLimiting("register");

auth.MapPost("/login", async (LoginRequest request, HttpContext context, IdentityConfigurationState state, ISchemaContractValidator schema, IAuthService service, CancellationToken cancellationToken) =>
{
    var unavailable = await EnsureReadyAsync(state, schema, cancellationToken);
    if (unavailable is not null) return unavailable;
    var result = await service.LoginAsync(request, RemoteIp(context), cancellationToken);
    return result.Succeeded ? Results.Ok(result.Value) : ErrorResult(result.Error!);
}).RequireRateLimiting("login");

auth.MapPost("/refresh", async (RefreshRequest request, HttpContext context, IdentityConfigurationState state, ISchemaContractValidator schema, IAuthService service, CancellationToken cancellationToken) =>
{
    var unavailable = await EnsureReadyAsync(state, schema, cancellationToken);
    if (unavailable is not null) return unavailable;
    var result = await service.RefreshAsync(request, RemoteIp(context), cancellationToken);
    return result.Succeeded ? Results.Ok(result.Value) : ErrorResult(result.Error!);
}).RequireRateLimiting("refresh");

auth.MapPost("/logout", async (LogoutRequest request, HttpContext context, IdentityConfigurationState state, ISchemaContractValidator schema, IAuthService service, CancellationToken cancellationToken) =>
{
    var unavailable = await EnsureReadyAsync(state, schema, cancellationToken);
    if (unavailable is not null) return unavailable;
    await service.LogoutAsync(request, RemoteIp(context), cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/account/me", async (HttpContext context, IdentityConfigurationState state, ISchemaContractValidator schema, IAuthService service, CancellationToken cancellationToken) =>
{
    var unavailable = await EnsureReadyAsync(state, schema, cancellationToken);
    if (unavailable is not null) return unavailable;
    var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
    if (!Guid.TryParse(subject, out var userId))
        return Results.Json(new ApiError("AUTH_REQUIRED", "Oturum gerekli."), statusCode: StatusCodes.Status401Unauthorized);
    var result = await service.GetAccountAsync(userId, cancellationToken);
    return result.Succeeded ? Results.Ok(result.Value) : ErrorResult(result.Error!);
}).RequireAuthorization();

app.Run();

static RateLimitPartition<string> PerIp(HttpContext context, int permitLimit, TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0, AutoReplenishment = true });

static string? RemoteIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

static async Task<IResult?> EnsureReadyAsync(IdentityConfigurationState state, ISchemaContractValidator validator, CancellationToken cancellationToken)
{
    if (!state.IsConfigured)
        return Results.Json(new ApiError("SERVICE_UNAVAILABLE", "Hesap servisi yapılandırılmamış."), statusCode: StatusCodes.Status503ServiceUnavailable);
    var validation = await validator.ValidateAsync(cancellationToken);
    return validation.IsValid
        ? null
        : Results.Json(new ApiError("SCHEMA_CONTRACT_MISMATCH", "Hesap servisi veritabanı sözleşmesi doğrulanamadı."), statusCode: StatusCodes.Status503ServiceUnavailable);
}

static IResult ErrorResult(ApiError error) => error.Code switch
{
    "AUTH_INVALID_CREDENTIALS" or "REFRESH_TOKEN_INVALID" or "AUTH_REQUIRED" => Results.Json(error, statusCode: StatusCodes.Status401Unauthorized),
    "EMAIL_ALREADY_REGISTERED" => Results.Json(error, statusCode: StatusCodes.Status409Conflict),
    "SERVICE_CONFIGURATION_ERROR" => Results.Json(error, statusCode: StatusCodes.Status503ServiceUnavailable),
    "ACCOUNT_NOT_FOUND" => Results.Json(error, statusCode: StatusCodes.Status404NotFound),
    _ => Results.BadRequest(error)
};

public partial class Program;
