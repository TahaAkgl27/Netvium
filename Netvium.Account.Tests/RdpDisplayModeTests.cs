using Netvium.Rdp;
using Netvium.Rdp.Clean;
using Xunit;

namespace Netvium.Account.Tests;

public sealed class RdpDisplayModeTests
{
    [Fact]
    public void NativeIsTheSafeDefaultAndDoesNotEnableSmartSizing()
    {
        var mode = default(RdpDisplayMode);

        Assert.Equal(RdpDisplayMode.Native, mode);
        Assert.False(RdpDisplayModePolicy.IsSmartSizingEnabled(mode));
        Assert.Equal("Native", RdpDisplayModePolicy.GetLabel(mode));
    }

    [Fact]
    public void FitAndNativeTransitionsMapToTheExpectedSmartSizingValue()
    {
        Assert.True(RdpDisplayModePolicy.IsSmartSizingEnabled(RdpDisplayMode.Fit));
        Assert.False(RdpDisplayModePolicy.IsSmartSizingEnabled(RdpDisplayMode.Native));
        Assert.Equal("Fit", RdpDisplayModePolicy.GetLabel(RdpDisplayMode.Fit));
    }

    [Fact]
    public void SessionModeSurvivesHostAbsenceUntilReconnect()
    {
        using var session = new RdpCleanSession();

        Assert.Equal(RdpDisplayMode.Native, session.DisplayMode);
        Assert.False(session.TryGetSmartSizingEnabled());

        session.SetDisplayMode(RdpDisplayMode.Fit);
        Assert.Equal(RdpDisplayMode.Fit, session.DisplayMode);
        Assert.True(session.TryGetSmartSizingEnabled());

        session.SetDisplayMode(RdpDisplayMode.Native);
        Assert.False(session.TryGetSmartSizingEnabled());
    }
    [Fact]
    public void NativeViewportUsesPhysicalPixelsAfterDipConversion()
    {
        var plan = RdpViewportSizing.Compute(1280, 720, 1.5, 1.25, RdpResolutionProfile.Native);

        Assert.Equal(1920, plan.ViewportPixelWidth);
        Assert.Equal(900, plan.ViewportPixelHeight);
        Assert.Equal(plan.ViewportPixelWidth, plan.RequestedDesktopWidth);
        Assert.Equal(plan.ViewportPixelHeight, plan.RequestedDesktopHeight);
    }

    [Fact]
    public void SamePhysicalRequestIsSkippedEvenWhenModeIsUnchanged()
    {
        Assert.True(RdpViewportUpdatePolicy.IsSamePhysicalRequest(1920, 900, 150, 150, 1920, 900, 150, 150));
        Assert.False(RdpViewportUpdatePolicy.IsSamePhysicalRequest(1920, 900, 150, 150, 1919, 900, 150, 150));
        Assert.False(RdpViewportUpdatePolicy.IsSamePhysicalRequest(1920, 900, 150, 150, 1920, 900, 125, 150));
    }

    [Fact]
    public void NewerResizeGenerationWinsOverQueuedOlderResize()
    {
        Assert.False(RdpViewportUpdatePolicy.IsLatest(4, 5));
        Assert.True(RdpViewportUpdatePolicy.IsLatest(5, 5));
    }
}