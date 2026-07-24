using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Separates intent (category) from goal (desired outcome).</summary>
    public sealed class GoalDetectionService
    {
        public WorkflowGoal Detect(AdminUserIntent intent, string userQuestion, WorkflowTarget target)
        {
            var q = Normalize(userQuestion);

            return intent switch
            {
                AdminUserIntent.FolderPermission => DetectFolderPermissionGoal(q, target),
                AdminUserIntent.PasswordReset => WorkflowGoal.ResetPassword,
                AdminUserIntent.UnlockUser => WorkflowGoal.UnlockAccount,
                AdminUserIntent.UserManagement when ContainsAny(q, "sil", "delete") => WorkflowGoal.DeleteUser,
                AdminUserIntent.UserManagement => WorkflowGoal.CreateUser,
                AdminUserIntent.GroupMembership => WorkflowGoal.ManageGroupMembership,
                AdminUserIntent.DhcpReservation => WorkflowGoal.CreateReservation,
                AdminUserIntent.DnsRecordCreation => WorkflowGoal.CreateDnsRecord,
                AdminUserIntent.ServiceRestart or AdminUserIntent.ServiceManagement => WorkflowGoal.RestartService,
                AdminUserIntent.EventLogReview or AdminUserIntent.EventId1074Analysis => WorkflowGoal.ReviewEvents,
                AdminUserIntent.IisBindingOrAppPool => WorkflowGoal.ConfigureIis,
                AdminUserIntent.Unknown => WorkflowGoal.Unknown,
                _ => WorkflowGoal.GeneralGuidance
            };
        }

        private static WorkflowGoal DetectFolderPermissionGoal(string q, WorkflowTarget target)
        {
            if (ContainsAny(q, "sorun", "çalışmıyor", "calismiyor", "troubleshoot", "problem", "hata", "error", "erişemiyor", "erisemiyor"))
                return WorkflowGoal.TroubleshootPermission;

            if (ContainsAny(q, "audit", "denetim", "denetlemek", "kontrol", "incele", "review", "kimler", "listele"))
                return WorkflowGoal.AuditPermission;

            if (!string.IsNullOrWhiteSpace(target.Operation)
                && ContainsAny(target.Operation.ToLowerInvariant(), "audit", "review", "denetim"))
                return WorkflowGoal.AuditPermission;

            return WorkflowGoal.GrantPermission;
        }

        private static bool ContainsAny(string haystack, params string[] needles) =>
            needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

        private static string Normalize(string text) => (text ?? string.Empty).ToLowerInvariant();
    }
}
