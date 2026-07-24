using System.Collections.Generic;
using Netvium.Helpers;

namespace Netvium.Models
{
    public enum HealthBadge { Healthy, Warning, Critical, Offline }

    /// <summary>Readability-focused RDP display mode (per-connection, persisted).</summary>
    public enum RdpReadabilityMode
    {
        Native,
        Comfort,
        Large,
        ExtraLarge,
        Fit
    }

    public class ServerInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public ServerInfo(string name, string ip, string username, string password, string folder, int port = 3389, int scale = 100)
        {
            _name = name;
            _ip = ip;
            _username = username;
            _password = password;
            _folder = folder;
            _port = port;
            _scale = scale > 0 ? scale : 100;
            _lastStatus = HealthBadge.Offline;
        }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private string _ip;
        public string Ip { get => _ip; set { _ip = value; OnPropertyChanged(); } }

        private string _username;
        public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }

        private string _password;
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }

        private string _folder;
        public string Folder { get => _folder; set { _folder = value; OnPropertyChanged(); } }

        private int _port;
        public int Port { get => _port; set { _port = value; OnPropertyChanged(); } }

        private int _scale = 100;
        public int Scale { get => _scale; set { _scale = value; OnPropertyChanged(); } }

        private RdpFillMode _fillMode = RdpFillMode.Fit;
        public RdpFillMode FillMode { get => _fillMode; set { _fillMode = value; OnPropertyChanged(); } }

        private RdpReadabilityMode _readabilityMode = RdpReadabilityMode.Comfort;
        public RdpReadabilityMode ReadabilityMode
        {
            get => _readabilityMode;
            set { _readabilityMode = value; OnPropertyChanged(); }
        }

        private Rdp.RdpResolutionProfile _resolutionProfile = Rdp.RdpResolutionProfile.Balanced;
        public Rdp.RdpResolutionProfile ResolutionProfile
        {
            get => _resolutionProfile;
            set { _resolutionProfile = value; OnPropertyChanged(); }
        }

        private HealthBadge _lastStatus;
        public HealthBadge LastStatus 
        { 
            get => _lastStatus; 
            set 
            { 
                _lastStatus = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(BadgeBrush)); 
            } 
        }

        // Grid Monitor Props
        private double _lastCpu;
        public double LastCpu { get => _lastCpu; set { _lastCpu = value; OnPropertyChanged(); } }

        private double _lastRam;
        public double LastRam { get => _lastRam; set { _lastRam = value; OnPropertyChanged(); } }

        private double _lastDisk;
        public double LastDisk { get => _lastDisk; set { _lastDisk = value; OnPropertyChanged(); } }

        private string _rdpSessions = "0";
        public string RdpSessions { get => _rdpSessions; set { _rdpSessions = value; OnPropertyChanged(); } }

        private string _latency = "N/A";
        public string Latency { get => _latency; set { _latency = value; OnPropertyChanged(); } }

        private string _alertMessage = "";
        public string AlertMessage { get => _alertMessage; set { _alertMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlert)); } }
        public bool HasAlert => !string.IsNullOrEmpty(AlertMessage);

        private string _aiInsight = "Analiz ediliyor...";
        public string AiInsight { get => _aiInsight; set { _aiInsight = value; OnPropertyChanged(); } }

        private string _lastEvent = "Olay yok";
        public string LastEvent { get => _lastEvent; set { _lastEvent = value; OnPropertyChanged(); } }

        /// <summary>Quick-connect session not persisted to connections.json.</summary>
        public bool IsEphemeral { get; set; }

        /// <summary>True when dpapi: credential on disk could not be decrypted for this entry.</summary>
        public bool StoredPasswordUnavailable { get; set; }

        public System.Windows.Media.Brush BadgeBrush => GetBrushForStatus(LastStatus);
        public System.Windows.Media.Brush StatusBrush => GetBrushForStatus(LastStatus, 0.4); // Softer for borders/highlights

        private System.Windows.Media.Brush GetBrushForStatus(HealthBadge status, double opacity = 1.0)
        {
            System.Windows.Media.Color color = status switch
            {
                HealthBadge.Healthy => System.Windows.Media.Color.FromRgb(16, 185, 129),
                HealthBadge.Warning => System.Windows.Media.Color.FromRgb(245, 158, 11),
                HealthBadge.Critical => System.Windows.Media.Color.FromRgb(239, 68, 68),
                _ => System.Windows.Media.Color.FromRgb(71, 85, 105)
            };

            if (opacity < 1.0) color.A = (byte)(255 * opacity);
            return new System.Windows.Media.SolidColorBrush(color);
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }

    public class PersistedServer
    {
        public string Name { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Folder { get; set; }
        public int Port { get; set; } = 3389;
        public int Scale { get; set; } = 100;
        public RdpReadabilityMode ReadabilityMode { get; set; } = RdpReadabilityMode.Comfort;
        public HealthBadge LastStatus { get; set; } = HealthBadge.Offline;
        public List<WmiSnapshot>? Snapshots { get; set; }
    }

    public class PersistedData
    {
        public List<PersistedServer> Servers { get; set; } = new();
        public List<string> Folders { get; set; } = new();
    }

    public enum RdpFillMode
    {
        Fit,
        FillCrop,
        Stretch
    }

    public class ConnectionProfile
    {
        public ConnectionProfile(string name, bool useMinimal, bool enableCredSsp, int authenticationLevel, bool negotiateSecurityLayer, bool promptForCredentials, int width = 1280, int height = 1024, uint scaleFactor = 100, bool enableClipboard = false, bool smartSizing = true)
        {
            Name = name;
            UseMinimal = useMinimal;
            EnableCredSsp = enableCredSsp;
            AuthenticationLevel = authenticationLevel;
            NegotiateSecurityLayer = negotiateSecurityLayer;
            PromptForCredentials = promptForCredentials;
            Width = width;
            Height = height;
            ScaleFactor = scaleFactor;
            EnableClipboard = enableClipboard;
            SmartSizing = smartSizing;
        }

        public string Name { get; }
        public bool UseMinimal { get; }
        public bool EnableCredSsp { get; }
        public int AuthenticationLevel { get; }
        public bool NegotiateSecurityLayer { get; }
        public bool PromptForCredentials { get; }
        public int Width { get; }
        public int Height { get; }
        public uint ScaleFactor { get; }
        public bool EnableClipboard { get; set; }
        public bool SmartSizing { get; set; }
    }
}
