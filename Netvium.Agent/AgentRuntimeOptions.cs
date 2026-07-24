using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Netvium.Agent;

internal sealed record AgentRuntimeOptions(
    int Port,
    string BindAddress,
    string SharedKey,
    bool ServiceMode,
    string? KeyFilePath,
    string? ConfigurationError = null,
    bool KeyFromStdin = false)
{
    public static string UnsupportedKeyArgumentMessage => "--key seçeneği güvenlik nedeniyle desteklenmiyor. --key-file veya --key-stdin kullanın.";

    public bool IsValid => Port is >= 1024 and <= 65535
        && !string.IsNullOrWhiteSpace(BindAddress)
        && !string.IsNullOrWhiteSpace(SharedKey)
        && string.IsNullOrWhiteSpace(ConfigurationError);

    public static AgentRuntimeOptions Parse(string[] args, bool isWindowsService) =>
        ParseAsync(args, isWindowsService, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<AgentRuntimeOptions> ParseAsync(
        string[] args,
        bool isWindowsService,
        CancellationToken cancellationToken)
    {
        var port = 9841;
        var bindAddress = "127.0.0.1";
        string? keyFile = null;
        var serviceMode = isWindowsService;
        var keyStdin = false;
        string? configurationError = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--service", StringComparison.OrdinalIgnoreCase))
                serviceMode = true;
            else if (arg.Equals("--console", StringComparison.OrdinalIgnoreCase))
                serviceMode = false;
            else if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out var parsedPort))
                port = parsedPort;
            else if (arg.Equals("--bind", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                bindAddress = args[++i];
            else if (arg.Equals("--key", StringComparison.OrdinalIgnoreCase))
            {
                // Consume the value without retaining or logging it. Plaintext command-line secrets are forbidden.
                if (i + 1 < args.Length)
                    i++;
                configurationError ??= UnsupportedKeyArgumentMessage;
            }
            else if (arg.Equals("--key-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                keyFile = args[++i];
            else if (arg.Equals("--key-stdin", StringComparison.OrdinalIgnoreCase))
                keyStdin = true;
        }

        if (keyStdin && keyFile != null)
            configurationError ??= "--key-file ve --key-stdin birlikte kullanılamaz.";

        string key = string.Empty;
        if (configurationError == null && keyStdin)
        {
            key = await ReadStdinKeyAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(key))
                configurationError = "Standart girdiden geçerli Agent anahtarı okunamadı.";
        }
        else if (configurationError == null && serviceMode)
        {
            keyFile ??= AgentSecretStore.DefaultServiceKeyPath;
            if (!AgentSecretStore.TryLoadProtectedKey(keyFile, out key))
                configurationError = "Servis anahtarı çözülemedi.";
        }
        else if (configurationError == null)
        {
            keyFile ??= AgentSecretStore.DefaultUserKeyPath;
            if (!AgentSecretStore.TryLoadCurrentUserProtectedKey(keyFile, out key))
                configurationError = "Kullanıcı Agent anahtarı çözülemedi.";
        }

        return new AgentRuntimeOptions(port, bindAddress, key, serviceMode, keyFile, configurationError, keyStdin);
    }

    private static async Task<string> ReadStdinKeyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // Require redirected stdin so the host controls echo and the read remains bounded.
            if (!Console.IsInputRedirected)
                return string.Empty;
            return (await Console.In.ReadLineAsync(timeout.Token).ConfigureAwait(false)) ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }
}