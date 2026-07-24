using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Netvium.Identity.Api.Contracts;
using Netvium.Identity.Api.Domain;
using Netvium.Identity.Api.Infrastructure;

namespace Netvium.Identity.Api.Tests;

public sealed class AuthEndpointsTests : IAsyncLifetime
{
    private readonly IdentityApiFactory _factory = new();
    private readonly HttpClient _client;

    public AuthEndpointsTests()
    {
        _client = _factory.CreateClient();
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Register_Succeeds_AndAssignsFreeLicense()
    {
        var email = Email();
        var response = await RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.Include(x => x.Licenses).ThenInclude(x => x.Plan).SingleAsync(x => x.Email == email);
        Assert.Contains(user.Licenses, x => x.Plan.Code == "FREE" && x.Status == "ACTIVE");
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmail()
    {
        var email = Email();
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(email)).StatusCode);
        var duplicate = await RegisterAsync("  " + email.ToUpperInvariant() + "  ");
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsWeakPassword()
    {
        var response = await RegisterAsync(Email(), "weak");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_Succeeds_AndWrongPasswordIsRejected()
    {
        var email = Email();
        await RegisterAsync(email);
        var success = await LoginAsync(email, "StrongPass123");
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var payload = await success.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(payload?.AccessToken);
        Assert.NotNull(payload?.RefreshToken);
        var failed = await LoginAsync(email, "WrongPass123");
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
    }

    [Fact]
    public async Task FiveFailedLogins_LockAccountForValidPassword()
    {
        var email = Email();
        await RegisterAsync(email);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "WrongPass123")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "StrongPass123")).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True((await db.Users.SingleAsync(x => x.Email == email)).LockoutEndUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task InactiveUser_IsRejected()
    {
        var email = Email();
        await RegisterAsync(email);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "StrongPass123")).StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndRejectsReuse()
    {
        var email = Email();
        await RegisterAsync(email);
        var login = await (await LoginAsync(email, "StrongPass123")).Content.ReadFromJsonAsync<AuthResponse>();
        var refreshedResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login!.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshedResponse.StatusCode);
        var refreshed = await refreshedResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotEqual(login.RefreshToken, refreshed!.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refreshed.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var email = Email();
        await RegisterAsync(email);
        var login = await (await LoginAsync(email, "StrongPass123")).Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login!.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task AccountMe_RequiresBearer_AndReturnsSafeProfile()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/account/me")).StatusCode);
        var email = Email();
        await RegisterAsync(email);
        var login = await (await LoginAsync(email, "StrongPass123")).Content.ReadFromJsonAsync<AuthResponse>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal(email, me!.Email);
        Assert.Equal("FREE", me.PlanCode);
    }

    [Fact]
    public async Task Database_StoresRefreshHash_NotRawToken()
    {
        var email = Email();
        await RegisterAsync(email);
        var login = await (await LoginAsync(email, "StrongPass123")).Content.ReadFromJsonAsync<AuthResponse>();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var stored = await db.RefreshTokens.SingleAsync(x => x.User.Email == email);
        Assert.NotEqual(login!.RefreshToken, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
    }

    [Fact]
    public async Task InvalidOrMalformedBodies_ReturnFixedErrors()
    {
        var missing = await _client.PostAsJsonAsync("/api/auth/register", new { email = "", displayName = "", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingError = await missing.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("REGISTRATION_INVALID", missingError!.Code);

        using var malformedContent = new StringContent("{not-json", System.Text.Encoding.UTF8, "application/json");
        var malformed = await _client.PostAsync("/api/auth/login", malformedContent);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var malformedError = await malformed.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("REQUEST_INVALID", malformedError!.Code);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email, string password = "StrongPass123") =>
        _client.PostAsJsonAsync("/api/auth/register", new { email, displayName = "Test User", password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password, deviceIdentifier = "test-device", deviceName = "Test Device" });

    private static string Email() => $"user-{Guid.NewGuid():N}@example.test";
}
