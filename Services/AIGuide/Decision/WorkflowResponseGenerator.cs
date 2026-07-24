using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Models.AIGuide.ExperiencePack;
using Netvium.Services.AIGuide.KnowledgePack;

namespace Netvium.Services.AIGuide.Decision
{
    /// <summary>Builds GuidanceResponse from deterministic WorkflowDecisionResult (Knowledge Pack templates).</summary>
    public sealed class WorkflowResponseGenerator
    {
        public GuidanceResponse Generate(
            WorkflowDecisionResult decision,
            string requestId,
            bool suppressManualControlNote = false)
        {
            var ctx = decision.Context;
            var useTurkish = ctx.UseTurkish;
            var steps = decision.ManualStepInstructions.Count > 0
                ? decision.ManualStepInstructions
                : [decision.NextStep.Instruction];

            var formatted = ctx.IsClarification
                ? FormatClarification(ctx, useTurkish, decision.ExperienceHints)
                : ctx.IsWrongScreen
                    ? FormatWrongScreen(ctx, steps, useTurkish, decision.ExperienceHints)
                    : FormatStructured(ctx, steps, useTurkish, decision.ExperienceHints);

            return new GuidanceResponse
            {
                RequestId = requestId,
                Success = true,
                CurrentScreen = ctx.ScreenDisplayName,
                WhatICanSee = ctx.IsClarification
                    ? BuildClarificationSummary(ctx, useTurkish)
                    : BuildWhatICanSee(ctx, useTurkish),
                ManualSteps = steps.ToList(),
                IfYouDoNotSeeIt = string.Empty,
                ImportantNote = suppressManualControlNote ? string.Empty : AIGuideLanguage.ManualControlNote,
                Confidence = ctx.ConfidenceCandidate,
                VisibleEvidence = BuildVisibleEvidence(ctx),
                FormattedAnswer = formatted
            };
        }

        private static List<GuidanceEvidence> BuildVisibleEvidence(WorkflowContext ctx)
        {
            if (ctx.StructuredEvidence != null)
            {
                return ctx.StructuredEvidence.All
                    .Select(e => new GuidanceEvidence { Label = e.Category, Detail = e.Statement })
                    .ToList();
            }

            return ctx.Evidence
                .Select(e => new GuidanceEvidence { Label = "Evidence", Detail = e })
                .ToList();
        }

        private static string FormatClarification(
            WorkflowContext ctx,
            bool useTurkish,
            IReadOnlyList<ExperienceHint> experienceHints)
        {
            var templates = KnowledgePackHost.Current.ResponseTemplates;
            var body = templates.ClarificationTemplate.Format(useTurkish, new Dictionary<string, string>
            {
                ["currentScreen"] = TemplateRenderer.Section("currentScreen", useTurkish),
                ["screenName"] = ctx.ScreenDisplayName,
                ["understood"] = TemplateRenderer.Section("understood", useTurkish),
                ["intentLabel"] = FormatIntentLabel(ctx.Intent, useTurkish),
                ["missingInformation"] = TemplateRenderer.Section("missingInformation", useTurkish),
                ["missingFields"] = FormatBulletList(ctx.MissingFields),
                ["toContinue"] = TemplateRenderer.Section("toContinue", useTurkish),
                ["question"] = ctx.ClarificationQuestion,
                ["confidence"] = TemplateRenderer.Section("confidence", useTurkish),
                ["confidenceLabel"] = AIGuideLanguage.LocalizeConfidenceLabel(ctx.ConfidenceCandidate)
            });

            var attention = BuildAttentionBlock(experienceHints, useTurkish);
            return string.IsNullOrWhiteSpace(attention) ? body : body + "\n\n" + attention;
        }

        private static string BuildClarificationSummary(WorkflowContext ctx, bool useTurkish)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(TemplateRenderer.Section("understood", useTurkish));
            sb.AppendLine(FormatIntentLabel(ctx.Intent, useTurkish));
            sb.AppendLine();
            sb.AppendLine(TemplateRenderer.Section("missingInformation", useTurkish));
            foreach (var field in ctx.MissingFields)
                sb.AppendLine("- " + field);
            return sb.ToString().TrimEnd();
        }

        private static string FormatIntentLabel(AdminUserIntent intent, bool useTurkish)
        {
            var labels = KnowledgePackHost.Current.ResponseTemplates.IntentLabels;
            return labels.TryGetValue(intent.ToString(), out var label)
                ? label.Get(useTurkish)
                : intent.ToString();
        }

        private static string BuildGoalSummary(WorkflowContext ctx, bool useTurkish)
        {
            var templates = KnowledgePackHost.Current.ResponseTemplates.GoalTemplates;
            var notSpecified = TemplateRenderer.Section("notSpecified", useTurkish);

            return ctx.Intent switch
            {
                AdminUserIntent.FolderPermission when ctx.Target.HasUserProvidedPath && templates.TryGetValue("FolderPermissionWithPath", out var fp) =>
                    fp.Format(useTurkish, new Dictionary<string, string>
                    {
                        ["path"] = ctx.Target.Path,
                        ["userName"] = string.IsNullOrWhiteSpace(ctx.Target.UserName) ? notSpecified : ctx.Target.UserName,
                        ["permissionLevel"] = string.IsNullOrWhiteSpace(ctx.Target.PermissionLevel) ? notSpecified : ctx.Target.PermissionLevel
                    }),
                AdminUserIntent.PasswordReset when templates.TryGetValue("PasswordReset", out var pr) => pr.Get(useTurkish),
                AdminUserIntent.UnlockUser when templates.TryGetValue("UnlockUser", out var ul) => ul.Get(useTurkish),
                AdminUserIntent.DhcpReservation when templates.TryGetValue("DhcpReservation", out var dr) => dr.Get(useTurkish),
                AdminUserIntent.DnsRecordCreation when templates.TryGetValue("DnsRecordCreation", out var dns) => dns.Get(useTurkish),
                AdminUserIntent.IisBindingOrAppPool when templates.TryGetValue("IisBindingOrAppPool", out var iis) => iis.Get(useTurkish),
                AdminUserIntent.EventLogReview or AdminUserIntent.EventId1074Analysis when templates.TryGetValue("EventLogReview", out var ev) => ev.Get(useTurkish),
                _ when templates.TryGetValue("Generic", out var generic) =>
                    generic.Format(useTurkish, new Dictionary<string, string> { ["intent"] = FormatIntentLabel(ctx.Intent, useTurkish) }),
                _ => ctx.Intent.ToString()
            };
        }

        private static string BuildWhatICanSee(WorkflowContext ctx, bool useTurkish)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(TemplateRenderer.Section("understoodGoal", useTurkish));
            sb.AppendLine(BuildGoalSummary(ctx, useTurkish));
            sb.AppendLine();
            sb.AppendLine(TemplateRenderer.Section("onScreenObservation", useTurkish));
            sb.AppendLine(ctx.ScreenObservation);
            if (ctx.Evidence.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(TemplateRenderer.Section("visibleEvidence", useTurkish));
                foreach (var e in ctx.Evidence)
                    sb.AppendLine("- " + e);
            }

            return sb.ToString().TrimEnd();
        }

        private static string FormatWrongScreen(
            WorkflowContext ctx,
            IReadOnlyList<string> steps,
            bool useTurkish,
            IReadOnlyList<ExperienceHint> experienceHints)
        {
            var templates = KnowledgePackHost.Current.ResponseTemplates;
            var tokens = BuildCommonTokens(ctx, steps, useTurkish);
            tokens["attentionBlock"] = BuildAttentionBlock(experienceHints, useTurkish);
            return templates.WrongScreenTemplate.Format(useTurkish, tokens);
        }

        private static string FormatStructured(
            WorkflowContext ctx,
            IReadOnlyList<string> steps,
            bool useTurkish,
            IReadOnlyList<ExperienceHint> experienceHints)
        {
            var templates = KnowledgePackHost.Current.ResponseTemplates;
            var tokens = BuildCommonTokens(ctx, steps, useTurkish);
            tokens["evidenceBlock"] = BuildEvidenceBlock(ctx, useTurkish);
            tokens["attentionBlock"] = BuildAttentionBlock(experienceHints, useTurkish);
            return templates.StructuredTemplate.Format(useTurkish, tokens);
        }

        private static Dictionary<string, string> BuildCommonTokens(
            WorkflowContext ctx,
            IReadOnlyList<string> steps,
            bool useTurkish)
        {
            return new Dictionary<string, string>
            {
                ["currentScreen"] = TemplateRenderer.Section("currentScreen", useTurkish),
                ["screenName"] = ctx.ScreenDisplayName,
                ["understoodGoal"] = TemplateRenderer.Section("understoodGoal", useTurkish),
                ["goal"] = BuildGoalSummary(ctx, useTurkish),
                ["onScreenObservation"] = TemplateRenderer.Section("onScreenObservation", useTurkish),
                ["observation"] = ctx.ScreenObservation,
                ["doThisNow"] = TemplateRenderer.Section("doThisNow", useTurkish),
                ["steps"] = FormatNumberedSteps(steps),
                ["confidence"] = TemplateRenderer.Section("confidence", useTurkish),
                ["confidenceLabel"] = AIGuideLanguage.LocalizeConfidenceLabel(ctx.ConfidenceCandidate)
            };
        }

        private static string BuildAttentionBlock(IReadOnlyList<ExperienceHint> hints, bool useTurkish)
        {
            if (hints == null || hints.Count == 0)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine(TemplateRenderer.Section("attention", useTurkish));
            foreach (var hint in hints)
                sb.AppendLine(hint.Text);
            return sb.ToString().TrimEnd();
        }

        private static string BuildEvidenceBlock(WorkflowContext ctx, bool useTurkish)
        {
            if (ctx.Evidence.Count == 0)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine(TemplateRenderer.Section("visibleEvidence", useTurkish));
            foreach (var e in ctx.Evidence)
                sb.AppendLine("- " + e);
            return sb.ToString().TrimEnd();
        }

        private static string FormatNumberedSteps(IReadOnlyList<string> steps)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < steps.Count; i++)
                sb.AppendLine($"{i + 1}. {steps[i]}");
            return sb.ToString().TrimEnd();
        }

        private static string FormatBulletList(IReadOnlyList<string> items)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in items)
                sb.AppendLine("- " + item);
            return sb.ToString().TrimEnd();
        }
    }
}
