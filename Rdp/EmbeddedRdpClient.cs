using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using Netvium.Models;
using Netvium.Native;
using Netvium.Helpers;

namespace Netvium.Rdp
{
    /// <summary>
    /// A unified, robust RDP client control that embeds Microsoft Remote Desktop ActiveX.
    /// Replaces the fragmented and broken legacy RDP implementation.
    /// </summary>
    public class EmbeddedRdpClient : AxHost
    {
        // MS RDP Client 11 (primary on Win10/11)
        public const string RdpClsid = "{A0C63C30-F08D-4AB4-907C-34905D770C7D}";
        // MS RDP Client 10 fallback
        public const string RdpClsidFallback = "{8B918B82-7985-4C24-89DF-C33AD2BBFBCD}";

        private dynamic? _ocx;
        private bool _isInitialized;
        private bool _eventsHooked;
        private readonly List<(System.Reflection.EventInfo Event, Delegate Handler)> _hookedEvents = new();
        private RdpOcxEventSink? _eventSink;
        private IConnectionPoint? _connectionPoint;
        private int _connectionPointCookie;
        private System.Windows.Forms.Timer? _displaySyncTimer;
        private int _displaySyncPass;
        private string? _lastServer;
        private DateTime _connectStartedUtc;
        private int _lastPort = 3389;

        internal RdpConnectionTimeline? ActiveTimeline { get; set; }

        public event Action? OnConnecting;
        public event Action? OnLoginComplete;
        public event Action? OnConnected;
        public event Action<int>? OnDisconnected;
        public event Action<int>? OnFatalError;
        public event Action<int>? OnLogonError;
        public event Action<int>? OnWarning;
        public event Action<int>? OnAuthenticationWarningDisplayed;
        public event Action<int>? OnAuthenticationWarningDismissed;

        public string ActiveClsid { get; }

        public new RdpState State { get; private set; } = RdpState.Idle;
        public bool IsConnected => State is RdpState.Connected or RdpState.LoginComplete;

        public EmbeddedRdpClient() : this(RdpClsid)
        {
        }

        public EmbeddedRdpClient(string clsid) : base(clsid)
        {
            ActiveClsid = clsid;
            this.BackColor = Color.Black;
            this.Dock = DockStyle.Fill;
            this.Margin = new Padding(0);
            this.Padding = new Padding(0);
            // Visible=true olmalı yoksa OCX bazen initialize olmuyor veya handshake başlamıyor.
            this.Visible = true; 
        }

        public object GetOcxObject()
        {
            if (_ocx == null) _ocx = GetOcx();
            return _ocx!;
        }

        public int? TryGetClientState()
        {
            try
            {
                if (_ocx == null) return null;
                return (int)_ocx.ClientState;
            }
            catch (COMException com)
            {
                RecordComError(com, "ClientState");
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to read RDP ClientState.", ex);
                return null;
            }
        }

        public int? TryGetOcxConnected()
        {
            try
            {
                if (_ocx == null) return null;
                return (int)_ocx.Connected;
            }
            catch (COMException com)
            {
                RecordComError(com, "Connected");
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to read RDP Connected property.", ex);
                return null;
            }
        }

        public string? TryGetStatusText(uint statusType = 1)
        {
            try
            {
                if (_ocx == null) return null;
                return (string)_ocx.GetStatusText(statusType);
            }
            catch (COMException com)
            {
                RecordComError(com, "GetStatusText");
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"GetStatusText failed: {ex.Message}", category: "RDP", action: "GetStatusText");
                return null;
            }
        }

        public int? LastComHResult { get; private set; }

        internal void RecordComError(COMException com, string context)
        {
            LastComHResult = com.ErrorCode;
            var detail = $"HRESULT=0x{com.ErrorCode:X8} ({com.ErrorCode}) context={context}";
            Tl($"COM error ({context})", detail, com);
            AppLogger.Error($"RDP COM error ({context}): 0x{com.ErrorCode:X8}", com, category: "RDP", action: "COM");
        }

        public int TryGetExtendedDisconnectReason()
        {
            try
            {
                if (_ocx == null) return 0;
                // BUG FIX: Dynamic dispatch for COM properties.
                return (int)_ocx.ExtendedDisconnectReason;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to read ExtendedDisconnectReason.", ex);
                return 0;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _ocx = GetOcx();
            Tl("ActiveX created", $"clsid={ActiveClsid} hwnd=0x{Handle:X}");
            InitializeRdp();
        }

        internal void RecordTimelineOcxCom(string eventName, string? detail = null) =>
            ActiveTimeline?.RecordOcxComCallback(eventName, detail);

        internal void RecordTimelineHandler(string eventName, string? detail = null) =>
            ActiveTimeline?.RecordOcxEvent($"{eventName} (handler)", detail);

        private void Tl(string milestone, string? detail = null, Exception? ex = null) =>
            ActiveTimeline?.Record(milestone, detail, ex);

        /// <summary>
        /// Fresh control setup before attaching to WindowsFormsHost.
        /// </summary>
        public void PrepareFreshControl()
        {
            Dock = DockStyle.Fill;
            Visible = true;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            BackColor = Color.Black;
        }

        /// <summary>HWND and parent must exist before Connect() — STA UI thread only.</summary>
        public bool EnsureReadyForConnect()
        {
            if (IsDisposed)
                return false;

            if (!IsHandleCreated)
                CreateControl();

            PerformLayout();

            if (!IsHandleCreated || Handle == nint.Zero)
            {
                Tl("EnsureReadyForConnect", "HWND not created");
                return false;
            }

            if (Parent == null)
            {
                Tl("EnsureReadyForConnect", "Parent not assigned");
                return false;
            }

            Visible = true;
            Tl("HWND_READY", $"activeXHwnd=0x{Handle:X} parent={(Parent?.GetType().Name ?? "null")}");
            return true;
        }

        public void ApplyDesktopSizeAfterLoginPublic() => ApplyDesktopSizeAfterLogin();

        /// <summary>mstsc-compatible connect — settings match external Remote Desktop client.</summary>
        public void ConnectMstscCompatible(
            string host,
            int port,
            string normalizedUser,
            string domain,
            string userForOcx,
            string password,
            int width,
            int height)
        {
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Host size invalid ({width}x{height}). Connect aborted.");

            if (!EnsureReadyForConnect())
                throw new InvalidOperationException("RDP control is not ready (handle/parent).");

            var (actualW, actualH) = GetActualClientSize();
            if (actualW <= 0 || actualH <= 0)
                throw new InvalidOperationException(
                    $"ActiveX layout is not ready (ClientSize={actualW}x{actualH}). Connect aborted.");

            if (_ocx == null)
            {
                _ocx = GetOcx();
                if (_ocx == null)
                    throw new InvalidOperationException("RDP OCX could not be created.");
            }

            EnsureOcxEventsHooked();
            ArgumentNullException.ThrowIfNull(_ocx);

            dynamic ocx = _ocx;
            _lastServer = host;
            _lastPort = port <= 0 ? 3389 : port;
            _lastUser = normalizedUser;
            _lastPass = password;
            _lastDisconnectReason = 0;
            _lastFatalErrorCode = 0;
            _lastLogonErrorCode = 0;
            RecordEvent("ConnectInit");

            var (cw, ch) = GetHostPixelSize(width, height);
            SetExplicitSize(cw, ch);
            _lastRemoteWidth = cw;
            _lastRemoteHeight = ch;

            ApplyMstscCompatibleConfiguration(host, port, normalizedUser, domain, userForOcx, password, cw, ch);

            LogSessionSnapshot("PreConnect", cw, ch);
            RdpConnectionLogger.ConnectCalled(host, _lastPort);
            Tl("CONNECT_CALLED", $"host={host} port={_lastPort} hwnd=0x{Handle:X}");
            Log("Executing OCX.Connect()...");
            try
            {
                ocx.Connect();
            }
            catch (COMException com)
            {
                RecordComError(com, "Connect");
                throw;
            }

            Tl("CONNECT_RETURNED", "Connect() returned normally");
            Log("OCX.Connect() returned.");
            EnsureOcxEventsHookedAfterConnect();
            ActiveTimeline?.RecordControlVerification(this);
            RecordEvent("ConnectCalled");

            State = RdpState.Connecting;
            _connectStartedUtc = DateTime.UtcNow;
            try { OnConnecting?.Invoke(); }
            catch (Exception ex) { AppLogger.Error("RDP OnConnecting invoke failed.", ex); Tl("OnConnecting delegate invoke failed", null, ex); }
        }

        private void ApplyMstscCompatibleConfiguration(
            string host,
            int port,
            string normalizedUser,
            string domain,
            string userForOcx,
            string password,
            int width,
            int height)
        {
            dynamic ocx = _ocx!;
            ocx.Server = host;
            Tl("Server assigned", host);

            string assignUser;
            string assignDomain;
            if (normalizedUser.Contains('@', StringComparison.Ordinal))
            {
                assignUser = normalizedUser.Trim();
                assignDomain = string.Empty;
            }
            else if (normalizedUser.Contains('\\', StringComparison.Ordinal))
            {
                assignUser = userForOcx.Trim();
                assignDomain = domain.Trim();
            }
            else
            {
                assignUser = string.IsNullOrWhiteSpace(userForOcx) ? normalizedUser.Trim() : userForOcx.Trim();
                assignDomain = domain.Trim();
            }

            ocx.UserName = assignUser;
            Tl("Username assigned", RedactUser(assignUser));
            try
            {
                ocx.Domain = assignDomain;
                Tl("Domain assigned", string.IsNullOrEmpty(assignDomain) ? "empty" : assignDomain);
            }
            catch (Exception ex) { Tl("Domain assignment failed", null, ex); }

            ocx.DesktopWidth = width;
            Tl("DesktopWidth", width.ToString());
            ocx.DesktopHeight = height;
            Tl("DesktopHeight", height.ToString());
            try { ocx.ColorDepth = 32; } catch { try { ocx.ColorDepth = 24; } catch { /* optional */ } }

            var adv = GetAdvancedSettings();
            if (adv != null)
            {
                try { adv.RDPPort = _lastPort; } catch (Exception ex) { Tl("RDPPort failed", null, ex); }
                try { adv.EnableCredSspSupport = 1; } catch (Exception ex) { Tl("EnableCredSspSupport failed", null, ex); }
                try { adv.AuthenticationLevel = 2; } catch (Exception ex) { Tl("AuthenticationLevel failed", null, ex); }
                try { adv.RedirectClipboard = 1; } catch (Exception ex) { Tl("RedirectClipboard failed", null, ex); }
                try { adv.SmartSizing = 1; } catch (Exception ex) { Tl("SmartSizing failed", null, ex); }
                try { adv.BitmapPersistence = 1; } catch (Exception ex) { Tl("BitmapPersistence failed", null, ex); }
                try { adv.Compress = 1; } catch (Exception ex) { Tl("Compress failed", null, ex); }
                try { adv.ClearTextPassword = password; Tl("Password assigned", "yes via AdvancedSettings (redacted)"); }
                catch (Exception ex) { Tl("AdvancedSettings password failed", null, ex); }
            }

            if (!string.IsNullOrEmpty(password))
                SetPassword(password);

            TryAssignUiParent();

            try
            {
                dynamic secured = ocx.SecuredSettings3 ?? ocx.SecuredSettings2 ?? ocx.SecuredSettings;
                if (secured != null)
                {
                    try { secured.StartProgram = string.Empty; } catch { /* optional */ }
                    try { secured.WorkDir = string.Empty; } catch { /* optional */ }
                }
            }
            catch (Exception ex) { Tl("SecuredSettings failed", null, ex); }

            Tl(
                "AdvancedSettings configured",
                $"port={_lastPort} credssp=true authLevel=2 clipboard=true smartSizing=true bitmapPersistence=1 compress=1 colorDepth=32");
        }

        /// <summary>
        /// Host pixel size after attach/layout — use for DesktopWidth/Height.
        /// </summary>
        public (int W, int H) GetHostPixelSize(int fallbackW, int fallbackH)
        {
            PerformLayout();
            var w = ClientSize.Width;
            var h = ClientSize.Height;
            if (w > 0 && h > 0)
                return (w, h);
            if (Parent != null)
            {
                w = Parent.ClientSize.Width;
                h = Parent.ClientSize.Height;
                if (w > 0 && h > 0)
                    return (w, h);
            }
            return (Math.Max(fallbackW, 800), Math.Max(fallbackH, 600));
        }

        /// <summary>ActiveX must have non-zero bounds before Connect() or handshake may never start.</summary>
        public void SetExplicitSize(int width, int height)
        {
            width = Math.Max(width, RdpHostLayoutSnapshot.MinHostWidth);
            height = Math.Max(height, RdpHostLayoutSnapshot.MinHostHeight);
            Dock = DockStyle.None;
            Bounds = new Rectangle(0, 0, width, height);
            MinimumSize = new Size(RdpHostLayoutSnapshot.MinHostWidth, RdpHostLayoutSnapshot.MinHostHeight);
            Visible = true;
            PerformLayout();
            if (Parent != null)
            {
                try { Parent.PerformLayout(); } catch { /* optional */ }
            }

            Tl(
                "Control sized",
                $"bounds={Bounds.Width}x{Bounds.Height} ClientSize={ClientSize.Width}x{ClientSize.Height} hwnd=0x{(IsHandleCreated ? Handle : nint.Zero):X}");
        }

        /// <summary>Returns actual control size only — never fabricates fallback dimensions.</summary>
        public (int W, int H) GetActualClientSize()
        {
            PerformLayout();
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
                return (ClientSize.Width, ClientSize.Height);
            if (Bounds.Width > 0 && Bounds.Height > 0)
                return (Bounds.Width, Bounds.Height);
            if (Parent != null && Parent.ClientSize.Width > 0 && Parent.ClientSize.Height > 0)
                return (Parent.ClientSize.Width, Parent.ClientSize.Height);
            return (0, 0);
        }

        public bool IsLayoutReadyForConnect()
        {
            if (!IsHandleCreated || Handle == nint.Zero || Parent == null)
                return false;
            var (w, h) = GetActualClientSize();
            return w > 0 && h > 0;
        }

        public void EnsureOcxEventsHooked()
        {
            if (_ocx == null && IsHandleCreated)
                _ocx = GetOcx();
            HookEvents();
        }

        /// <summary>
        /// Re-attempt event hooking after Connect() — some hosts only expose the connection point once active.
        /// </summary>
        internal void EnsureOcxEventsHookedAfterConnect()
        {
            if (_connectionPointCookie != 0 || _hookedEvents.Count > 0)
                return;

            _eventsHooked = false;
            HookEvents();
        }

        private void InitializeRdp()
        {
            if (_isInitialized || _ocx == null) return;
            try
            {
                dynamic ocx = _ocx!;
                var version = (object?)ocx.Version;
                Log($"Initialized MS RDP Client. Version: {version}");
                Tl("ActiveX initialized", $"version={version}");
                _isInitialized = true;
                RdpConnectionLogger.ControlCreated(ActiveClsid);
            }
            catch (Exception ex)
            {
                Log($"Initialization Error: {ex.Message}");
                Tl("ActiveX initialization error", null, ex);
            }
        }

        public void Connect(string server, int port, string user, string pass, ConnectionProfile profile, string? domain = null)
        {
            Tl("Connect() entered", $"server={server} port={port}");
            ActiveTimeline?.RecordThreadContext("Connect() entered");

            if (profile.Width <= 0 || profile.Height <= 0)
            {
                var ex = new InvalidOperationException($"Host size invalid ({profile.Width}x{profile.Height}). Connect aborted.");
                Tl("Connect() aborted", "invalid host size", ex);
                throw ex;
            }

            if (_ocx == null)
            {
                if (!IsHandleCreated) CreateControl();
                _ocx = GetOcx();
                if (_ocx == null)
                {
                    var ex = new InvalidOperationException("RDP OCX could not be created.");
                    Tl("Connect() aborted", "OCX null after CreateControl", ex);
                    throw ex;
                }

                Tl("ActiveX created", $"ocxType={_ocx.GetType().FullName}");
            }

            EnsureOcxEventsHooked();
            ArgumentNullException.ThrowIfNull(_ocx);

            try
            {
                dynamic ocx = _ocx;
                _lastServer = server;
                _lastPort = port <= 0 ? 3389 : port;
                _lastUser = user;
                _lastPass = pass;
                _lastProfile = profile;
                _lastDisconnectReason = 0;
                _lastFatalErrorCode = 0;
                _lastLogonErrorCode = 0;
                RecordEvent("ConnectInit");

                State = RdpState.Connecting;
                _connectStartedUtc = DateTime.UtcNow;
                try { OnConnecting?.Invoke(); } catch (Exception ex) { AppLogger.Error("RDP OnConnecting invoke failed.", ex); Tl("OnConnecting delegate invoke failed", null, ex); }

                string finalDomain = (domain ?? string.Empty).Trim();
                string finalUser = user.Trim();
                if (finalUser.Contains('@', StringComparison.Ordinal))
                {
                    finalDomain = string.Empty;
                }
                else if (string.IsNullOrEmpty(finalDomain) && finalUser.Contains('\\', StringComparison.Ordinal))
                {
                    var parts = finalUser.Split('\\', 2);
                    finalDomain = parts[0].Trim();
                    finalUser = parts.Length > 1 ? parts[1].Trim() : finalUser;
                }

                ocx.Server = server;
                Tl("Server assigned", server);
                ocx.UserName = finalUser;
                Tl("Username assigned", RedactUser(finalUser));
                try { ocx.Domain = finalDomain; Tl("Domain assigned", string.IsNullOrEmpty(finalDomain) ? "empty" : finalDomain); }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); Tl("Domain assignment failed", null, ex); }

                int w = profile.Width;
                int h = profile.Height;
                var (cw, ch) = GetHostPixelSize(w, h);
                w = cw;
                h = ch;
                _lastRemoteWidth = w;
                _lastRemoteHeight = h;
                ocx.DesktopWidth = w;
                Tl("DesktopWidth", w.ToString());
                ocx.DesktopHeight = h;
                Tl("DesktopHeight", h.ToString());
                ocx.ColorDepth = 24;

                Log(
                    $"Connect() | server={server}:{_lastPort} | profile={profile.Name} | credssp={profile.EnableCredSsp} | "
                    + $"authLevel={profile.AuthenticationLevel} | negotiateLayer={profile.NegotiateSecurityLayer} | "
                    + $"userFormat={DescribeUsernameFormat(finalUser, finalDomain)} | domain='{finalDomain}' | "
                    + $"desktop={w}x{h} | colorDepth=24");

                Log($"Final OCX Props -> Server: {server}, User: {RedactUser(finalUser)}, Domain: '{finalDomain}'");

                if (!string.IsNullOrEmpty(pass))
                    SetPassword(pass);
                else
                    Tl("Password assigned", "no (empty)");

                TryAssignUiParent();
                ApplyAuthenticationSettings(profile);

                LogSessionSnapshot("PreConnect", profile.Width, profile.Height);
                RdpConnectionLogger.ConnectCalled(server, _lastPort);
                Tl("Connect() called");
                Log("Executing OCX.Connect()...");
                ocx.Connect();
                Tl("Connect() returned normally");
                Log("OCX.Connect() returned.");
                EnsureOcxEventsHookedAfterConnect();
                ActiveTimeline?.RecordControlVerification(this);
                RecordEvent("ConnectCalled");
            }
            catch (Exception ex)
            {
                State = RdpState.Failed;
                Log($"Critical Connection Error: {ex.Message}");
                Tl("Connect() exception", null, ex);
                LogSessionSnapshot("ConnectFailed", profile.Width, profile.Height);
                OnDisconnected?.Invoke(-1);
            }
        }

        private void SetPassword(string pass)
        {
            if (_ocx == null || string.IsNullOrEmpty(pass)) return;
            dynamic ocx = _ocx!;

            try
            {
                var ns = (NativeMethods.IMsTscNonScriptable)ocx;
                ns.ClearTextPassword = pass;
                Log("Credential injection succeeded via IMsTscNonScriptable.");
                Tl("Password assigned", "yes via IMsTscNonScriptable (value redacted)");
                RdpConnectionLogger.CredentialsApplied(_lastUser);
                return;
            }
            catch (Exception ex) { AppLogger.Debug($"IMsTscNonScriptable password failed: {ex.Message}", category: "RDP"); Tl("Password via IMsTscNonScriptable failed", null, ex); }

            var adv = GetAdvancedSettings();
            if (adv != null)
            {
                try
                {
                    adv.ClearTextPassword = pass;
                    Log("Credential injection succeeded via AdvancedSettings.");
                    Tl("Password assigned", "yes via AdvancedSettings (value redacted)");
                    RdpConnectionLogger.CredentialsApplied(_lastUser);
                }
                catch (Exception ex) { AppLogger.Error("AdvancedSettings password failed.", ex); Tl("Password via AdvancedSettings failed", null, ex); }
            }
            else
            {
                Tl("Password assigned", "failed — AdvancedSettings unavailable");
            }
        }

        private string? _lastUser;
        private string? _lastPass;
        private ConnectionProfile? _lastProfile;
        private RdpConnectionQuality _connectionQuality = RdpConnectionQuality.Lan;
        private int _lastRemoteWidth;
        private int _lastRemoteHeight;
        private string _lastEventName = "none";
        private int _lastDisconnectReason;
        private int _lastFatalErrorCode;
        private int _lastLogonErrorCode;

        public string LastEventName => _lastEventName;
        public int LastLogonErrorCode => _lastLogonErrorCode;

        private void TryAssignUiParent()
        {
            if (_ocx == null)
                return;

            try
            {
                nint parentHandle = Parent?.Handle ?? Handle;
                if (parentHandle == nint.Zero)
                    return;

                if (_ocx is NativeMethods.IMsRdpClientNonScriptable5 ns5)
                {
                    ns5.put_UIParent(parentHandle);
                    return;
                }

                dynamic ocx = _ocx;
                var adv = GetAdvancedSettings();
                if (adv != null)
                {
                    try { adv.UIParentWindowHandle = (int)parentHandle; } catch { /* optional */ }
                }
            }
            catch (Exception ex) { AppLogger.Debug($"UIParent assign failed: {ex.Message}", category: "RDP"); }
        }

        public void MarkTransportConnected()
        {
            if (State == RdpState.LoginComplete || State == RdpState.Connected)
                return;

            State = RdpState.Connected;
            RecordEvent("TransportConnected");
        }

        private void ApplyDesktopSizeAfterLogin()
        {
            SyncSessionDisplaySettings();
        }

        /// <summary>
        /// Pushes host pixel size into the live RDP session — required to fix embedded black screen.
        /// </summary>
        public void SyncSessionDisplaySettings(int? width = null, int? height = null)
        {
            if (_ocx == null)
                return;

            try
            {
                if ((int)_ocx.Connected == 0)
                    return;
            }
            catch
            {
                return;
            }

            int w;
            int h;
            if (width is > 0 and var pw && height is > 0 and var ph)
            {
                w = pw;
                h = ph;
            }
            else
            {
                (w, h) = GetHostPixelSize(_lastRemoteWidth, _lastRemoteHeight);
            }

            if (w <= 0 || h <= 0)
                return;

            try
            {
                dynamic ocx = _ocx;
                _lastRemoteWidth = w;
                _lastRemoteHeight = h;

                var adv = GetAdvancedSettings();
                if (adv != null)
                {
                    try { adv.SmartSizing = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    try { adv.DisplayConnectionBar = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                }

                try { ocx.DesktopWidth = w; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { ocx.DesktopHeight = h; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

                var synced = TryUpdateSessionDisplaySettings(ocx, w, h);
                Log(synced
                    ? $"UpdateSessionDisplaySettings OK ({w}x{h})"
                    : $"UpdateSessionDisplaySettings skipped ({w}x{h})");

                SyncHostBoundsToParent();
                try { Parent?.PerformLayout(); } catch { /* optional */ }
                PerformLayout();
                Focus();
                Invalidate(true);
                Update();
            }
            catch (Exception ex)
            {
                AppLogger.Error("RDP SyncSessionDisplaySettings failed.", ex);
            }
        }

        private static bool TryUpdateSessionDisplaySettings(dynamic ocx, int w, int h)
        {
            try
            {
                ocx.UpdateSessionDisplaySettings(w, h, w, h, 0, 100, 100);
                return true;
            }
            catch (Exception ex) { AppLogger.Debug($"UpdateSessionDisplaySettings 7-arg failed: {ex.Message}", category: "RDP"); }

            try
            {
                ocx.UpdateSessionDisplaySettings(w, h, w, h, 0);
                return true;
            }
            catch (Exception ex) { AppLogger.Debug($"UpdateSessionDisplaySettings 5-arg failed: {ex.Message}", category: "RDP"); }

            try
            {
                ocx.UpdateSessionDisplaySettings(w, h, 100);
                return true;
            }
            catch (Exception ex) { AppLogger.Debug($"UpdateSessionDisplaySettings 3-arg failed: {ex.Message}", category: "RDP"); }

            return false;
        }

        public void StartDisplaySyncLoop(int maxPasses = 24, int intervalMs = 500)
        {
            StopDisplaySyncLoop();
            _displaySyncPass = 0;
            _displaySyncTimer = new System.Windows.Forms.Timer { Interval = intervalMs };
            _displaySyncTimer.Tick += (_, _) =>
            {
                _displaySyncPass++;
                SyncSessionDisplaySettings();
                if (_displaySyncPass >= maxPasses)
                    StopDisplaySyncLoop();
            };
            _displaySyncTimer.Start();
            SyncSessionDisplaySettings();
        }

        public void StopDisplaySyncLoop()
        {
            if (_displaySyncTimer == null)
                return;

            try
            {
                _displaySyncTimer.Stop();
                _displaySyncTimer.Dispose();
            }
            catch (Exception ex) { AppLogger.Error("Failed to stop RDP display sync timer.", ex); }
            finally
            {
                _displaySyncTimer = null;
            }
        }

        public void Reconnect()
        {
            if (_lastServer != null && _lastUser != null && _lastPass != null && _lastProfile != null)
            {
                Connect(_lastServer, _lastPort, _lastUser, _lastPass, _lastProfile);
            }
        }

        public void SendCtrlAltDel()
        {
            if (_ocx == null) return;
            try
            {
                var nonScriptable = _ocx as NativeMethods.IMsRdpClientNonScriptable;
                if (nonScriptable != null)
                {
                    bool pbKeyInterrupt = false;
                    object vKeys = new object[] { 0x11, 0x12, 0x2E };
                    nonScriptable.SendKeys(3, ref pbKeyInterrupt, ref vKeys);
                    Log("Ctrl+Alt+Del Triggered.");
                }
            }
            catch (Exception ex) { Log($"Input Error: {ex.Message}"); }
        }

        /// <summary>
        /// Aktif RDP oturumunda Win+R ile komut çalıştırır (WMI/RPC gerekmez).
        /// </summary>
        public async Task<bool> ExecuteRunDialogCommandAsync(string command)
        {
            if (!IsConnected || _ocx == null || string.IsNullOrWhiteSpace(command))
                return false;

            if (InvokeRequired)
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                BeginInvoke(new Action(async () =>
                {
                    try { tcs.TrySetResult(await ExecuteRunDialogCommandCoreAsync(command)); }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Run dialog command failed.", ex);
                        tcs.TrySetResult(false);
                    }
                }));
                return await tcs.Task.ConfigureAwait(true);
            }

            return await ExecuteRunDialogCommandCoreAsync(command).ConfigureAwait(true);
        }

        private async Task<bool> ExecuteRunDialogCommandCoreAsync(string command)
        {
            try
            {
                System.Windows.Clipboard.SetText(command);
                await Task.Delay(700).ConfigureAwait(true);

                SendVirtualKeyChord(0x5B, 0x52); // Win+R
                await Task.Delay(700).ConfigureAwait(true);

                SendVirtualKeyChord(0x11, 0x56); // Ctrl+V
                await Task.Delay(350).ConfigureAwait(true);

                SendVirtualKey(0x0D); // Enter
                Log($"Run-dialog command sent: {command}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Run-dialog command failed: {ex.Message}");
                return false;
            }
        }

        private void SendVirtualKey(int vk)
        {
            if (_ocx is not NativeMethods.IMsRdpClientNonScriptable nonScriptable)
                return;

            bool pbKeyInterrupt = false;
            object vKeys = new object[] { vk };
            nonScriptable.SendKeys(1, ref pbKeyInterrupt, ref vKeys);
        }

        private void SendVirtualKeyChord(int vk1, int vk2)
        {
            if (_ocx is not NativeMethods.IMsRdpClientNonScriptable nonScriptable)
                return;

            bool pbKeyInterrupt = false;
            object vKeys = new object[] { vk1, vk2 };
            nonScriptable.SendKeys(2, ref pbKeyInterrupt, ref vKeys);
        }

        public void Disconnect()
        {
            SafeDisconnect();
        }

        public void SafeDisconnect()
        {
            try
            {
                if (_ocx is null)
                {
                    State = RdpState.Disconnected;
                    return;
                }

                dynamic ocx = _ocx;
                if ((int)ocx.Connected != 0)
                {
                    ocx.Disconnect();
                }
                State = RdpState.Disconnected;
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        public void ForceRefresh()
        {
            try
            {
                SyncSessionDisplaySettings();
                SyncHostBoundsToParent();
                Invalidate(true);
                Update();
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }




        public void SyncHostBoundsToParent()
        {
            try
            {
                Dock = DockStyle.Fill;
                Margin = Padding.Empty;
                Padding = Padding.Empty;
            }
            catch { /* optional */ }
        }

        public void EnsureHostFillsParent() => SyncHostBoundsToParent();

        public bool TryGetSmartSizingEnabled()
        {
            try
            {
                var adv = GetAdvancedSettings();
                if (adv != null)
                    return (int)adv.SmartSizing != 0;
            }
            catch { /* optional */ }

            return false;
        }

        public void SetSmartSizing(bool enabled)
        {
            try
            {
                var adv = GetAdvancedSettings();
                if (adv != null)
                {
                    adv.SmartSizing = enabled ? 1 : 0;
                    Tl("SmartSizing toggled", enabled ? "on" : "off");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("SetSmartSizing failed.", ex);
            }
        }

        public (int? Width, int? Height) TryGetNegotiatedDesktopSize()
        {
            try
            {
                if (_ocx != null)
                    return ((int)_ocx.DesktopWidth, (int)_ocx.DesktopHeight);
            }
            catch (Exception ex) { AppLogger.Error("Failed to read negotiated desktop size.", ex); }

            if (_lastRemoteWidth > 0 && _lastRemoteHeight > 0)
                return (_lastRemoteWidth, _lastRemoteHeight);

            return (null, null);
        }

        public void ApplyConnectionQuality(RdpConnectionQuality quality)
        {
            _connectionQuality = quality;
            if (_ocx == null) return;
            try
            {
                var adv = GetAdvancedSettings();
                RdpRenderingQuality.ApplyProfile(adv, quality);
                Log($"Connection quality profile: {quality}");
            }
            catch (Exception ex) { Log($"Quality profile error: {ex.Message}"); }
        }

        public void SetClipboardRedirect(bool enabled)
        {
            if (_ocx == null) return;
            try
            {
                var adv = GetAdvancedSettings();
                if (adv != null)
                {
                    adv.RedirectClipboard = enabled ? 1 : 0;
                    if (_lastProfile != null) _lastProfile.EnableClipboard = enabled;
                    Log($"Clipboard redirect set to: {enabled}");
                }
            }
            catch (Exception ex) { Log($"Clipboard redirect error: {ex.Message}"); }
        }

        public void ApplyPerformanceLevel(string level)
        {
            if (_ocx == null) return;
            try
            {
                var adv = GetAdvancedSettings();
                if (adv == null) return;

                switch (level?.ToLowerInvariant())
                {
                    case "low":
                        try { adv.Compress = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.PerformanceFlags = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.AllowFontSmoothing = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.EnableDesktopComposition = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.DisableWallpaper = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.DisableThemes = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        break;
                    case "high":
                        try { adv.Compress = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.PerformanceFlags = 0x00000080 | 0x00000100 | 0x00000020; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.AllowFontSmoothing = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.EnableDesktopComposition = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.DisableWallpaper = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.DisableThemes = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        break;
                    default:
                        try { adv.Compress = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.PerformanceFlags = 0x00000080 | 0x00000100; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.AllowFontSmoothing = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.EnableDesktopComposition = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.DisableWallpaper = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { adv.DisableThemes = 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        break;
                }

                Log($"Performance level set to: {level}");
            }
            catch (Exception ex) { Log($"Performance level error: {ex.Message}"); }
        }

        public void SetKeyboardCapture(bool enabled)
        {
            if (_ocx == null) return;
            try
            {
                var adv = GetAdvancedSettings();
                if (adv != null)
                {
                    try { adv.GrabFocusOnConnect = enabled ? 1 : 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    try { adv.EnableWindowsKey = enabled ? 1 : 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                }
            }
            catch (Exception ex) { Log($"Keyboard capture error: {ex.Message}"); }
        }

        public void SetMouseCapture(bool enabled)
        {
            if (_ocx == null) return;
            try
            {
                var adv = GetAdvancedSettings();
                if (adv != null)
                {
                    try { adv.AllowBackgroundInput = enabled ? 0 : 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                }
            }
            catch (Exception ex) { Log($"Mouse capture error: {ex.Message}"); }
        }

        public void SyncState()
        {
            if (_ocx == null) return;

            try
            {
                int connected = TryGetOcxConnected() ?? 0;
                var statusText = TryGetStatusText();
                if (!string.IsNullOrWhiteSpace(statusText))
                    LogVerbose($"Poll status: {statusText}");

                if (connected != 0)
                {
                    if (State == RdpState.Connecting)
                    {
                        Log($"Poll: Transport connected (Connected={connected}).");
                        HandleOnConnected(null, null);
                    }

                    TryPromoteLoginCompleteFromOcxState(connected);
                }
                else if (State == RdpState.Connected || State == RdpState.LoginComplete)
                {
                    LogVerbose("Poll: Disconnection detected via SyncState.");
                    HandleOnDisconnected(null, null);
                }
            }
            catch (COMException com)
            {
                RecordComError(com, "SyncState");
            }
            catch (Exception ex)
            {
                LogVerbose($"SyncState Error: {ex.Message}");
            }
        }

        /// <summary>
        /// OCX events often do not fire when hosted inside WPF WindowsFormsHost.
        /// Poll ClientState/Connected and promote to LoginComplete when the session is active.
        /// </summary>
        private void TryPromoteLoginCompleteFromOcxState(int connected)
        {
            if (State == RdpState.LoginComplete || connected == 0)
                return;

            var clientState = TryGetClientState();
            if (clientState == 0)
            {
                Log("Poll: Active session detected (ClientState=0).");
                if (State != RdpState.LoginComplete)
                    HandleOnLoginComplete(null, null);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnhookEvents();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopDisplaySyncLoop();
                UnhookEvents();
                try { SafeDisconnect(); } catch (Exception ex) { AppLogger.Error("RDP client disconnect during dispose failed.", ex); }
                _ocx = null;
                _isInitialized = false;
            }

            base.Dispose(disposing);
        }

        private void HookEvents()
        {
            if (_ocx == null || _eventsHooked) return;
            try
            {
                object ocxRef = GetOcxObject();
                _eventSink = new RdpOcxEventSink(this);
                var container = (IConnectionPointContainer)ocxRef;
                var iid = typeof(NativeMethods.IMsTscAxEvents).GUID;
                container.FindConnectionPoint(ref iid, out _connectionPoint);
                _connectionPoint?.Advise(_eventSink, out _connectionPointCookie);
                _eventsHooked = true;
                Tl("OCX events hooked", $"connectionPoint cookie={_connectionPointCookie}");
                AppLogger.Debug(
                    $"RDP OCX connection-point events hooked cookie={_connectionPointCookie}",
                    category: "RDP",
                    action: "HookEvents");
            }
            catch (Exception ex)
            {
                AppLogger.Error("RDP connection-point hook failed; trying reflection fallback.", ex);
                Tl("OCX connection-point hook failed", null, ex);
                HookEventsReflection();
            }
        }

        private void HookEventsReflection()
        {
            if (_ocx == null || _eventsHooked) return;
            try
            {
                object ocxRef = GetOcxObject();
                var ocxType = ocxRef.GetType();
                void TryHook(string eventName, string handlerMethodName)
                {
                    try
                    {
                        var ev = ocxType.GetEvent(eventName);
                        if (ev == null) return;
                        var mi = GetType().GetMethod(handlerMethodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        if (mi == null) return;
                        var del = Delegate.CreateDelegate(ev.EventHandlerType!, this, mi, throwOnBindFailure: false);
                        if (del == null) return;
                        ev.AddEventHandler(ocxRef, del);
                        _hookedEvents.Add((ev, del));
                    }
                    catch (Exception ex) { AppLogger.Error("Failed to hook RDP OCX event.", ex); }
                }

                TryHook("OnConnected", nameof(HandleOnConnected));
                TryHook("OnDisconnected", nameof(HandleOnDisconnected));
                TryHook("OnLogonError", nameof(HandleOnLogonError));
                TryHook("OnFatalError", nameof(HandleOnFatalError));
                TryHook("OnConnecting", nameof(HandleOnConnecting));
                TryHook("OnLoginComplete", nameof(HandleOnLoginComplete));
                TryHook("OnWarning", nameof(HandleOnWarning));
                TryHook("OnAuthenticationWarning", nameof(HandleOnAuthenticationWarning));
                TryHook("OnAuthenticationWarningDisplayed", nameof(HandleOnAuthenticationWarningDisplayed));
                TryHook("OnAuthenticationWarningDismissed", nameof(HandleOnAuthenticationWarningDismissed));
                TryHook("OnConfirmClose", nameof(HandleOnConfirmClose));
                if (_hookedEvents.Count > 0)
                    _eventsHooked = true;
                Tl("OCX reflection events hooked", $"count={_hookedEvents.Count}");
                AppLogger.Debug(
                    $"RDP OCX reflection events hooked count={_hookedEvents.Count}",
                    category: "RDP",
                    action: "HookEvents");
            }
            catch (Exception ex)
            {
                Log($"HookEvents Error: {ex.Message}");
                Tl("HookEvents reflection error", null, ex);
            }
        }

        private void HandleOnConfirmClose(object? sender, EventArgs? e)
        {
            RecordTimelineHandler("OnConfirmClose");
            ActiveTimeline?.RecordOcxEvent("OnConfirmClose");
        }

        internal void RaiseOcxConnecting() => BeginInvoke(new Action(() => HandleOnConnecting(null, null)));
        internal void RaiseOcxConnected() => BeginInvoke(new Action(() => HandleOnConnected(null, null)));
        internal void RaiseOcxLoginComplete() => BeginInvoke(new Action(() => HandleOnLoginComplete(null, null)));
        internal void RaiseOcxDisconnected(int reason) => BeginInvoke(new Action(() => HandleOnDisconnected(null, reason)));
        internal void RaiseOcxFatalError(int code) => BeginInvoke(new Action(() => HandleOnFatalError(null, code)));
        internal void RaiseOcxLogonError(int code) => BeginInvoke(new Action(() => HandleOnLogonError(null, code)));
        internal void RaiseOcxWarning(int code) => BeginInvoke(new Action(() => HandleOnWarning(null, code)));
        internal void RaiseOcxAuthenticationWarning(int code) => BeginInvoke(new Action(() => HandleOnAuthenticationWarning(null, code)));
        internal void RaiseOcxAuthenticationWarningDisplayed() => BeginInvoke(new Action(() => HandleOnAuthenticationWarningDisplayed(null, null)));
        internal void RaiseOcxAuthenticationWarningDismissed() => BeginInvoke(new Action(() => HandleOnAuthenticationWarningDismissed(null, null)));
        internal void RaiseOcxRemoteDesktopSizeChange(int width, int height)
        {
            RecordTimelineHandler("OnRemoteDesktopSizeChange", $"{width}x{height}");
            BeginInvoke(new Action(() => SyncSessionDisplaySettings(width, height)));
        }

        private void UnhookEvents()
        {
            if (_connectionPoint != null && _connectionPointCookie != 0)
            {
                try { _connectionPoint.Unadvise(_connectionPointCookie); }
                catch (Exception ex) { AppLogger.Error("Failed to unhook RDP connection point.", ex); }
                _connectionPoint = null;
                _connectionPointCookie = 0;
                _eventSink = null;
                _eventsHooked = false;
            }

            if (_ocx == null || !_eventsHooked)
            {
                _hookedEvents.Clear();
                _eventsHooked = false;
                return;
            }

            try
            {
                foreach (var (ev, handler) in _hookedEvents)
                {
                    try { ev.RemoveEventHandler(_ocx, handler); }
                    catch (Exception ex) { AppLogger.Error("Failed to unhook RDP OCX event.", ex); }
                }
            }
            finally
            {
                _hookedEvents.Clear();
                _eventsHooked = false;
            }
        }

        private void HandleOnConnecting(object? sender, EventArgs? e)
        {
            RecordTimelineHandler("OnConnecting");
            State = RdpState.Connecting;
            RecordEvent("OnConnecting");
            RdpConnectionLogger.Connecting(_lastServer);
            OnConnecting?.Invoke();
        }

        private void HandleOnLoginComplete(object? sender, EventArgs? e)
        {
            RecordTimelineHandler("OnLoginComplete");
            State = RdpState.LoginComplete;
            RecordEvent("OnLoginComplete");
            RdpConnectionLogger.LoginComplete(_lastServer);
            ApplyDesktopSizeAfterLogin();
            StartDisplaySyncLoop();
            OnLoginComplete?.Invoke();
        }

        private void HandleOnConnected(object? sender, EventArgs? e)
        {
            RecordTimelineHandler("OnConnected");
            MarkTransportConnected();
            RecordEvent("OnConnected");
            RdpConnectionLogger.Connected(_lastServer);
            OnConnected?.Invoke();
        }

        private void HandleOnDisconnected(object? sender, object? e)
        {
            int reason = 0;
            try
            {
                if (e is int directReason)
                    reason = directReason;
                else if (e != null)
                {
                    var p = e.GetType().GetProperty("discReason") ?? e.GetType().GetProperty("Reason") ?? e.GetType().GetProperty("reason");
                    if (p != null)
                    {
                        var v = p.GetValue(e);
                        if (v is int i) reason = i;
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            RecordTimelineHandler("OnDisconnected", $"reason={reason}");
            _lastDisconnectReason = reason;
            State = RdpState.Disconnected;
            var ext = TryGetExtendedDisconnectReason();
            RecordEvent("OnDisconnected", reason);
            RdpConnectionLogger.Disconnected(_lastServer, reason, ext);
            OnDisconnected?.Invoke(reason);
        }

        private void HandleOnFatalError(object? sender, object? e)
        {
            int code = ExtractEventCode(e, "errorCode", "Code", "code");
            RecordTimelineHandler("OnFatalError", $"code={code}");
            _lastFatalErrorCode = code;
            State = RdpState.Failed;
            RecordEvent("OnFatalError", code);
            RdpConnectionLogger.FatalError(_lastServer, code);
            OnFatalError?.Invoke(code);
            OnDisconnected?.Invoke(code);
        }

        private void HandleOnLogonError(object? sender, object? e)
        {
            int code = ExtractEventCode(e, "lError", "Error", "error", "ErrorCode", "errorCode");
            RecordTimelineHandler("OnLogonError", $"code={code}");
            _lastLogonErrorCode = code;
            State = RdpState.Failed;
            RecordEvent("OnLogonError", code);
            RdpConnectionLogger.LogonError(_lastServer, code);
            OnLogonError?.Invoke(code);
            OnDisconnected?.Invoke(code);
        }

        private void HandleOnWarning(object? sender, object? e)
        {
            int code = ExtractEventCode(e, "warningCode", "WarningCode", "code", "Code");
            RecordTimelineHandler("OnWarning", $"code={code}");
            RecordEvent("OnWarning", code);
            RdpConnectionLogger.Warning(_lastServer, code);
            OnWarning?.Invoke(code);
        }

        private void HandleOnAuthenticationWarning(object? sender, object? e)
        {
            int code = ExtractEventCode(e, "warningCode", "WarningCode", "code", "Code");
            RecordEvent("OnAuthenticationWarning", code);
            OnAuthenticationWarningDisplayed?.Invoke(code);
        }

        private void HandleOnAuthenticationWarningDisplayed(object? sender, object? e)
        {
            int code = ExtractEventCode(e, "warningCode", "WarningCode", "code", "Code");
            RecordTimelineHandler("OnAuthenticationWarningDisplayed", $"code={code}");
            RecordEvent("OnAuthenticationWarningDisplayed", code);
            OnAuthenticationWarningDisplayed?.Invoke(code);
        }

        private void HandleOnAuthenticationWarningDismissed(object? sender, object? e)
        {
            int code = ExtractEventCode(e, "warningCode", "WarningCode", "code", "Code");
            RecordEvent("OnAuthenticationWarningDismissed", code);
            OnAuthenticationWarningDismissed?.Invoke(code);
        }

        private int ExtractEventCode(object? e, params string[] propertyNames)
        {
            int code = -1;
            try
            {
                if (e is int directCode)
                    return directCode;
                if (e == null) return code;
                foreach (var name in propertyNames)
                {
                    var p = e.GetType().GetProperty(name);
                    if (p == null) continue;
                    var v = p.GetValue(e);
                    if (v is int i)
                        return i;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }

            return code;
        }

        private void RecordEvent(string eventName, int code = 0)
        {
            _lastEventName = eventName;
            LogOcxEvent(eventName, code);
            var hostW = Parent?.ClientSize.Width ?? Width;
            var hostH = Parent?.ClientSize.Height ?? Height;
            LogSessionSnapshot(eventName, hostW, hostH);
        }

        private void LogOcxEvent(string eventName, int code = 0)
        {
            var profileName = _lastProfile?.Name ?? "n/a";
            var userFormat = DescribeUsernameFormat(_lastUser, null);
            var ext = TryGetExtendedDisconnectReason();
            Log(
                $"EVENT {eventName} | server={_lastServer} | profile={profileName} | userFormat={userFormat} | "
                + $"state={State} | code={code} | logonError={_lastLogonErrorCode} | fatalError={_lastFatalErrorCode} | "
                + $"disconnectReason={_lastDisconnectReason} | extDisconnectReason={ext} | "
                + $"desktop={_lastRemoteWidth}x{_lastRemoteHeight} | colorDepth=24");
        }

        private static string DescribeUsernameFormat(string? user, string? domain)
        {
            if (!string.IsNullOrWhiteSpace(domain))
                return "DOMAIN\\user";
            if (string.IsNullOrWhiteSpace(user))
                return "empty";
            if (user.Contains('\\', StringComparison.Ordinal))
                return "DOMAIN\\user";
            if (user.Contains('@', StringComparison.Ordinal))
                return "UPN";
            return "plain";
        }

        public void LogSessionSnapshot(string checkpoint, int hostWidth, int hostHeight)
        {
            RdpBlackScreenDiagnostics.Log(checkpoint, this, hostWidth, hostHeight);
        }

        internal bool TryReadSessionSnapshot(int hostWidth, int hostHeight, out RdpSessionSnapshot snapshot)
        {
            int? ocxConnected = null;
            int desktopW = _lastRemoteWidth;
            int desktopH = _lastRemoteHeight;
            try
            {
                if (_ocx != null)
                {
                    ocxConnected = (int)_ocx.Connected;
                    desktopW = (int)_ocx.DesktopWidth;
                    desktopH = (int)_ocx.DesktopHeight;
                }
            }
            catch (Exception ex) { AppLogger.Error("Failed to read RDP session snapshot.", ex); }

            snapshot = new RdpSessionSnapshot
            {
                Server = _lastServer,
                LastEvent = _lastEventName,
                OcxConnected = ocxConnected,
                HostWidth = hostWidth,
                HostHeight = hostHeight,
                ControlWidth = Width,
                ControlHeight = Height,
                DesktopWidth = desktopW,
                DesktopHeight = desktopH,
                DisconnectReason = _lastDisconnectReason,
                ExtendedDisconnectReason = TryGetExtendedDisconnectReason(),
                LastFatalErrorCode = _lastFatalErrorCode,
                LastLogonErrorCode = _lastLogonErrorCode,
                State = State,
                ClientState = TryGetClientState()
            };
            return true;
        }

        private void ApplyAuthenticationSettings(ConnectionProfile profile)
        {
            var negotiateLayer = profile.NegotiateSecurityLayer ? 1 : 0;
            var adv = GetAdvancedSettings();
            if (adv != null)
            {
                try { adv.EnableCredSspSupport = profile.EnableCredSsp ? 1 : 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.AuthenticationLevel = profile.AuthenticationLevel; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.NegotiateSecurityLayer = negotiateLayer; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.RDPPort = _lastPort; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.SmartSizing = profile.SmartSizing ? 1 : 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.PromptForCredentials = profile.PromptForCredentials ? 1 : 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.PromptForCredsOnClient = profile.PromptForCredentials ? 1 : 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.GrabFocusOnConnect = 1; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                try { adv.RedirectClipboard = profile.EnableClipboard ? 1 : 0; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            }

            try
            {
                var iunk = Marshal.GetIUnknownForObject(_ocx!);
                var iid = NativeMethods.IID_IMsRdpExtendedSettings;
                if (Marshal.QueryInterface(iunk, ref iid, out IntPtr ppv) == 0 && ppv != IntPtr.Zero)
                {
                    try
                    {
                        var ext = (NativeMethods.IMsRdpExtendedSettings)Marshal.GetObjectForIUnknown(ppv);
                        ext.put_Property("WarnAboutTrustedCert", false);
                        ext.put_Property("WarnAboutInvalidCert", false);
                        ext.put_Property("WarnAboutSendingCredentials", false);
                        ext.put_Property("WarnAboutDirectPlayToPlay", false);
                        ext.put_Property("AuthenticationLevel", profile.AuthenticationLevel);
                        ext.put_Property("EnableCredSspSupport", profile.EnableCredSsp);
                        ext.put_Property("NegotiateSecurityLayer", profile.NegotiateSecurityLayer);
                        ext.put_Property("PromptForCredentials", profile.PromptForCredentials);
                        ext.put_Property("PromptForCredsOnClient", profile.PromptForCredentials);
                        ext.put_Property("SmartSizing", profile.SmartSizing);
                        try { ext.put_Property("RedirectDirectX", false); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                        try { ext.put_Property("UseMultimon", false); } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
                    }
                    finally { Marshal.Release(ppv); }
                }
                Marshal.Release(iunk);
            }
            catch (Exception ex) { AppLogger.Error("RDP extended authentication settings failed.", ex); }

            Log(
                $"AuthSettings applied | profile={profile.Name} | credssp={profile.EnableCredSsp} | "
                + $"authLevel={profile.AuthenticationLevel} | negotiateLayer={profile.NegotiateSecurityLayer} | "
                + $"promptCreds={profile.PromptForCredentials}");
            Tl(
                "AdvancedSettings configured",
                $"profile={profile.Name} credssp={profile.EnableCredSsp} authLevel={profile.AuthenticationLevel} "
                + $"negotiateLayer={profile.NegotiateSecurityLayer} port={_lastPort} smartSizing={profile.SmartSizing}");
        }

        private dynamic? GetAdvancedSettings()
        {
            if (_ocx == null) return null;
            // First try dynamic access which is safest
            try { return _ocx.AdvancedSettings9; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { return _ocx.AdvancedSettings8; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { return _ocx.AdvancedSettings7; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { return _ocx.AdvancedSettings2; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            try { return _ocx.AdvancedSettings; } catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            return null;
        }

        private static bool IsVerboseRdpLogEnabled =>
            true;

        private static string RedactUser(string? user)
        {
            if (string.IsNullOrWhiteSpace(user))
                return "—";

            if (user.Contains('\\'))
            {
                var parts = user.Split('\\');
                return parts.Length > 1 ? $"{parts[0]}\\***" : "***";
            }

            return user.Length <= 2 ? "***" : $"{user[0]}***";
        }

        private void Log(string msg) => WriteLog(msg, verbose: false);

        internal void LogSessionLine(string msg) => Log(msg);

        private void LogVerbose(string msg)
        {
            if (!IsVerboseRdpLogEnabled)
                return;

            WriteLog(msg, verbose: true);
        }

        private void WriteLog(string msg, bool verbose)
        {
            Debug.WriteLine($"[RDP] {msg}");
            if (!IsVerboseRdpLogEnabled && verbose)
                return;

            if (IsVerboseRdpLogEnabled)
                TryAppendLogLine($"[RDP] {msg}");
        }

        private static void TryAppendLogLine(string line)
        {
            if (!IsVerboseRdpLogEnabled)
                return;

            // FIX: File I/O offloaded to background — previously ran on the UI thread
            // for every OCX event, adding cumulative disk latency to the hot path.
            var captured = line;
            Task.Run(() =>
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netvium");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, "rdp.log");
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {captured}{Environment.NewLine}");
                }
                catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            });
        }

        public string GetDisconnectReason(int reason)
        {
            return RdpErrorMapper.GetFriendlyReason(reason);
        }

        private bool IsNotImplemented(Exception ex)
        {
            if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                ex = tie.InnerException;

            if (ex is System.Runtime.InteropServices.COMException comEx)
                return comEx.HResult == unchecked((int)0x80004001); // E_NOTIMPL

            var msg = ex.Message;
            if (string.IsNullOrEmpty(msg)) return false;

            return msg.Contains("not implemented", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("uygulanmadı", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("desteklenmiyor", StringComparison.OrdinalIgnoreCase);
        }
    }
}
