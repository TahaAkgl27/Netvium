using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Advances folder-permission and AD steps on continuation turns.</summary>
    public sealed class WorkflowStepProgressionService
    {
        public WorkflowStepResult? TryGetContinuationStep(
            ConversationContinuityResult continuity,
            WorkflowContext context,
            WorkflowSessionState? session)
        {
            if (!continuity.IsContinuation || session == null)
                return null;

            return session.Intent switch
            {
                AdminUserIntent.FolderPermission => GetFolderPermissionStep(continuity, context, session),
                AdminUserIntent.PasswordReset => GetPasswordResetStep(continuity, context, session),
                AdminUserIntent.UnlockUser => GetUnlockStep(continuity, context, session),
                AdminUserIntent.DhcpReservation => GetDhcpStep(continuity, context, session),
                _ => null
            };
        }

        private static WorkflowStepResult? GetFolderPermissionStep(
            ConversationContinuityResult continuity,
            WorkflowContext context,
            WorkflowSessionState session)
        {
            var tr = context.UseTurkish;
            var folder = !string.IsNullOrWhiteSpace(context.Target.FolderName)
                ? context.Target.FolderName
                : context.Target.Path;
            var user = context.Target.UserName;
            var perm = context.Target.PermissionLevel;

            if (continuity.IsFolderOpenedAck && context.TargetVisible)
            {
                return WorkflowGuideSupport.Step(
                    "right-click-properties",
                    2,
                    WorkflowGuideSupport.Localize(tr,
                        $"Right-click the {folder} folder and choose Properties.",
                        $"{folder} klasörüne sağ tıkla ve Properties / Özellikler seç."));
            }

            if (!continuity.ShouldAdvanceStep && !continuity.IsFolderOpenedAck)
            {
                return ResolveFolderPermissionByScreenState(context, session, folder, user, perm, tr);
            }

            if (continuity.ShouldAdvanceStep)
            {
                return session.LastStepId switch
                {
                    "navigate" or "sm-redirect" or "navigate-unknown" or "navigate-target"
                        when context.TargetVisible => WorkflowGuideSupport.Step(
                            "right-click-properties",
                            2,
                            WorkflowGuideSupport.Localize(tr,
                                $"Right-click the {folder} folder and choose Properties.",
                                $"{folder} klasörüne sağ tıkla ve Properties / Özellikler seç.")),

                    "right-click-properties" or "target" => WorkflowGuideSupport.Step(
                        "open-security-tab",
                        3,
                        WorkflowGuideSupport.Localize(tr,
                            "In Properties, open the Security tab.",
                            "Properties > Security / Güvenlik sekmesini aç.")),

                    "open-security-tab" => WorkflowGuideSupport.Step(
                        "edit-permissions",
                        4,
                        WorkflowGuideSupport.Localize(tr,
                            "Click Edit, then Add, enter the user name, and grant the permission level.",
                            "Edit > Add ile kullanıcıyı ekle ve izin seviyesini seç.")),

                    "edit-permissions" => RenderAddPermissionStep(folder, user, perm, tr),

                    _ => ResolveFolderPermissionByScreenState(context, session, folder, user, perm, tr)
                };
            }

            return null;
        }

        private static WorkflowStepResult? ResolveFolderPermissionByScreenState(
            WorkflowContext context,
            WorkflowSessionState session,
            string folder,
            string user,
            string perm,
            bool tr)
        {
            if (context.TargetVisible)
            {
                if (session.LastStepId is "navigate" or "sm-redirect" or "navigate-unknown" or "navigate-target")
                {
                    return WorkflowGuideSupport.Step(
                        "right-click-properties",
                        2,
                        WorkflowGuideSupport.Localize(tr,
                            $"Right-click the {folder} folder and choose Properties.",
                            $"{folder} klasörüne sağ tıkla ve Properties / Özellikler seç."));
                }

                if (session.LastStepId is "right-click-properties")
                {
                    return WorkflowGuideSupport.Step(
                        "open-security-tab",
                        3,
                        WorkflowGuideSupport.Localize(tr,
                            "In Properties, open the Security tab.",
                            "Properties > Security / Güvenlik sekmesini aç."));
                }
            }

            return null;
        }

        private static WorkflowStepResult RenderAddPermissionStep(
            string folder,
            string user,
            string perm,
            bool tr)
        {
            var userPart = string.IsNullOrWhiteSpace(user) ? string.Empty : user;
            var permPart = string.IsNullOrWhiteSpace(perm) ? "Read" : perm;

            var en = string.IsNullOrWhiteSpace(userPart)
                ? $"On the Security tab for {folder}, click Edit > Add, enter the user, and grant {permPart}."
                : $"On the Security tab, click Edit > Add, type {userPart}, click Check Names, and grant {permPart}.";

            var trText = string.IsNullOrWhiteSpace(userPart)
                ? $"{folder} için Security sekmesinde Edit > Add ile kullanıcıyı ekle ve {permPart} iznini ver."
                : $"Security sekmesinde Edit > Add > {userPart} yaz, Check Names yap ve {permPart} iznini ver.";

            return WorkflowGuideSupport.Step("add-permission", 5, WorkflowGuideSupport.Localize(tr, en, trText));
        }

        private static WorkflowStepResult? GetPasswordResetStep(
            ConversationContinuityResult continuity,
            WorkflowContext context,
            WorkflowSessionState session)
        {
            if (!continuity.IsContinuation)
                return null;

            var tr = context.UseTurkish;
            var user = context.Target.UserName;

            if (session.LastStepId is "find-user" or "open-aduc" or "generic")
            {
                return WorkflowGuideSupport.Step(
                    "reset",
                    2,
                    WorkflowGuideSupport.Localize(tr,
                        string.IsNullOrWhiteSpace(user)
                            ? "Right-click the user and choose Reset Password."
                            : $"Right-click {user} and choose Reset Password.",
                        string.IsNullOrWhiteSpace(user)
                            ? "Kullanıcıya sağ tık > Reset Password."
                            : $"{user} kullanıcısına sağ tık > Reset Password."));
            }

            return null;
        }

        private static WorkflowStepResult? GetUnlockStep(
            ConversationContinuityResult continuity,
            WorkflowContext context,
            WorkflowSessionState session)
        {
            if (!continuity.IsContinuation)
                return null;

            var tr = context.UseTurkish;
            var user = context.Target.UserName;

            return WorkflowGuideSupport.Step(
                "unlock",
                2,
                WorkflowGuideSupport.Localize(tr,
                    string.IsNullOrWhiteSpace(user)
                        ? "Properties > Account > Unlock account, or right-click > Unlock."
                        : $"Select {user}, then Properties > Account > Unlock account.",
                    string.IsNullOrWhiteSpace(user)
                        ? "Properties > Account > Unlock account veya sağ tık > Unlock."
                        : $"{user} seç, Properties > Account > Unlock account."));
        }

        private static WorkflowStepResult? GetDhcpStep(
            ConversationContinuityResult continuity,
            WorkflowContext context,
            WorkflowSessionState session)
        {
            if (!continuity.IsContinuation)
                return null;

            var tr = context.UseTurkish;
            var ipHint = !string.IsNullOrWhiteSpace(context.Target.IpAddress)
                ? $" ({context.Target.IpAddress})"
                : string.Empty;

            return WorkflowGuideSupport.Step(
                "reservation",
                2,
                WorkflowGuideSupport.Localize(tr,
                    $"IPv4 > Scope > Reservations > New Reservation. Enter MAC and IP{ipHint}.",
                    $"IPv4 > Scope > Reservations > New Reservation. MAC ve IP gir{ipHint}."));
        }
    }
}
