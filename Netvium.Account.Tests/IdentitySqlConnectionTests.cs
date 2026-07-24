using Microsoft.Data.SqlClient;
using Netvium.Services.Account.DirectSql;
using Xunit;

namespace Netvium.Account.Tests;

public sealed class IdentitySqlConnectionTests
{
    [Fact]
    public void BuildConnectionString_UsesEncryptedSqlConnection()
    {
        var options = new IdentitySqlConnectionOptions(
            "sql.example.test", "NetviumIdentity", false, "netvium_user", "secret-value", true);
        var builder = new SqlConnectionStringBuilder(options.BuildConnectionString());
        Assert.Equal("sql.example.test", builder.DataSource);
        Assert.Equal("NetviumIdentity", builder.InitialCatalog);
        Assert.True(builder.Encrypt);
        Assert.False(builder.PersistSecurityInfo);
        Assert.Equal("netvium_user", builder.UserID);
    }

    [Fact]
    public async Task ConnectionStore_RoundTripsThroughDpapi()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NetviumAccountTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new IdentitySqlConnectionStore(directory);
            var expected = new IdentitySqlConnectionOptions(
                "sql.example.test", "NetviumIdentity", false, "netvium_user", "secret-value", true);
            await store.WriteAsync(expected);
            var actual = await store.ReadAsync();
            Assert.Equal(expected, actual);
            var persisted = await File.ReadAllTextAsync(Path.Combine(directory, "sql-connection.dat"));
            Assert.DoesNotContain("secret-value", persisted);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
