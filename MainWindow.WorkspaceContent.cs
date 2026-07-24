using System;
using System.Windows;
using Netvium.Helpers;
using Netvium.Models;
using Netvium.Rdp.Clean;
using Netvium.ViewModels;
using Netvium.Views;

namespace Netvium
{
    public partial class MainWindow
    {
        private void ApplyActiveWorkspaceContent()
        {
            try
            {
                if (_activeSession?.ContentType == TabContentType.RdpSession && _activeSession.ContentViewModel != null)
                {
                    var tabLabel = GetRdpTabLabel(_activeSession);
                    RdpCleanHostLogger.Log($"SELECTED_TAB={tabLabel} CONTENT_TYPE=RdpSession");

                    if (CentralWorkspaceHost != null)
                    {
                        CentralWorkspaceHost.Visibility = Visibility.Visible;
                        CentralWorkspaceHost.Content = _activeSession.ContentViewModel;
                        ForceWorkspaceLayoutPass();
                    }

                    if (SelectedServerPanel != null)
                        SelectedServerPanel.Visibility = Visibility.Collapsed;
                    if (RdpPlaceholder != null)
                        RdpPlaceholder.Visibility = Visibility.Collapsed;
                    if (RdpContentGrid != null)
                        RdpContentGrid.Visibility = Visibility.Visible;

                    _activeSession.TabView?.TriggerConnectIfReady();
                    return;
                }

                RdpCleanHostLogger.Log("SELECTED_TAB=Dashboard CONTENT_TYPE=Dashboard");

                if (CentralWorkspaceHost != null)
                {
                    CentralWorkspaceHost.Content = DashboardTabContentViewModel.Instance;
                    CentralWorkspaceHost.Visibility = Visibility.Collapsed;
                }

                if (HasOpenRdpTab())
                    return;

                if (SelectedServerPanel != null && _activeDashboardServer != null)
                    SelectedServerPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to apply active workspace content.", ex);
            }
        }

        private static string GetRdpTabLabel(RdpTabSession session) =>
            string.IsNullOrWhiteSpace(session.Server.Name) ? session.Server.Ip : session.Server.Name;

        private bool HasOpenRdpTab() =>
            _activeSession != null && _rdpSessions.Contains(_activeSession);

        private RdpSessionViewModel CreateSessionViewModel(RdpTabSession session, ServerInfo connectServer)
        {
            var vm = RdpSessionViewModel.Create(connectServer);
            session.ContentViewModel = vm;
            session.ContentType = TabContentType.RdpSession;
            session.CleanSession = vm.CleanSession;
            return vm;
        }

        private void ForceWorkspaceLayoutPass()
        {
            try
            {
                MainContentGrid?.UpdateLayout();
                RdpContentGrid?.UpdateLayout();
                CentralWorkspaceHost?.UpdateLayout();
                _activeSession?.TabView?.UpdateLayout();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to force workspace layout pass.", ex);
            }
        }
    }
}
