using Netvium.Models;
using Netvium.Rdp.Clean;
using Netvium.ViewModels;
using Netvium.Services;
using Xunit;

namespace Netvium.Account.Tests;

public sealed class RdpPhase6ATests
{
    [Theory]
    [InlineData("password=my secret pass")]
    [InlineData("password=\"my secret pass\"")]
    [InlineData("password='my secret pass'")]
    [InlineData("password: my secret pass")]
    [InlineData("pwd=my secret pass token=abc def ghi")]
    [InlineData("api_key=abc def ghi username=user name")]
    public void Redactor_hides_complete_multiword_values(string input)
    {
        var output = RdpLogRedactor.Redact(input);
        Assert.DoesNotContain("my secret pass", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc def ghi", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Redactor_masks_nested_connection_string_and_endpoint()
    {
        var output = RdpLogRedactor.Redact(
            "connectionString=Server=db;Password=my secret pass;User Id=user name; host=10.40.0.2:3389");
        Assert.DoesNotContain("my secret pass", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user name", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10.40.0.2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Redactor_preserves_safe_text()
    {
        var output = RdpLogRedactor.Redact("RDP bağlantısı zaman aşımına uğradı");
        Assert.Equal("RDP bağlantısı zaman aşımına uğradı", output);
    }

    [Fact]
    public void Redactor_hides_domain_user_and_multiple_fields()
    {
        var output = RdpLogRedactor.Redact("domain\\username password=first second token=third fourth");
        Assert.DoesNotContain("domain\\username", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first second", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("third fourth", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clean_session_dispose_is_idempotent_without_host()
    {
        using var session = new RdpCleanSession();
        session.Dispose();
        session.Dispose();
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void PowerShell_bootstrap_contains_no_runtime_secret_or_encoded_command()
    {
        var sentinel = "S3cret" + " Value With Spaces!_FAZ6A";
        var bootstrap = SecurePowerShellLauncher.BuildBootstrapScript();
        Assert.DoesNotContain(sentinel, bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("EncodedCommand", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Console]::In", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ReadLine", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShell_bootstrap_uses_runtime_secure_string_conversion()
    {
        var bootstrap = SecurePowerShellLauncher.BuildBootstrapScript();
        Assert.Contains("ConvertTo-SecureString $plainPassword -AsPlainText -Force", bootstrap, StringComparison.Ordinal);
        Assert.Contains("PSCredential", bootstrap, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Disposed_view_model_does_not_start_a_late_connect()
    {
        var vm = RdpSessionViewModel.Create(new ServerInfo("test", "127.0.0.1", "user", "pass", "", 3389, 100));
        vm.Dispose();
        await vm.StartConnectAsync();
        Assert.True(vm.CleanSession.IsDisposed);
    }

    [Fact]
    public void Disposing_one_session_does_not_dispose_another()
    {
        using var first = new RdpCleanSession();
        using var second = new RdpCleanSession();
        first.Dispose();
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }
}