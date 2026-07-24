using Xunit;
using Netvium.Rdp.Clean;

namespace Netvium.Account.Tests;

public sealed class RdpFailurePolicyTests
{
    [Theory]
    [InlineData(RdpFailureCategory.NetworkUnavailable)]
    [InlineData(RdpFailureCategory.ConnectionTimeout)]
    [InlineData(RdpFailureCategory.TransportDisconnected)]
    [InlineData(RdpFailureCategory.HostUnavailable)]
    public void Temporary_failures_are_retryable(RdpFailureCategory category)
    {
        var failure = RdpFailureClassifier.Classify("session", "test", categoryHint: category);
        Assert.True(failure.IsRetryable);
        Assert.True(RdpRetryPolicy.ShouldRetry(failure, 1));
        Assert.True(RdpRetryPolicy.ShouldRetry(failure, 2));
        Assert.False(RdpRetryPolicy.ShouldRetry(failure, 3));
    }

    [Theory]
    [InlineData(RdpFailureCategory.AuthenticationFailed)]
    [InlineData(RdpFailureCategory.AccountRestricted)]
    [InlineData(RdpFailureCategory.CertificateOrSecurityFailure)]
    [InlineData(RdpFailureCategory.PolicyDenied)]
    [InlineData(RdpFailureCategory.ClientFatalError)]
    [InlineData(RdpFailureCategory.UserInitiatedDisconnect)]
    [InlineData(RdpFailureCategory.SessionClosed)]
    [InlineData(RdpFailureCategory.Unknown)]
    public void Permanent_failures_are_not_retried(RdpFailureCategory category)
    {
        var failure = RdpFailureClassifier.Classify("session", "test", categoryHint: category);
        Assert.False(failure.IsRetryable);
        Assert.False(RdpRetryPolicy.ShouldRetry(failure, 1));
    }

    [Fact]
    public void Retry_delays_are_exactly_two_and_five_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), RdpRetryPolicy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(5), RdpRetryPolicy.GetDelay(2));
        Assert.Equal(TimeSpan.Zero, RdpRetryPolicy.GetDelay(3));
    }

    [Fact]
    public void User_disconnect_overrides_transport_details()
    {
        var failure = RdpFailureClassifier.Classify(
            "session", "OnDisconnected", disconnectReason: 7, userInitiated: true);
        Assert.Equal(RdpFailureCategory.UserInitiatedDisconnect, failure.Category);
        Assert.False(failure.IsRetryable);
    }

    [Fact]
    public void Numeric_codes_are_not_guessed()
    {
        var failure = RdpFailureClassifier.Classify(
            "session", "UnknownSource", disconnectReason: 1234, logonErrorCode: 9876);
        Assert.Equal(RdpFailureCategory.Unknown, failure.Category);
        Assert.False(failure.IsRetryable);
    }

    [Fact]
    public void Exception_metadata_is_structured_without_raw_exception_message_in_safe_message()
    {
        var failure = RdpFailureClassifier.Classify(
            "session", "ConnectionTimeout", exception: new TimeoutException("password=secret-token"));
        Assert.Equal(nameof(TimeoutException), failure.ExceptionType);
        Assert.NotNull(failure.HResult);
        Assert.DoesNotContain("secret-token", failure.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("password", failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }
}