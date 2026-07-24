using System;
using Netvium.Models;

namespace Netvium.Rdp
{
    public interface IRdpEngine : IDisposable
    {
        event Action<RdpState, string?> StateChanged;
        
        RdpState State { get; }
        
        bool Initialize(object client);
        void Configure(string server, string user, string pass, ConnectionProfile profile, int width, int height);
        void Connect();
        void Disconnect();
        void UpdateResolution(int width, int height);
        void SendCtrlAltDel();
        string GetDiagnostics();
        void Log(string msg);
        
        int LastError { get; }
        int DisconnectReason { get; }
        int ExtendedDisconnectReason { get; }
    }
}
