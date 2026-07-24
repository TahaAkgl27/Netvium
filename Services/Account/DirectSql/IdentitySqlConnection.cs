using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Netvium.Helpers;

namespace Netvium.Services.Account.DirectSql;

public sealed record IdentitySqlConnectionOptions(
    string Server,
    string Database,
    bool UseWindowsAuthentication,
    string UserName,
    string Password,
    bool TrustServerCertificate)
{
    public string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Database))
            throw new InvalidOperationException("Sunucu ve veritabanı alanları zorunludur.");
        if (!UseWindowsAuthentication && (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password)))
            throw new InvalidOperationException("SQL kullanıcı adı ve parolası zorunludur.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server.Trim(),
            InitialCatalog = Database.Trim(),
            IntegratedSecurity = UseWindowsAuthentication,
            Encrypt = true,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = 8,
            PersistSecurityInfo = false,
            ApplicationName = "Netvium Desktop"
        };
        if (!UseWindowsAuthentication)
        {
            builder.UserID = UserName.Trim();
            builder.Password = Password;
        }
        return builder.ConnectionString;
    }
}

public sealed class IdentitySqlConnectionStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IdentitySqlConnectionStore(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netvium", "Identity");
        _path = Path.Combine(directory, "sql-connection.dat");
    }

    public async Task<IdentitySqlConnectionOptions?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var protectedJson = await File.ReadAllTextAsync(_path, cancellationToken);
            if (!CredentialProtector.TryUnprotect(protectedJson, out var json)) return null;
            return JsonSerializer.Deserialize<IdentitySqlConnectionOptions>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteAsync(IdentitySqlConnectionOptions options, CancellationToken cancellationToken = default)
    {
        _ = options.BuildConnectionString();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(options, JsonOptions);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, CredentialProtector.Protect(json), Encoding.UTF8, cancellationToken);
        File.Move(temp, _path, true);
    }
}

public sealed record IdentitySqlConnectionTestResult(bool Succeeded, string Message);

public static class IdentitySqlConnectionTester
{
    public static async Task<IdentitySqlConnectionTestResult> TestAsync(
        IdentitySqlConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(options.BuildConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME IN ('Users','RefreshTokens','PasswordResetTokens','Plans','UserLicenses','UserDevices','AuthAuditLogs');
                """;
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count != 7)
                return new(false, "Sunucuya bağlanıldı ancak NetviumIdentity tabloları eksik veya yanlış veritabanı seçildi.");
            return new(true, "Bağlantı testi başarılı. Ayarları uygulamak için “Kaydet ve Devam Et” seçeneğine tıklayın.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "SQL bağlantısı zaman aşımına uğradı.");
        }
        catch (SqlException ex) when (ex.Number == 18456)
        {
            return new(false,
                "SQL kullanici adi veya parolasi hatali ya da kullanici devre disi. SQL kimlik bilgilerini kontrol edin.");
        }
        catch (SqlException ex) when (ex.Number == 229)
        {
            return new(false,
                "SQL kullanicisinin NetviumIdentity veritabanina erisim yetkisi yok.");
        }
        catch (SqlException ex) when (ex.Number is 208 or 207)
        {
            return new(false,
                "NetviumIdentity tablolari eksik veya uygulamayla uyumlu degil.");
        }
        catch (SqlException ex) when (ex.Number is -2 or 0)
        {
            return new(false,
                "SQL Server yanit vermiyor. Sunucu adresini, TCP/IP protokolunu ve 1433 port yonlendirmesini kontrol edin.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return new(false, "SQL Server'a bağlanılamadı. Sunucu, yetki ve güvenlik duvarı ayarlarını kontrol edin.");
        }
    }
}
