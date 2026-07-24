using Netvium.Models;
using Netvium.Rdp.Clean;
using Netvium.Services;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class Phase6EActivePathTests
{
    [Fact]
    public void LocalLicenseDat_IsNotAnEntitlementSource()
    {
        var temp = Path.Combine(Path.GetTempPath(), "Netvium-6e-license-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "license.dat"), "{\"Plan\":\"PremiumPlus\",\"Signature\":\"forged\"}");
            FeatureEntitlementService.Instance.SetAccountAuthenticated(false);
            Assert.False(FeatureEntitlementService.Instance.IsPremiumEnabled);
            Assert.False(FeatureEntitlementService.Instance.IsAiGuideEnabled);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
            FeatureEntitlementService.Instance.SetAccountAuthenticated(false);
        }
    }

    [Fact]
    public void AccountPlanIsTheOnlyActiveEntitlementSource()
    {
        FeatureEntitlementService.Instance.SetAccountAuthenticated(true, "PREMIUM");
        Assert.True(FeatureEntitlementService.Instance.IsPremiumEnabled);
        Assert.False(FeatureEntitlementService.Instance.IsAiGuideEnabled);
        FeatureEntitlementService.Instance.SetAccountAuthenticated(true, "PREMIUM_PLUS");
        Assert.True(FeatureEntitlementService.Instance.IsAiGuideEnabled);
        FeatureEntitlementService.Instance.SetAccountAuthenticated(false);
    }

    [Fact]
    public void CleanHostUsesNativeDisplayModeAndSecureSmartSizingPolicy()
    {
        using var host = new RdpSessionHostControl();
        Assert.Equal(RdpDisplayMode.Native, host.DisplayMode);
        Assert.False(host.TryGetSmartSizingEnabled());
        Assert.False(RdpDisplayModePolicy.IsSmartSizingEnabled(RdpDisplayMode.Native));
        Assert.True(RdpDisplayModePolicy.IsSmartSizingEnabled(RdpDisplayMode.Fit));
        Assert.Equal("Native", RdpDisplayModePolicy.GetLabel(RdpDisplayMode.Native));
    }

    [Fact]
    public void CleanHostRetryAndTimeoutSurfaceIsActive()
    {
        using var host = new RdpSessionHostControl { ConnectTimeoutSeconds = 17 };
        Assert.Equal(17, host.ConnectTimeoutSeconds);
        var failure = RdpFailureClassifier.Classify("session", "ConnectionTimeout");
        Assert.True(RdpRetryPolicy.ShouldRetry(failure, 1));
    }
}