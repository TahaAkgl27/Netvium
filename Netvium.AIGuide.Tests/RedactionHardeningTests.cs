using Netvium.Helpers.Logging;
using Netvium.Services.AIGuide;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class RedactionHardeningTests
{
    [Theory]
    [InlineData("AKIA")]
    [InlineData("ASIA")]
    [InlineData("AIDA")]
    public void AwsAccessKeys_AreRedacted(string prefix)
    {
        var syntheticKey = prefix + "ABCDEFGHIJKLMNOP";
        var ai = AIGuideSecretRedactor.Redact(syntheticKey);
        var log = SensitiveDataRedactor.Redact(syntheticKey);
        Assert.True(!ai.Contains(syntheticKey, StringComparison.Ordinal), "AI redactor retained an AWS access key.");
        Assert.True(!log.Contains(syntheticKey, StringComparison.Ordinal), "Log redactor retained an AWS access key.");
    }

    [Fact]
    public void PemPrivateKeyBlock_IsEntirelyRedacted()
    {
        var fixture = "-----BEGIN " + "PRIVATE KEY-----\nSYNTHETIC_NOT_A_REAL_KEY_1234567890\nLINE_TWO\n-----END " + "PRIVATE KEY-----";
        var ai = AIGuideSecretRedactor.Redact(fixture);
        var log = SensitiveDataRedactor.Redact(fixture);
        Assert.True(!ai.Contains("SYNTHETIC_NOT_A_REAL_KEY", StringComparison.Ordinal), "AI redactor retained PEM content.");
        Assert.True(!log.Contains("SYNTHETIC_NOT_A_REAL_KEY", StringComparison.Ordinal), "Log redactor retained PEM content.");
        Assert.DoesNotContain("BEGIN PRIVATE KEY", ai, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", log, StringComparison.Ordinal);
    }

    [Fact]
    public void AwsSecretDomainUserAndContextualHostname_AreRedacted()
    {
        const string fixture = "aws_secret_access_key=syntheticSecretValue DOMAIN\\synthetic.user hostname=synthetic-host";
        var ai = AIGuideSecretRedactor.Redact(fixture);
        var log = SensitiveDataRedactor.Redact(fixture);
        foreach (var value in new[] { ai, log })
        {
            Assert.DoesNotContain("syntheticSecretValue", value, StringComparison.Ordinal);
            Assert.DoesNotContain("DOMAIN\\synthetic.user", value, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-host", value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OrdinaryLanguageAndUnlabelledWords_AreNotOverRedacted()
    {
        const string fixture = "The server is healthy. Sunucu bak\u0131m\u0131 tamamland\u0131. folder\\file and blue-widget are labels.";
        Assert.Equal(fixture, AIGuideSecretRedactor.Redact(fixture));
        Assert.Equal(fixture, SensitiveDataRedactor.Redact(fixture));
    }

    [Fact]
    public void LongAdversarialInput_CompletesSafely()
    {
        var fixture = "hostname=" + new string('a', 200_000) + " password=" + new string('x', 200_000);
        var ai = AIGuideSecretRedactor.Redact(fixture);
        var log = SensitiveDataRedactor.Redact(fixture);
        Assert.False(string.IsNullOrEmpty(ai));
        Assert.False(string.IsNullOrEmpty(log));
    }
}
