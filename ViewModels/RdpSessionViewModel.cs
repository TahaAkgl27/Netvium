using Netvium.Models;
using Netvium.Rdp;
using Netvium.Rdp.Clean;
using Netvium.Services;
using Netvium.Views;

namespace Netvium.ViewModels
{
    public sealed class RdpSessionViewModel : ViewModelBase, IDisposable
    {
        private TaskCompletionSource _connectAttemptTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken _connectToken;
        private bool _connectStarted;
        private CancellationTokenSource? _retryCts;
        private Task? _automaticRetryTask;
        private bool _connectFlowActive;
        private bool _disposed;

        private RdpSessionViewModel(ServerInfo server)
        {
            Server = server;
            DisplayName = string.IsNullOrWhiteSpace(server.Name) ? server.Ip : server.Name;
            Host = server.Ip.Trim();
            Port = server.Port <= 0 ? 3389 : server.Port;
            Username = server.Username ?? string.Empty;
            Password = server.Password ?? string.Empty;
            CleanSession = new RdpCleanSession();
            CleanSession.FailureDetected += OnFailureDetected;

        }

        public ServerInfo Server { get; }
        public string DisplayName { get; }
        public string Host { get; }
        public int Port { get; }
        public string Username { get; }
        public string Password { get; }
        public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
        public RdpCleanSession CleanSession { get; }
        public RdpSessionTabView? SessionView { get; private set; }
        public Task ConnectAttemptTask => _connectAttemptTcs.Task;

        private void LogRdp(string message) => RdpCleanHostLogger.Log(message, SessionView?.SessionId ?? CleanSession.SessionId);

        public static RdpSessionViewModel Create(ServerInfo server)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));

            if (string.IsNullOrWhiteSpace(server.Ip))
                throw new ArgumentException("Host is required.", nameof(server));

            return new RdpSessionViewModel(server);
        }

        public void AttachView(RdpSessionTabView view, CancellationToken connectToken)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            SessionView = view;
            CleanSession.SessionId = view.SessionId;
            _connectToken = connectToken;
            view.DataContext = this;
            view.ResolutionProfile = Server.ResolutionProfile;
            OnPropertyChanged(nameof(SessionView));
        }

        public void DetachView()
        {
            SessionView = null;
            OnPropertyChanged(nameof(SessionView));
        }

        public async Task ReconnectAsync()
        {
            if (SessionView == null)
                throw new InvalidOperationException("RdpSessionTabView is not attached.");

            CancelAutomaticRetry();
            CleanSession.Disconnect();
            _connectStarted = false;
            _connectAttemptTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            SessionView.ShowConnecting($"Reconnecting to {DisplayName}...");
            await SessionView.ResetHostForReconnectAsync().ConfigureAwait(true);
            await StartConnectAsync().ConfigureAwait(true);
        }

        public async Task ReconnectWithResolutionProfileAsync(RdpResolutionProfile profile)
        {
            if (SessionView == null)
                throw new InvalidOperationException("RdpSessionTabView is not attached.");

            Server.ResolutionProfile = profile;
            SessionView.ResolutionProfile = profile;

            CancelAutomaticRetry();
            CleanSession.Disconnect();
            _connectStarted = false;
            _connectAttemptTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var label = RdpViewportSizing.GetProfileLabel(profile);
            if (profile == RdpResolutionProfile.Native)
                SessionView.ShowConnecting($"Applying {label} (viewport resolution) to {DisplayName}...");
            else
            {
                var (w, h) = RdpViewportSizing.GetProfileDesktopSize(profile, 0, 0);
                SessionView.ShowConnecting($"Applying {label} ({w}x{h}) to {DisplayName}...");
            }
            await SessionView.ResetHostForReconnectAsync().ConfigureAwait(true);
            await StartConnectAsync().ConfigureAwait(true);
        }

        public async Task StartConnectAsync()
        {
            if (_disposed || _connectStarted)
                return;

            _connectStarted = true;
            _connectFlowActive = true;
            using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(_connectToken);
            _retryCts = retryCts;
            try
            {
                var sessionView = SessionView ?? throw new InvalidOperationException("RdpSessionTabView is not attached.");
                var settings = SettingsService.Instance.Current;
                var automaticRetry = settings.ReconnectAutomatically;
                var maxRetries = automaticRetry ? RdpRetryPolicy.MaxAutoRetries : 0;
                var attempt = 0;

                while (true)
                {
                    retryCts.Token.ThrowIfCancellationRequested();
                    if (attempt > 0)
                    {
                        var delay = RdpRetryPolicy.GetDelay(attempt);
                        sessionView.ShowConnecting($"Bağlantı kesildi. {delay.TotalSeconds:0} saniye sonra yeniden deneniyor… ({attempt}/{maxRetries})");
                        RdpCleanHostLogger.Log(RdpFailureLogForRetry(attempt, delay), CleanSession.SessionId);
                        await Task.Delay(delay, retryCts.Token).ConfigureAwait(true);
                        CleanSession.DisconnectForRetry();
                        await sessionView.ResetHostForReconnectAsync().ConfigureAwait(true);
                    }

                    LogPayload(viewModelBound: true, winFormsClient: attempt == 0 ? "pending" : "retry");
                    sessionView.ShowConnecting(attempt == 0 ? $"Connecting to {DisplayName}..." : "Bağlantı yeniden kuruluyor…");
                    if (!await sessionView.PrepareHostForConnectAsync().ConfigureAwait(true))
                        throw new InvalidOperationException("RDP viewer layout is not ready.");

                    sessionView.ApplyLayoutResize(updateSessionDisplay: false);
                    sessionView.ApplyDpiScaleToHost();
                    sessionView.ResolutionProfile = Server.ResolutionProfile;
                    var plan = sessionView.BuildConnectSizingPlan();
                    sessionView.HostControl.ConnectTimeoutSeconds = Math.Clamp(settings.DefaultConnectionTimeoutSeconds, 5, 120);
                    sessionView.HostControl.SetConnectSizing(plan);
                    sessionView.LogRenderingDiagnostics(RdpDiagnosticsPhase.BeforeConnect);

                    await CleanSession.ConnectAsync(Server, sessionView.HostControl, retryCts.Token).ConfigureAwait(true);
                    if (CleanSession.IsConnected)
                    {
                        sessionView.SetResolutionProfile(plan.Profile);
                        sessionView.ApplyLayoutResize(updateSessionDisplay: true);
                        sessionView.LogRenderingDiagnostics(RdpDiagnosticsPhase.AfterConnect);
                        CleanSession.ResetRetryState();
                        _connectAttemptTcs.TrySetResult();
                        return;
                    }

                    var failure = CleanSession.LastFailure
                        ?? RdpFailureClassifier.Classify(CleanSession.SessionId, "ConnectResult");
                    if (!automaticRetry || !RdpRetryPolicy.ShouldRetry(failure, attempt + 1))
                    {
                        CleanSession.PublishTerminalFailure(failure);
                        sessionView.ShowFailure("Bağlantı kurulamadı", failure.SafeMessage, BuildDiagnosticsReport());
                        _connectAttemptTcs.TrySetException(new InvalidOperationException(failure.SafeMessage));
                        return;
                    }
                    attempt++;
                    CleanSession.SetRetryAttempt(attempt);
                }
            }
            catch (OperationCanceledException)
            {
                _connectAttemptTcs.TrySetCanceled(retryCts.Token);
            }
            catch (Exception ex)
            {
                var safe = CleanSession.LastFailure?.SafeMessage ?? "RDP bağlantısı kurulamadı.";
                LogRdp($"StartConnectAsync failed type={ex.GetType().Name} hresult={ex.HResult}");
                SessionView?.ShowFailure("Bağlantı kurulamadı", safe, BuildDiagnosticsReport());
                _connectAttemptTcs.TrySetException(new InvalidOperationException(safe));
            }
            finally
            {
                _connectFlowActive = false;
                if (ReferenceEquals(_retryCts, retryCts))
                    _retryCts = null;
            }
        }

        private string RdpFailureLogForRetry(int attempt, TimeSpan delay) =>
            $"RetryScheduled attempt={attempt}/{RdpRetryPolicy.MaxAutoRetries} delaySeconds={delay.TotalSeconds:0} category={CleanSession.LastFailure?.Category.ToString() ?? "Unknown"}";

        private void OnFailureDetected(RdpFailureInfo failure)
        {
            if (_disposed || _connectFlowActive || !_connectStarted || !SettingsService.Instance.Current.ReconnectAutomatically)
                return;
            if (!RdpRetryPolicy.ShouldRetry(failure, 1) || _automaticRetryTask is { IsCompleted: false })
                return;
            _connectStarted = false;
            _automaticRetryTask = StartConnectAsync();
        }

        public void CancelAutomaticRetry()
        {
            try { _retryCts?.Cancel(); } catch { }
            _automaticRetryTask = null;
        }
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _connectStarted = false;
            CancelAutomaticRetry();
            CleanSession.FailureDetected -= OnFailureDetected;
            CleanSession.Dispose();
        }
        public void ApplySidebarWidth(double sidebarWidthDip)
        {
            if (SessionView != null)
                SessionView.SidebarWidthDip = sidebarWidthDip;
        }

        public string BuildDiagnosticsReport()
        {
            var viewReport = SessionView?.BuildRenderingDiagnosticsReport(RdpDiagnosticsPhase.Manual) ?? "view=not-attached";
            var clientState = CleanSession.ConnectionState;
            var locked = SessionView?.HostControl.GetLockedDesktopSize();
            var lockedText = locked.HasValue ? $"{locked.Value.DesktopWidth}x{locked.Value.DesktopHeight}" : "n/a";
            var profile = RdpViewportSizing.GetProfileLabel(Server.ResolutionProfile);
            return $"Server: {DisplayName}{Environment.NewLine}"
                + $"Host: {Host}{Environment.NewLine}"
                + $"Port: {Port}{Environment.NewLine}"
                + $"Username provided: {(HasUsername ? "yes" : "no")}{Environment.NewLine}"
                + $"Resolution profile: {profile}{Environment.NewLine}"
                + $"ViewModelBound: yes{Environment.NewLine}"
                + $"Final state: {clientState}{Environment.NewLine}"
                + $"ActiveX version: {SessionView?.HostControl.ActiveControlType ?? "none"}{Environment.NewLine}"
                + $"ActiveX CLSID: {SessionView?.HostControl.ActiveControlClsid ?? "n/a"}{Environment.NewLine}"
                + $"Locked remote: {lockedText}{Environment.NewLine}"
                + viewReport;
        }

        private void LogPayload(bool viewModelBound, string winFormsClient)
        {
            LogRdp(
                $"Host={Host} Port={Port} Username provided={(HasUsername ? "yes" : "no")} "
                + $"Profile={RdpViewportSizing.GetProfileLabel(Server.ResolutionProfile)} "
                + $"ViewModelBound={(viewModelBound ? "yes" : "no")} winFormsClient={winFormsClient}");
        }
    }
}
