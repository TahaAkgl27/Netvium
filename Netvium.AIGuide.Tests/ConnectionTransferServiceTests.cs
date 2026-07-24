using System.Text;
using Netvium.Models;
using Netvium.Rdp;
using Netvium.Services;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class ConnectionTransferServiceTests
{
    [Fact]
    public async Task NetviumXml_RoundTripsConnectionsFoldersFavoritesAndPasswords()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netvium-connections-{Guid.NewGuid():N}.xml");
        try
        {
            var source = new ServerInfo("Primary", "10.20.30.40", "admin", "secret-password", "Production", 3390, 125)
            {
                ReadabilityMode = RdpReadabilityMode.Large,
                ResolutionProfile = RdpResolutionProfile.FullHd
            };

            var exported = await ConnectionTransferService.ExportNetviumAsync(
                path,
                new[] { source },
                new[] { "Production", "Empty Folder" },
                new[] { "10.20.30.40:3390" },
                "transfer-password");

            Assert.Equal(1, exported);
            var withoutPassword = await ConnectionTransferService.ImportAsync(path);
            Assert.True(withoutPassword.PasswordRequired);

            var imported = await ConnectionTransferService.ImportAsync(path, "transfer-password");
            Assert.True(imported.Succeeded, imported.ErrorMessage);
            var server = Assert.Single(imported.Servers);
            Assert.Equal("Primary", server.Name);
            Assert.Equal("10.20.30.40", server.Ip);
            Assert.Equal("admin", server.Username);
            Assert.Equal("secret-password", server.Password);
            Assert.Equal("Production", server.Folder);
            Assert.Equal(3390, server.Port);
            Assert.Equal(125, server.Scale);
            Assert.Equal(RdpReadabilityMode.Large, server.ReadabilityMode);
            Assert.Equal(RdpResolutionProfile.FullHd, server.ResolutionProfile);
            Assert.Contains("Empty Folder", imported.Folders);
            Assert.Contains("10.20.30.40:3390", imported.FavoriteServerKeys);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task MRemoteNgXml_ImportsRdpAndSkipsUnsupportedProtocol_WithSixteenByteGcmNonce()
    {
        const string protectedVerifier = "PcSrhagQ2EQSrvKz27jkk5W7trkgmccrQdg2B1im9bfAUDJXTYl7IqZt/37mvH4olyHEEzQ+1Jl5jbrLbg8wZHYe";
        var path = Path.Combine(Path.GetTempPath(), $"mremote-connections-{Guid.NewGuid():N}.xml");
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Connections EncryptionEngine="AES" BlockCipherMode="GCM" KdfIterations="1000" FullFileEncryption="false" Protected="{protectedVerifier}" ConfVersion="2.6">
              <Node Name="Production" Type="Container" Protocol="RDP" Port="3389">
                <Node Name="RDP Server" Type="Connection" Hostname="10.0.0.10" Username="admin" Password="" Protocol="RDP" Port="3390" />
                <Node Name="SSH Server" Type="Connection" Hostname="10.0.0.11" Username="root" Password="" Protocol="SSH2" Port="22" />
              </Node>
            </Connections>
            """;

        try
        {
            await File.WriteAllTextAsync(path, xml, new UTF8Encoding(false));
            var imported = await ConnectionTransferService.ImportAsync(path);

            Assert.True(imported.Succeeded, imported.ErrorMessage);
            var server = Assert.Single(imported.Servers);
            Assert.Equal("RDP Server", server.Name);
            Assert.Equal("10.0.0.10", server.Ip);
            Assert.Equal(3390, server.Port);
            Assert.Equal("Production", server.Folder);
            Assert.Equal(1, imported.SkippedUnsupportedProtocols);

            var externalSample = Environment.GetEnvironmentVariable("NETVIUM_MREMOTE_SAMPLE");
            if (!string.IsNullOrWhiteSpace(externalSample) && File.Exists(externalSample))
            {
                var sampleImport = await ConnectionTransferService.ImportAsync(externalSample);
                Assert.True(sampleImport.Succeeded, sampleImport.ErrorMessage);
                Assert.NotEmpty(sampleImport.Servers);
                Assert.Equal(0, sampleImport.CredentialsNotImported);
            }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
