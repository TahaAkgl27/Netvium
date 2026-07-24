
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Netvium.Rdp.Clean
{
    public enum RdpFailureCategory
    {
        NetworkUnavailable,
        ConnectionTimeout,
        TransportDisconnected,
        HostUnavailable,
        AuthenticationFailed,
        AccountRestricted,
        CertificateOrSecurityFailure,
        PolicyDenied,
        ClientFatalError,
        UserInitiatedDisconnect,
        SessionClosed,
        Unknown,
    }

    public sealed record RdpFailureInfo
    {
        public RdpFailureCategory Category { get; init; } = RdpFailureCategory.Unknown;
        public string Source { get; init; } = "Unknown";
        public int? DisconnectReason { get; init; }
        public int? ExtendedDisconnectReason { get; init; }
        public int? FatalErrorCode { get; init; }
        public int? LogonErrorCode { get; init; }
        public string? ExceptionType { get; init; }
        public int? HResult { get; init; }
        public Exception? Exception { get; init; }
        public string SafeMessage { get; init; } = RdpFailureMessages.For(RdpFailureCategory.Unknown);
        public bool IsRetryable { get; init; }
        public string SessionId { get; init; } = "n/a";
        public int RetryAttempt { get; init; }
        public int MaxRetryAttempts { get; init; } = RdpRetryPolicy.MaxAutoRetries;
        public string? DetailHint { get; init; }

        public string Signature =>
            string.Join("|", Source, Category, DisconnectReason, ExtendedDisconnectReason,
                FatalErrorCode, LogonErrorCode, HResult, ExceptionType);
    }

    public static class RdpFailureMessages
    {
        public static string For(RdpFailureCategory category) => category switch
        {
            RdpFailureCategory.AuthenticationFailed or RdpFailureCategory.AccountRestricted =>
                "Oturum açılamadı. Kullanıcı adı veya parolayı kontrol edin.",
            RdpFailureCategory.CertificateOrSecurityFailure or RdpFailureCategory.PolicyDenied =>
                "Bağlantı güvenlik veya sunucu politikası nedeniyle reddedildi.",
            RdpFailureCategory.NetworkUnavailable or RdpFailureCategory.HostUnavailable =>
                "Sunucuya ulaşılamadı. Otomatik yeniden deneme tamamlandı.",
            RdpFailureCategory.UserInitiatedDisconnect =>
                "Bağlantı kullanıcı tarafından kesildi.",
            RdpFailureCategory.SessionClosed =>
                "RDP oturumu kapatıldı.",
            RdpFailureCategory.ClientFatalError =>
                "RDP istemcisi bağlantıyı sonlandırdı.",
            RdpFailureCategory.ConnectionTimeout or RdpFailureCategory.TransportDisconnected =>
                "RDP bağlantısı beklenmedik şekilde kesildi.",
            _ => "RDP bağlantısı kurulamadı.",
        };
    }
    public static class RdpFailureClassifier
    {
        public static RdpFailureInfo Classify(
            string sessionId,
            string source,
            int? disconnectReason = null,
            int? extendedDisconnectReason = null,
            int? fatalErrorCode = null,
            int? logonErrorCode = null,
            Exception? exception = null,
            bool userInitiated = false,
            string? detailHint = null,
            RdpFailureCategory? categoryHint = null,
            int retryAttempt = 0,
            int maxRetryAttempts = RdpRetryPolicy.MaxAutoRetries)
        {
            var category = categoryHint ?? InferCategory(
                source,
                disconnectReason,
                fatalErrorCode,
                logonErrorCode,
                exception,
                userInitiated,
                detailHint);

            return new RdpFailureInfo
            {
                Category = category,
                Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source,
                DisconnectReason = disconnectReason,
                ExtendedDisconnectReason = extendedDisconnectReason,
                FatalErrorCode = fatalErrorCode,
                LogonErrorCode = logonErrorCode,
                ExceptionType = exception?.GetType().Name,
                HResult = exception?.HResult,
                Exception = exception,
                SafeMessage = RdpFailureMessages.For(category),
                IsRetryable = RdpRetryPolicy.IsRetryable(category),
                SessionId = string.IsNullOrWhiteSpace(sessionId) ? "n/a" : sessionId,
                RetryAttempt = Math.Max(0, retryAttempt),
                MaxRetryAttempts = Math.Clamp(maxRetryAttempts, 0, RdpRetryPolicy.MaxAutoRetries),
                DetailHint = detailHint,
            };
        }

        private static RdpFailureCategory InferCategory(
            string source,
            int? disconnectReason,
            int? fatalErrorCode,
            int? logonErrorCode,
            Exception? exception,
            bool userInitiated,
            string? detailHint)
        {
            if (userInitiated)
                return RdpFailureCategory.UserInitiatedDisconnect;

            if (ContainsAny(detailHint, "account locked", "account disabled", "account restricted", "expired"))
                return RdpFailureCategory.AccountRestricted;

            if (ContainsAny(detailHint, "certificate", "security", "credssp", "nla", "tls"))
                return RdpFailureCategory.CertificateOrSecurityFailure;

            if (ContainsAny(detailHint, "policy", "denied", "permission", "not allowed"))
                return RdpFailureCategory.PolicyDenied;

            if (ContainsAny(detailHint, "username", "password", "logon", "authentication"))
                return RdpFailureCategory.AuthenticationFailed;

            if (exception is TimeoutException || ContainsAny(source, "timeout"))
                return RdpFailureCategory.ConnectionTimeout;

            if (exception is SocketException socket)
            {
                return socket.SocketErrorCode is SocketError.HostNotFound
                    or SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable
                    ? RdpFailureCategory.HostUnavailable
                    : RdpFailureCategory.NetworkUnavailable;
            }

            if (ContainsAny(source, "TcpProbe", "Network"))
                return RdpFailureCategory.NetworkUnavailable;

            if (ContainsAny(source, "HostUnavailable", "HostNotFound"))
                return RdpFailureCategory.HostUnavailable;

            if (ContainsAny(source, "Logon", "Authentication"))
                return RdpFailureCategory.AuthenticationFailed;

            if (ContainsAny(source, "Disconnected", "Polling", "SyncState"))
                return RdpFailureCategory.TransportDisconnected;

            if (ContainsAny(source, "Fatal") || fatalErrorCode.HasValue)
                return RdpFailureCategory.ClientFatalError;

            if (ContainsAny(source, "SessionClosed"))
                return RdpFailureCategory.SessionClosed;

            // Numeric RDP/COM codes are intentionally not guessed here.
            _ = disconnectReason;
            _ = logonErrorCode;
            return RdpFailureCategory.Unknown;
        }

        private static bool ContainsAny(string? value, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class RdpRetryPolicy
    {
        public const int MaxAutoRetries = 2;

        public static IReadOnlyList<TimeSpan> Delays { get; } =
            new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };

        public static bool IsRetryable(RdpFailureCategory category) =>
            category is RdpFailureCategory.NetworkUnavailable
                or RdpFailureCategory.ConnectionTimeout
                or RdpFailureCategory.TransportDisconnected
                or RdpFailureCategory.HostUnavailable;

        public static bool ShouldRetry(RdpFailureInfo failure, int retryAttempt) =>
            failure.IsRetryable
            && retryAttempt >= 1
            && retryAttempt <= MaxAutoRetries;

        public static TimeSpan GetDelay(int retryAttempt) =>
            retryAttempt is >= 1 and <= MaxAutoRetries
                ? Delays[retryAttempt - 1]
                : TimeSpan.Zero;
    }

    internal static class RdpFailureLogFormatter
    {
        public static string Format(
            RdpFailureInfo failure,
            string phase,
            TimeSpan? delay = null,
            bool duplicateSuppressed = false)
        {
            var delayText = delay.HasValue ? delay.Value.TotalSeconds.ToString("0.###") : "0";
            return $"FailurePhase={phase} Category={failure.Category} Source={failure.Source} "
                + $"DisconnectReason={failure.DisconnectReason?.ToString() ?? "n/a"} "
                + $"ExtendedDisconnectReason={failure.ExtendedDisconnectReason?.ToString() ?? "n/a"} "
                + $"FatalErrorCode={failure.FatalErrorCode?.ToString() ?? "n/a"} "
                + $"LogonErrorCode={failure.LogonErrorCode?.ToString() ?? "n/a"} "
                + $"ExceptionType={failure.ExceptionType ?? "n/a"} "
                + $"HRESULT={failure.HResult?.ToString("X8") ?? "n/a"} "
                + $"Retryable={failure.IsRetryable} RetryAttempt={failure.RetryAttempt}/{failure.MaxRetryAttempts} "
                + $"DelaySeconds={delayText} DuplicateSuppressed={duplicateSuppressed}";
        }
    }
}
