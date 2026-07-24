using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class AnswerQualityChecker
    {
        public static AnswerQualityResult Check(
            GuidanceResponse response,
            AdminToolGuidanceHint hint,
            string userQuestion,
            bool isFreshContext)
        {
            var text = (response.WhatICanSee + " " + response.FormattedAnswer + " "
                + string.Join(" ", response.ManualSteps)).ToLowerInvariant();
            var question = userQuestion.ToLowerInvariant();

            var intentAddressed = response.Success
                && (!string.IsNullOrWhiteSpace(response.WhatICanSee) || response.ManualSteps.Count > 0);

            var wrongToolMentioned = hint.IsToolMismatch
                && (text.Contains("bu işlem bu ekrandan yapılmaz", StringComparison.Ordinal)
                    || text.Contains("doğru araç", StringComparison.Ordinal)
                    || text.Contains("cannot be done from", StringComparison.OrdinalIgnoreCase)
                    || text.Contains(hint.RecommendedToolName.ToLowerInvariant()));

            var actionable = response.ManualSteps.Count >= 1
                || text.Length >= 80
                || hint.UseLocalAnswer;

            var visibleToolCorrect = !hint.IsToolMismatch || wrongToolMentioned;

            if (hint.UserIntent == AdminUserIntent.DhcpReservation && hint.IsCorrectTool)
                intentAddressed = intentAddressed && (text.Contains("reservation", StringComparison.Ordinal)
                    || text.Contains("rezerv", StringComparison.Ordinal));

            if (hint.UserIntent == AdminUserIntent.PasswordReset && hint.IsToolMismatch)
                intentAddressed = intentAddressed && wrongToolMentioned;

            if (hint.UserIntent == AdminUserIntent.FolderPermission)
            {
                intentAddressed = intentAddressed
                    && (text.Contains("anladığım hedef", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("understood goal", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("security", StringComparison.Ordinal)
                        || text.Contains("güvenlik", StringComparison.Ordinal)
                        || text.Contains("properties", StringComparison.Ordinal)
                        || text.Contains("özellik", StringComparison.Ordinal)
                        || text.Contains("file explorer", StringComparison.Ordinal)
                        || text.Contains("dosya gezgini", StringComparison.Ordinal));
            }

            var notes = new List<string>();
            if (!intentAddressed) notes.Add("intent-not-fully-addressed");
            if (!actionable) notes.Add("not-actionable-enough");
            if (hint.IsToolMismatch && !wrongToolMentioned) notes.Add("wrong-tool-not-mentioned");
            if (!isFreshContext) notes.Add("stale-context-risk");

            var passed = response.Success
                && intentAddressed
                && actionable
                && visibleToolCorrect
                && isFreshContext;

            return new AnswerQualityResult
            {
                Passed = passed,
                VisibleToolCorrect = visibleToolCorrect,
                IntentAddressed = intentAddressed,
                WrongToolMentioned = wrongToolMentioned,
                Actionable = actionable,
                AvoidedStaleContext = isFreshContext,
                Notes = notes
            };
        }
    }
}
