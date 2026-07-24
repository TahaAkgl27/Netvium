using System.Windows;
using System.Windows.Forms.Integration;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Rdp;
using Netvium.Rdp.Clean;
using Netvium.ViewModels;
using Netvium.Views;

namespace Netvium
{
    public partial class MainWindow
    {
        private WindowsFormsHost? RdpHost => GetActiveTabView()?.FormsHostElement;
        private System.Windows.Controls.Border? RdpLoadingOverlay => GetActiveTabView()?.SessionLoadingOverlay;
        private System.Windows.Controls.TextBlock? RdpLoadingStatusText => GetActiveTabView()?.SessionLoadingStatusText;

        private RdpSessionTabView? GetActiveTabView() => _activeSession?.TabView;

        private RdpSessionTabView EnsureTabSessionView(RdpTabSession session, RdpSessionViewModel contentVm)
        {
            if (session.TabView != null)
                return session.TabView;

            var view = new RdpSessionTabView
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Visibility = Visibility.Visible,
                SessionId = session.Id
            };

            contentVm.AttachView(view, session.ConnectionCts?.Token ?? CancellationToken.None);
            WireTabSessionView(session, view);
            session.TabView = view;

            if (view.IsLoaded)
                view.TriggerConnectIfReady();

            return view;
        }

        private void WireTabSessionView(RdpTabSession session, RdpSessionTabView view)
        {
            view.ReconnectRequested += async (_, _) =>
            {
                if (_activeSession != session)
                    SwitchToTab(session);

                if (session.ContentViewModel is not RdpSessionViewModel vm)
                    return;

                try
                {
                    await vm.ReconnectAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("RDP reconnect failed.", ex, category: "RDP", action: "Reconnect");
                    view.ShowFailure("Yeniden bağlantı kurulamadı", "Bağlantı bilgilerini ve sunucu erişimini kontrol edin.", view.BuildDiagnosticsSnapshot());
                }
            };
            view.DisconnectRequested += (_, _) =>
            {
                if (_activeSession != session)
                    SwitchToTab(session);
                OnDisconnectClick(this, new RoutedEventArgs());
            };
            view.SendCtrlAltDelRequested += (_, _) => session.CleanSession?.SendCtrlAltDel();
            view.ResolutionProfileReconnectRequested += async (_, profile) =>
            {
                if (_activeSession != session)
                    SwitchToTab(session);

                if (session.ContentViewModel is not RdpSessionViewModel vm)
                    return;

                session.Server.ResolutionProfile = profile;

                try
                {
                    await vm.ReconnectWithResolutionProfileAsync(profile).ConfigureAwait(true);
                    SaveConnectionsToDisk();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("RDP resolution profile reconnect failed.", ex, category: "RDP", action: "ResolutionProfile");
                    view.ShowFailure("Yeniden bağlantı kurulamadı", "Bağlantı bilgilerini ve sunucu erişimini kontrol edin.", view.BuildDiagnosticsSnapshot());
                }
            };
            view.CopyDiagnosticsRequested += (_, _) => OnRdpFailureCopyDiagnosticsClick(this, new RoutedEventArgs());
            view.FailureRetryRequested += (_, _) => OnRdpFailureRetryClick(this, new RoutedEventArgs());
        }

        private void ShowActiveTabSessionView()
        {
            ApplyActiveWorkspaceContent();
        }

        private void RemoveTabSessionView(RdpTabSession session)
        {
            if (session.TabView == null)
                return;

            try
            {
                session.ContentViewModel?.DetachView();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to remove tab session view.", ex);
            }

            session.TabView = null;
            session.ContentViewModel = null;
        }

        private void ShowRdpSessionWorkspace()
        {
            if (RdpPlaceholder != null)
                RdpPlaceholder.Visibility = Visibility.Collapsed;
            if (RdpContentGrid != null)
                RdpContentGrid.Visibility = Visibility.Visible;

            ShowActiveTabSessionView();
            ApplyRdpFullViewportChrome(active: true);
        }

        private void HideRdpSessionWorkspace()
        {
            if (CentralWorkspaceHost != null)
            {
                CentralWorkspaceHost.Content = DashboardTabContentViewModel.Instance;
                CentralWorkspaceHost.Visibility = Visibility.Collapsed;
            }

            ApplyActiveWorkspaceContent();
        }
    }
}
