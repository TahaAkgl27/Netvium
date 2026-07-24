using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Senior Windows admin knowledge injected into guidance prompts per tool and intent.
    /// </summary>
    internal static class AdminToolExpertCatalog
    {
        public static string SelectModuleId(
            MmcConsoleKind visibleTool,
            AdminUserIntent intent,
            bool modalSecurity,
            bool modalPriority)
        {
            if (modalSecurity || (modalPriority && intent == AdminUserIntent.SecurityAlertInterpretation))
                return "EndpointSecurity";

            return intent switch
            {
                AdminUserIntent.PasswordReset or AdminUserIntent.UnlockUser => "ActiveDirectory",
                AdminUserIntent.GroupMembership or AdminUserIntent.UserManagement => "ActiveDirectory",
                AdminUserIntent.DhcpReservation or AdminUserIntent.IpConflictPrevention => "DhcpReservation",
                AdminUserIntent.DhcpManagement => "DhcpManager",
                AdminUserIntent.DnsRecordCreation or AdminUserIntent.DnsManagement => "DnsManager",
                AdminUserIntent.EventId1074Analysis => "EventViewer1074",
                AdminUserIntent.EventLogReview => "EventViewer",
                AdminUserIntent.GroupPolicy => "GroupPolicy",
                AdminUserIntent.ServiceRestart or AdminUserIntent.ServiceManagement => "Services",
                AdminUserIntent.IisBindingOrAppPool => "IisManager",
                AdminUserIntent.FirewallNetworkTroubleshooting => "NetworkSettings",
                AdminUserIntent.FolderPermission => "FolderPermission",
                AdminUserIntent.RdpTroubleshooting => "RemoteDesktop",
                AdminUserIntent.SecurityAlertInterpretation => "EndpointSecurity",
                AdminUserIntent.ConnectionStatus => "ConnectionStatus",
                _ => visibleTool switch
                {
                    MmcConsoleKind.Dhcp => "DhcpManager",
                    MmcConsoleKind.DnsManager => "DnsManager",
                    MmcConsoleKind.EventViewer => "EventViewer",
                    MmcConsoleKind.ActiveDirectoryUsersAndComputers => "ActiveDirectory",
                    MmcConsoleKind.GroupPolicyManagement => "GroupPolicy",
                    MmcConsoleKind.Services => "Services",
                    MmcConsoleKind.IisManager => "IisManager",
                    MmcConsoleKind.ComputerManagement => "ComputerManagement",
                    MmcConsoleKind.EndpointSecurity => "EndpointSecurity",
                    _ => "GeneralWindowsAdmin"
                }
            };
        }

        public static string GetExpertKnowledge(string moduleId, AdminUserIntent intent) =>
            (moduleId, intent) switch
            {
                ("DhcpReservation", AdminUserIntent.DhcpReservation or AdminUserIntent.IpConflictPrevention) => DhcpReservationKnowledge,
                ("DhcpManager", _) => DhcpManagerKnowledge,
                ("ActiveDirectory", AdminUserIntent.PasswordReset) => AdPasswordResetKnowledge,
                ("ActiveDirectory", AdminUserIntent.UnlockUser) => AdUnlockUserKnowledge,
                ("ActiveDirectory", AdminUserIntent.GroupMembership) => AdGroupMembershipKnowledge,
                ("ActiveDirectory", _) => AdGeneralKnowledge,
                ("DnsManager", AdminUserIntent.DnsRecordCreation) => DnsRecordKnowledge,
                ("DnsManager", _) => DnsManagerKnowledge,
                ("EventViewer1074", _) => Event1074Knowledge,
                ("EventViewer", _) => EventViewerKnowledge,
                ("GroupPolicy", _) => GpoKnowledge,
                ("Services", AdminUserIntent.ServiceRestart) => ServiceRestartKnowledge,
                ("Services", _) => ServicesKnowledge,
                ("IisManager", _) => IisKnowledge,
                ("EndpointSecurity", _) => EndpointSecurityKnowledge,
                ("NetworkSettings", _) => NetworkTroubleshootingKnowledge,
                ("RemoteDesktop", _) => RdpKnowledge,
                ("ComputerManagement", _) => ComputerManagementKnowledge,
                ("TaskScheduler", _) => TaskSchedulerKnowledge,
                ("RegistryEditor", _) => RegistryKnowledge,
                ("ServerManager", _) => ServerManagerKnowledge,
                ("FolderPermission", _) => FolderPermissionKnowledge,
                _ => GeneralWindowsAdminKnowledge
            };

        private const string GeneralWindowsAdminKnowledge =
            """
            Answer as a senior Windows administrator. Be concise and actionable.
            State whether the current screen is the right place. If not, name the correct Microsoft tool.
            Provide exact menu/path steps. Mention risks or prerequisites. Do not describe Netvium/RDP unless asked.
            """;

        private const string DhcpReservationKnowledge =
            """
            DHCP Reservation expert guidance:
            - Correct tool: DHCP Manager (IPv4 Scope > Reservations).
            - User must open the relevant IPv4 Scope first from the left tree.
            - Path: IPv4 > [Scope] > Reservations > New Reservation.
            - Required: reservation name, reserved IP (within scope range), MAC address (without dashes or with, per UI).
            - Effect: DHCP assigns that IP only to that MAC; other devices won't receive it.
            - Check: existing lease conflict (Address Leases), duplicate MAC, scope exclusion range.
            - Warn: wrong scope or IP outside scope range will fail.
            """;

        private const string DhcpManagerKnowledge =
            """
            DHCP Manager: scopes, address leases, reservations, scope options, failover.
            Common tasks: new scope, activate scope, exclusion range, reservation, lease cleanup, conflict detection.
            """;

        private const string AdPasswordResetKnowledge =
            """
            AD password reset: Active Directory Users and Computers > find user > right-click > Reset Password.
            If account locked: also check 'Unlock account' on Account tab or via Reset Password dialog context.
            Requires appropriate AD permissions. User must change password at next logon if policy requires.
            """;

        private const string AdUnlockUserKnowledge =
            """
            Unlock AD user: Active Directory Users and Computers > find user > Properties > Account tab > Unlock account.
            Or: right-click user > Unlock. Check badPwdCount and lockoutTime if lockouts recur.
            """;

        private const string AdGroupMembershipKnowledge =
            """
            AD group membership: Active Directory Users and Computers > find user or group > Properties > Member Of / Members tab.
            Add to group: user Properties > Member Of > Add. Or group Properties > Members > Add.
            """;

        private const string AdGeneralKnowledge =
            """
            Active Directory Users and Computers: users, groups, OUs, computers. Common: reset password, unlock, enable/disable, move object, group membership.
            """;

        private const string DnsRecordKnowledge =
            """
            DNS record creation: DNS Manager > Forward Lookup Zone > [zone] > right-click > New Host (A/AAAA), Alias (CNAME), etc.
            Verify zone type (AD integrated vs primary), record name, IP, TTL, and reverse PTR if needed.
            """;

        private const string DnsManagerKnowledge =
            """
            DNS Manager: forward/reverse zones, A, AAAA, CNAME, SRV, MX records, scavenging, zone transfers.
            """;

        private const string Event1074Knowledge =
            """
            Event ID 1074: planned/system-initiated restart/shutdown (user or process triggered).
            If description mentions TrustedInstaller or Windows Update, likely update-related restart.
            If unexpected: also check Event ID 41 (unexpected shutdown), 6008 (unexpected shutdown previous), 1076 (reason not reported).
            """;

        private const string EventViewerKnowledge =
            """
            Event Viewer: Windows Logs (System, Application, Security), filter by Event ID, level, time.
            Export logs for analysis. Correlate System + Application for service failures.
            """;

        private const string GpoKnowledge =
            """
            Group Policy Management: GPO create/link, edit user/computer policies, GPResult, modeling.
            """;

        private const string ServiceRestartKnowledge =
            """
            Service restart: services.msc or Services snap-in > find service > Restart (or Stop then Start).
            Check Dependencies tab first. Note: stopping critical services (AD DS, DNS, DHCP) causes outage.
            """;

        private const string ServicesKnowledge =
            """
            Services console: startup type (Automatic/Manual/Disabled), log on account, recovery actions, dependencies.
            """;

        private const string IisKnowledge =
            """
            IIS Manager: sites, bindings (IP/port/hostname/cert), application pools (identity, .NET version, pipeline), restart app pool, web.config location.
            """;

        private const string EndpointSecurityKnowledge =
            """
            Security alert analysis (Check Point, Defender, etc.):
            - Summarize what happened from visible alert text (domain, device, action blocked).
            - Risk level: calm, factual — blocked DNS/URL attempt is medium unless persistence indicators.
            - Next checks: endpoint logs, DNS cache (ipconfig /displaydns), browser history, running processes, proxy settings, other clients with same behavior.
            - Do not panic. Document domain, time, user/device, and escalate if repeated or high severity.
            """;

        private const string NetworkTroubleshootingKnowledge =
            """
            Network troubleshooting: IP config, DNS resolution, gateway, firewall rules, ping/tracert, adapter settings, VPN split tunnel.
            """;

        private const string RdpKnowledge =
            """
            RDP troubleshooting: NLA, credential, firewall 3389, RD Gateway, licensing, display scaling, reconnection limits.
            Only use when user asks about RDP/remote desktop issues.
            """;

        private const string ComputerManagementKnowledge =
            """
            Computer Management: Device Manager, Disk Management, Services, Event Viewer, Shared Folders, Local Users and Groups.
            """;

        private const string TaskSchedulerKnowledge =
            """
            Task Scheduler: create task, triggers, actions, conditions, history, run with highest privileges, service account.
            """;

        private const string RegistryKnowledge =
            """
            Registry Editor: HKLM/HKCU hives, export before change, permissions. Warn: incorrect changes can break the system.
            """;

        private const string ServerManagerKnowledge =
            """
            Server Manager: roles/features, local server info, remote management, events, BPA.
            File and Storage Services > Shares manages share-level permissions only — not NTFS on subfolders.
            For NTFS permissions on paths like D:\Shared\IT, direct user to File Explorer or Computer Management > Shared Folders.
            """;

        private const string FolderPermissionKnowledge =
            """
            Folder / NTFS / share permission expert guidance:
            - If user provides a path (e.g. D:\Shared\IT), that path is ALWAYS the target — never assume a randomly selected file/folder on screen is the target.
            - Correct tool for NTFS permissions: File Explorer (Properties > Security) or Computer Management > Shared Folders.
            - Server Manager > Shares: share-level permissions only; cannot set NTFS on subfolders like IT under D:\Shared.
            - NTFS path: right-click folder > Properties > Security > Edit > Add > user > Check Names > grant Read & execute, List folder contents, Read (for read-only).
            - If folder is shared: also check Sharing tab > Advanced Sharing > Permissions for network share Read access.
            - State clearly when target folder is NOT visible on screen; give navigation to path first.
            - Do not invent folder names from unrelated selected items.
            - Confidence High only when both screen and user target align; Medium/Low when mismatch or target not visible.
            """;
    }
}
