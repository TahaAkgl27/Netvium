using Netvium.Rdp;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class RdpConnectDeadlineTests
{
    [Fact]
    public void DefaultTimeout_IsOneHundredTwentySeconds()
    {
        Assert.Equal(120, RdpConnectDeadline.DefaultTimeoutSeconds);
        Assert.Equal(120, RdpConnectDeadline.ConnectPhaseTimeoutSeconds);
    }

    [Fact]
    public void IsExpired_ReturnsTrueAfterDeadline()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(-1);
        Assert.True(RdpConnectDeadline.IsExpired(deadline));
    }

    [Fact]
    public void IsExpired_ReturnsFalseBeforeDeadline()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Assert.False(RdpConnectDeadline.IsExpired(deadline));
    }

    [Fact]
    public void RemainingSeconds_IsZeroWhenExpired()
    {
        var deadline = DateTime.UtcNow.AddSeconds(-2);
        Assert.Equal(0, RdpConnectDeadline.RemainingSeconds(deadline));
    }

    [Theory]
    [InlineData(RdpConnectionState.Connecting, false)]
    [InlineData(RdpConnectionState.Connected, false)]
    [InlineData(RdpConnectionState.LoginComplete, true)]
    [InlineData(RdpConnectionState.Failed, true)]
    [InlineData(RdpConnectionState.TimedOut, true)]
    [InlineData(RdpConnectionState.Disconnected, true)]
    [InlineData(RdpConnectionState.Cancelled, true)]
    public void ConnectionState_TerminalFlags(RdpConnectionState state, bool isTerminal)
    {
        Assert.Equal(isTerminal, state.IsTerminal());
    }

    [Theory]
    [InlineData(RdpConnectionState.Connecting, true)]
    [InlineData(RdpConnectionState.Connected, true)]
    [InlineData(RdpConnectionState.LoginComplete, false)]
    [InlineData(RdpConnectionState.Failed, false)]
    public void ConnectionState_InProgressFlags(RdpConnectionState state, bool inProgress)
    {
        Assert.Equal(inProgress, state.IsInProgress());
    }

    [Fact]
    public void DiagnosticsReport_ContainsRequiredFields()
    {
        var d = new RdpConnectionAttemptDiagnostics
        {
            ServerName = "WebApp",
            Host = "10.0.0.1",
            Port = 3389,
            HasUsername = true,
            ConnectStartedUtc = DateTime.UtcNow,
            LastEvent = "OnConnecting",
            FinalState = RdpConnectionState.TimedOut,
            FailureReason = "Connection timed out. Check RDP service, firewall, credentials, or network."
        };

        var report = d.ToReport();
        Assert.Contains("WebApp", report);
        Assert.Contains("10.0.0.1", report);
        Assert.Contains("3389", report);
        Assert.Contains("Username provided: yes", report);
        Assert.Contains("OnConnecting", report);
        Assert.Contains("TimedOut", report);
        Assert.DoesNotContain("password", report, StringComparison.OrdinalIgnoreCase);
    }
}
