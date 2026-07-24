using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.KnowledgePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    internal static class WorkflowConditionEvaluator
    {
        public static bool Matches(string condition, WorkflowGuideBuildInput input, bool targetVisible)
        {
            if (string.IsNullOrWhiteSpace(condition) || condition == "always")
                return true;

            return condition switch
            {
                "screen_is_server_manager" => input.ScreenType == MmcConsoleKind.ServerManager,
                "has_path_and_not_target_visible" => input.Target.HasUserProvidedPath && !targetVisible,
                "no_path" => !input.Target.HasUserProvidedPath && string.IsNullOrWhiteSpace(input.Target.FolderName),
                "target_visible" => targetVisible,
                "intent_password_reset" => input.Intent == AdminUserIntent.PasswordReset,
                "intent_unlock" => input.Intent == AdminUserIntent.UnlockUser,
                "intent_password_or_unlock" => input.Intent is AdminUserIntent.PasswordReset or AdminUserIntent.UnlockUser,
                "intent_group" => input.Intent == AdminUserIntent.GroupMembership,
                "intent_user_management" => input.Intent == AdminUserIntent.UserManagement,
                "intent_dhcp_reservation" => input.Intent == AdminUserIntent.DhcpReservation,
                "intent_ip_conflict" => input.Intent == AdminUserIntent.IpConflictPrevention,
                "intent_dns_record" => input.Intent == AdminUserIntent.DnsRecordCreation,
                "intent_event_1074" => input.Intent == AdminUserIntent.EventId1074Analysis,
                _ => false
            };
        }

        public static string PickObservationKey(WorkflowDefinition def, WorkflowGuideBuildInput input, bool targetVisible)
        {
            if (input.Intent == AdminUserIntent.FolderPermission)
            {
                if (input.ScreenType == MmcConsoleKind.ServerManager)
                    return input.Target.HasUserProvidedPath || !string.IsNullOrWhiteSpace(input.Target.FolderName)
                        ? "serverManagerWithTarget" : "serverManagerGeneric";
                if (targetVisible) return "targetVisible";
                if (input.Target.HasUserProvidedPath || !string.IsNullOrWhiteSpace(input.Target.FolderName))
                    return "targetHiddenWithPath";
                return "targetHiddenNoPath";
            }

            if (input.Intent == AdminUserIntent.EventId1074Analysis && def.Observations.ContainsKey("event1074"))
                return "event1074";

            return input.ScreenType switch
            {
                MmcConsoleKind.ActiveDirectoryUsersAndComputers when input.Intent is AdminUserIntent.PasswordReset or AdminUserIntent.UnlockUser or AdminUserIntent.UserManagement or AdminUserIntent.GroupMembership => "onCorrectScreen",
                MmcConsoleKind.Dhcp when input.Intent is AdminUserIntent.DhcpReservation or AdminUserIntent.DhcpManagement or AdminUserIntent.IpConflictPrevention => "onCorrectScreen",
                MmcConsoleKind.DnsManager when input.Intent is AdminUserIntent.DnsRecordCreation or AdminUserIntent.DnsManagement => "onCorrectScreen",
                MmcConsoleKind.IisManager when input.Intent == AdminUserIntent.IisBindingOrAppPool => "onCorrectScreen",
                MmcConsoleKind.EventViewer when input.Intent is AdminUserIntent.EventLogReview or AdminUserIntent.EventId1074Analysis => "onCorrectScreen",
                _ => def.Observations.ContainsKey("wrongScreen") ? "wrongScreen" : "default"
            };
        }
    }
}
