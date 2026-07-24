using System.IO;
using System.Security.Cryptography;
using System.Text;
using Netvium.Helpers;

namespace Netvium.Services.Account;

public sealed class DpapiRefreshTokenStore : IRefreshTokenStore
{
    private readonly string _path;

    public DpapiRefreshTokenStore(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netvium", "Identity");
        _path = Path.Combine(directory, "refresh.dat");
    }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        var protectedValue = await File.ReadAllTextAsync(_path, cancellationToken);
        if (CredentialProtector.TryUnprotect(protectedValue, out var token) && !string.IsNullOrWhiteSpace(token))
            return token;
        await ClearAsync(cancellationToken);
        return null;
    }

    public async Task WriteAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var tempPath = _path + ".tmp";
        await File.WriteAllTextAsync(tempPath, CredentialProtector.Protect(refreshToken), Encoding.UTF8, cancellationToken);
        File.Move(tempPath, _path, true);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path))
            File.Delete(_path);
        return Task.CompletedTask;
    }
}

public sealed class DpapiDeviceIdentityProvider : IDeviceIdentityProvider
{
    private readonly string _path;

    public DpapiDeviceIdentityProvider(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netvium", "Identity");
        _path = Path.Combine(directory, "device.dat");
    }

    public async Task<(string Identifier, string Name)> GetAsync(CancellationToken cancellationToken = default)
    {
        var identifier = string.Empty;
        if (File.Exists(_path))
        {
            var protectedValue = await File.ReadAllTextAsync(_path, cancellationToken);
            CredentialProtector.TryUnprotect(protectedValue, out identifier);
        }
        if (string.IsNullOrWhiteSpace(identifier))
        {
            identifier = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, CredentialProtector.Protect(identifier), Encoding.UTF8, cancellationToken);
        }
        return (identifier, Environment.MachineName);
    }
}
