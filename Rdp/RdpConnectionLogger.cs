using Netvium.Helpers;

namespace Netvium.Rdp
{
    internal static class RdpConnectionLogger
    {
        public static void FirstConnectStarted(string? host) =>
            Log("FirstConnectStarted", $"host={host}");

        public static void ConnectStarted(string? host, int port) =>
            Log("RdpConnectStarted", $"host={host} port={port}");

        public static void TcpOk(string? host, int port) =>
            Log("TCP_OK", $"host={host} port={port}");

        public static void AttemptStarted(RdpConnectionAttemptDiagnostics d) =>
            Log(
                "RdpAttemptStarted",
                $"server={d.ServerName} host={d.Host} port={d.Port} user={(d.HasUsername ? "yes" : "no")} "
                + $"startedUtc={d.ConnectStartedUtc:O}");

        public static void AttemptEvent(string? host, string eventName, int? code = null) =>
            Log("RdpAttemptEvent", $"host={host} event={eventName} code={(code.HasValue ? code.Value.ToString() : "—")}");

        public static void AttemptFinalized(RdpConnectionAttemptDiagnostics d) =>
            Log(
                "RdpAttemptFinalized",
                $"host={d.Host} port={d.Port} user={(d.HasUsername ? "yes" : "no")} "
                + $"startedUtc={d.ConnectStartedUtc:O} elapsedMs={d.ElapsedMs} lastEvent={d.LastEvent} "
                + $"finalState={d.FinalState} errorCode={(d.ErrorCode.HasValue ? d.ErrorCode.Value.ToString() : "—")} "
                + $"reason={d.FailureReason ?? "—"}");

        public static void ControlCreated(string clsid) =>
            Log("RdpControlCreated", $"clsid={clsid}");

        public static void CredentialsApplied(string? user) =>
            Log("RdpCredentialsApplied", $"user={RedactUser(user)}");

        public static void ConnectCalled(string? host, int port) =>
            Log("RdpConnectCalled", $"host={host} port={port}");

        public static void Connecting(string? host) =>
            Log("RdpConnecting", $"host={host}");

        public static void Connected(string? host) =>
            Log("RdpConnected", $"host={host}");

        public static void RdpConnectionStarted(string? host) =>
            Log("RdpConnectionStarted", $"host={host}");

        public static void AiGuidePausedForConnection(string? host) =>
            Log("AiGuidePausedForConnection", $"host={host}");

        public static void ExistingAiGuideTaskCancelled(string? host) =>
            Log("ExistingAiGuideTaskCancelled", $"host={host}");

        public static void RdpConnectedEvent(string? host) =>
            Log("RdpConnectedEvent", $"host={host}");

        public static void FirstNonBlankFrameDetected(string? host) =>
            Log("FirstNonBlankFrameDetected", $"host={host}");

        public static void RdpReadyWaitStarted(string? host) =>
            Log("RdpReadyWaitStarted", $"host={host}");

        public static void FirstFrameDetected(string? host) =>
            Log("FirstFrameDetected", $"host={host}");

        public static void RdpReadyTimeout(string? host) =>
            Log("RdpReadyTimeout", $"host={host} allowManualQueryWithWarning=true");

        public static void StableReadyReached(string? host) =>
            Log("StableReadyReached", $"host={host}");

        public static void AiGuideResumed(string? host) =>
            Log("AiGuideResumed", $"host={host}");

        public static void LoginComplete(string? host) =>
            Log("RdpLoginComplete", $"host={host}");

        public static void FirstFrameReady(string? host) =>
            Log("FirstFrameReady", $"host={host}");

        public static void AiGuideEnabled(string? host) =>
            Log("AiGuideEnabled", $"host={host}");

        public static void UiFreezeDetected(string checkpoint, int durationMs) =>
            Log("UiFreezeDetected", $"checkpoint={checkpoint} durationMs={durationMs}");

        public static void ConnectionTimeout(string? host, int seconds) =>
            Log("RdpConnectionTimeout", $"host={host} timeoutSeconds={seconds}");

        public static void Disconnected(string? host, int reason, int ext) =>
            Log("RdpDisconnected", $"host={host} reason={reason} ext={ext}");

        public static void LogonError(string? host, int code) =>
            Log("RdpLogonError", $"host={host} code={code}");

        public static void FatalError(string? host, int code) =>
            Log("RdpFatalError", $"host={host} code={code}");

        public static void Warning(string? host, int code) =>
            Log("RdpWarning", $"host={host} code={code}");

        public static void LifecycleEvent(string? host, string eventName, string details) =>
            Log("RdpLifecycleEvent", $"host={host} event={eventName} {details}");

        public static void Recovered(string? host, string detail) =>
            Log("RdpRecovered", $"host={host} detail={detail}");

        private static void Log(string action, string message) =>
            AppLogger.Debug(message, category: "RDP", action: action);

        private static string RedactUser(string? user)
        {
            if (string.IsNullOrWhiteSpace(user))
                return string.Empty;
            if (user.Contains('@'))
                return user[..Math.Min(user.IndexOf('@'), user.Length)] + "@***";
            if (user.Contains('\\'))
                return user.Split('\\')[0] + "\\***";
            return user.Length <= 2 ? "***" : user[..2] + "***";
        }
    }
}
