using Netvium.Rdp.Clean;
using System.Text;

namespace Netvium.Rdp
{
    public sealed class RdpConnectionAttemptDiagnostics
    {
        public string? ServerName { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 3389;
        public bool HasUsername { get; set; }
        public DateTime ConnectStartedUtc { get; set; }
        public string LastEvent { get; set; } = "none";
        public RdpConnectionState FinalState { get; set; } = RdpConnectionState.Idle;
        public int? ErrorCode { get; set; }
        public string? FailureReason { get; set; }
        public string? ProfileLabel { get; set; }
        public int? ClientState { get; set; }
        public int ElapsedMs { get; set; }
        public bool? TcpReachable { get; set; }
        public string? ActiveXVersion { get; set; }
        public string? ApartmentState { get; set; }
        public int ConnectPhaseTimeoutSeconds { get; set; } = 120;
        public int? ComHResult { get; set; }
        public string? LastStatusText { get; set; }
        public string LiveTimeline { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public RdpFailureCategory FailureCategory { get; set; } = RdpFailureCategory.Unknown;
        public int? DisconnectReason { get; set; }
        public int? ExtendedDisconnectReason { get; set; }
        public int? FatalErrorCode { get; set; }
        public int? LogonErrorCode { get; set; }
        public bool Retryable { get; set; }
        public int RetryAttempt { get; set; }
        public int MaxRetryAttempts { get; set; } = RdpRetryPolicy.MaxAutoRetries;

        public RdpConnectionAttemptDiagnostics Clone() => new()
        {
            ServerName = ServerName,
            Host = Host,
            Port = Port,
            HasUsername = HasUsername,
            ConnectStartedUtc = ConnectStartedUtc,
            LastEvent = LastEvent,
            FinalState = FinalState,
            ErrorCode = ErrorCode,
            FailureReason = FailureReason,
            ProfileLabel = ProfileLabel,
            ClientState = ClientState,
            ElapsedMs = ElapsedMs,
            TcpReachable = TcpReachable,
            ActiveXVersion = ActiveXVersion,
            ApartmentState = ApartmentState,
            ConnectPhaseTimeoutSeconds = ConnectPhaseTimeoutSeconds,
            ComHResult = ComHResult,
            LastStatusText = LastStatusText,
            LiveTimeline = LiveTimeline,
            SessionId = SessionId,
            FailureCategory = FailureCategory,
            DisconnectReason = DisconnectReason,
            ExtendedDisconnectReason = ExtendedDisconnectReason,
            FatalErrorCode = FatalErrorCode,
            LogonErrorCode = LogonErrorCode,
            Retryable = Retryable,
            RetryAttempt = RetryAttempt,
            MaxRetryAttempts = MaxRetryAttempts,
        };

        public string ToReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Netvium RDP Connection Diagnostics");
            sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Server: {ServerName ?? "—"}");
            sb.AppendLine($"Host: {Host ?? "—"}");
            sb.AppendLine($"Port: {Port}");
            sb.AppendLine($"TCP {Port} reachable: {(TcpReachable.HasValue ? TcpReachable.Value ? "yes" : "no" : "—")}");
            sb.AppendLine($"ActiveX version: {ActiveXVersion ?? "—"}");
            sb.AppendLine($"Apartment state: {ApartmentState ?? "—"}");
            sb.AppendLine($"Connect phase timeout (s): {ConnectPhaseTimeoutSeconds}");
            sb.AppendLine($"Username provided: {(HasUsername ? "yes" : "no")}");
            sb.AppendLine($"Connect started (UTC): {ConnectStartedUtc:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Elapsed ms: {ElapsedMs}");
            sb.AppendLine($"Profile: {ProfileLabel ?? "—"}");
            sb.AppendLine($"Last RDP event: {LastEvent}");
            sb.AppendLine($"ClientState: {(ClientState.HasValue ? ClientState.Value.ToString() : "—")}");
            if (ComHResult.HasValue)
                sb.AppendLine($"COM HRESULT: 0x{ComHResult.Value:X8} ({ComHResult.Value})");
            if (!string.IsNullOrWhiteSpace(LastStatusText))
                sb.AppendLine($"Status text: {LastStatusText}");
            if (!string.IsNullOrWhiteSpace(LiveTimeline))
            {
                sb.AppendLine("Live timeline:");
                sb.AppendLine(LiveTimeline);
            }
            sb.AppendLine($"Session ID: {SessionId ?? "n/a"}");
            sb.AppendLine($"Failure category: {FailureCategory}");
            sb.AppendLine($"Retryable: {Retryable} ({RetryAttempt}/{MaxRetryAttempts})");
            sb.AppendLine($"Disconnect reason: {DisconnectReason?.ToString() ?? "n/a"}");
            sb.AppendLine($"Extended disconnect reason: {ExtendedDisconnectReason?.ToString() ?? "n/a"}");
            sb.AppendLine($"Final state: {FinalState}");
            if (ErrorCode.HasValue)
                sb.AppendLine($"Error code: {ErrorCode.Value}");
            if (!string.IsNullOrWhiteSpace(FailureReason))
                sb.AppendLine($"Reason: {FailureReason}");
            return sb.ToString().TrimEnd();
        }
    }

    public sealed class RdpConnectionFailureInfo
    {
        public RdpConnectionState State { get; init; }
        public string Message { get; init; } = string.Empty;
        public RdpConnectionAttemptDiagnostics Diagnostics { get; init; } = new();
        public Netvium.Rdp.Clean.RdpFailureInfo? Failure { get; init; }
    }
}
