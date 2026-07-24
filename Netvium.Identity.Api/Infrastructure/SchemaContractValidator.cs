using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Netvium.Identity.Api.Infrastructure;

public sealed record SchemaValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static SchemaValidationResult Valid { get; } = new(true, Array.Empty<string>());
}

public interface ISchemaContractValidator
{
    Task<SchemaValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}

public sealed class SchemaContractValidator(
    IdentityDbContext db,
    Netvium.Identity.Api.Configuration.IdentityConfigurationState configuration) : ISchemaContractValidator
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredColumns =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Users"] = ["Id", "Email", "NormalizedEmail", "DisplayName", "PasswordHash", "IsEmailVerified", "IsActive", "FailedLoginCount", "LockoutEndUtc", "LastLoginAtUtc", "CreatedAtUtc", "UpdatedAtUtc"],
            ["RefreshTokens"] = ["Id", "UserId", "TokenHash", "TokenFamilyId", "CreatedAtUtc", "ExpiresAtUtc", "UsedAtUtc", "RevokedAtUtc", "ReplacedByTokenId", "DeviceIdentifier", "DeviceName"],
            ["PasswordResetTokens"] = ["Id", "UserId", "TokenHash", "CreatedAtUtc", "ExpiresAtUtc"],
            ["Plans"] = ["Id", "Code", "DisplayName", "IsActive"],
            ["UserLicenses"] = ["Id", "UserId", "PlanId", "Status", "StartsAtUtc", "ExpiresAtUtc", "CreatedAtUtc"],
            ["UserDevices"] = ["Id", "UserId", "DeviceIdentifier", "DeviceName", "FirstSeenAtUtc", "LastSeenAtUtc", "IsActive"],
            ["AuthAuditLogs"] = ["Id", "UserId", "EventType", "Success", "FailureCode", "IpAddress", "DeviceIdentifier", "OccurredAtUtc"]
        };

    public async Task<SchemaValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.DatabaseConfigured)
            return new(false, ["DATABASE_NOT_CONFIGURED"]);

        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo'";
            var actual = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.GetString(0);
                var column = reader.GetString(1);
                if (!actual.TryGetValue(table, out var columns))
                    actual[table] = columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                columns.Add(column);
            }

            var errors = new List<string>();
            foreach (var (table, columns) in RequiredColumns)
            {
                if (!actual.TryGetValue(table, out var actualColumns))
                {
                    errors.Add($"MISSING_TABLE:{table}");
                    continue;
                }
                errors.AddRange(columns.Where(column => !actualColumns.Contains(column)).Select(column => $"MISSING_COLUMN:{table}.{column}"));
            }
            return new(errors.Count == 0, errors);
        }
        catch
        {
            return new(false, ["DATABASE_UNAVAILABLE"]);
        }
    }
}
