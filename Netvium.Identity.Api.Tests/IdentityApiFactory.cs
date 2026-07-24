using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Netvium.Identity.Api.Configuration;
using Netvium.Identity.Api.Domain;
using Netvium.Identity.Api.Infrastructure;

namespace Netvium.Identity.Api.Tests;

public sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "identity-tests-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting("ConnectionStrings:NetviumIdentity", "test-only-placeholder");
        builder.UseSetting("Jwt:Issuer", "netvium-tests");
        builder.UseSetting("Jwt:Audience", "netvium-desktop-tests");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IdentityDbContext>();
            services.AddDbContext<IdentityDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            services.RemoveAll<ISchemaContractValidator>();
            services.AddScoped<ISchemaContractValidator, AlwaysValidSchemaContractValidator>();
            services.RemoveAll<IdentityConfigurationState>();
            services.AddSingleton(new IdentityConfigurationState(true, true));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Code = "FREE", DisplayName = "Free", IsActive = true });
            db.SaveChanges();
        });
    }

    private sealed class AlwaysValidSchemaContractValidator : ISchemaContractValidator
    {
        public Task<SchemaValidationResult> ValidateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SchemaValidationResult.Valid);
    }
}
