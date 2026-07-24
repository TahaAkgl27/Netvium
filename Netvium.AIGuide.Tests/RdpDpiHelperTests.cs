using Netvium.Rdp;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class RdpDpiHelperTests
{
    [Theory]
    [InlineData(100, 100)]
    [InlineData(119, 100)]
    [InlineData(120, 140)]
    [InlineData(150, 140)]
    [InlineData(159, 140)]
    [InlineData(160, 180)]
    [InlineData(200, 180)]
    public void NormalizeDeviceScalePercent_ReturnsProtocolSupportedValue(int input, int expected)
    {
        Assert.Equal(expected, RdpDpiHelper.NormalizeDeviceScalePercent(input));
    }

    [Theory]
    [InlineData(1920, 96, 508)]
    [InlineData(3348, 144, 591)]
    [InlineData(0, 144, 10)]
    public void PixelsToPhysicalMillimeters_UsesDpiAndProtocolBounds(int pixels, int dpi, uint expected)
    {
        Assert.Equal(expected, RdpDpiHelper.PixelsToPhysicalMillimeters(pixels, dpi));
    }
}