using System.Runtime.InteropServices;
using Netvium.Native;

namespace Netvium.Rdp
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class RdpOcxEventSink : StandardOleMarshalObject, NativeMethods.IMsTscAxEvents
    {
        private readonly EmbeddedRdpClient _client;

        public RdpOcxEventSink(EmbeddedRdpClient client) => _client = client;

        public void OnConnecting()
        {
            _client.RecordTimelineOcxCom("OnConnecting");
            _client.RaiseOcxConnecting();
        }

        public void OnConnected()
        {
            _client.RecordTimelineOcxCom("OnConnected");
            _client.RaiseOcxConnected();
        }

        public void OnLoginComplete()
        {
            _client.RecordTimelineOcxCom("OnLoginComplete");
            _client.RaiseOcxLoginComplete();
        }

        public void OnDisconnected(int discReason)
        {
            _client.RecordTimelineOcxCom("OnDisconnected", $"reason={discReason}");
            _client.RaiseOcxDisconnected(discReason);
        }
        public void OnReceivedTSPublicKey(string publicKey) { }
        public void OnAuthenticationWarningDisplayed()
        {
            _client.RecordTimelineOcxCom("OnAuthenticationWarningDisplayed");
            _client.RaiseOcxAuthenticationWarningDisplayed();
        }

        public void OnAuthenticationWarningDismissed() => _client.RaiseOcxAuthenticationWarningDismissed();
        public void OnAuthenticationWarning(int code)
        {
            _client.RecordTimelineOcxCom("OnAuthenticationWarning", $"code={code}");
            _client.RaiseOcxAuthenticationWarning(code);
        }
        public void OnRemoteDesktopSizeChange(int width, int height)
        {
            _client.RecordTimelineOcxCom("OnRemoteDesktopSizeChange", $"{width}x{height}");
            _client.RaiseOcxRemoteDesktopSizeChange(width, height);
        }

        public void OnWarning(int warningCode)
        {
            _client.RecordTimelineOcxCom("OnWarning", $"code={warningCode}");
            _client.RaiseOcxWarning(warningCode);
        }

        public void OnFatalError(int errorCode)
        {
            _client.RecordTimelineOcxCom("OnFatalError", $"code={errorCode}");
            _client.RaiseOcxFatalError(errorCode);
        }

        public void OnLogonError(int errorCode)
        {
            _client.RecordTimelineOcxCom("OnLogonError", $"code={errorCode}");
            _client.RaiseOcxLogonError(errorCode);
        }
    }
}
