using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide;
using Netvium.Services.AIGuide.Decision;

namespace AIGuideQaRunner;

internal static class QaEngine
{
    private static readonly IWorkflowDecisionEngine DecisionEngine = AIGuideCompositionRoot.CreateDecisionEngine();
    private static readonly WorkflowResponseGenerator ResponseGenerator = new();

    public static QaActual Evaluate(QaContext ctx, string question)
    {
        var combined = string.Join("\n", ctx.ScreenText, ctx.AddressOrPathVisible, ctx.SelectedListItem);
        var (detectedKind, snapIn, _) = MmcConsoleDetector.Detect(combined);
        if (ctx.ScreenKind != MmcConsoleKind.Unknown)
            detectedKind = ctx.ScreenKind;

        var visual = new ScreenVisualContext
        {
            ActiveWindowTitle = string.IsNullOrWhiteSpace(ctx.AddressOrPathVisible)
                ? ctx.ScreenText
                : $"{ctx.ScreenText} {ctx.AddressOrPathVisible}",
            OcrWindowTitle = ctx.ScreenText,
            MmcConsole = detectedKind,
            MmcSnapIn = snapIn,
            SelectedListItem = ctx.SelectedListItem,
            SelectedTreeNode = ctx.SelectedTreeNode,
            OcrSelectedRow = ctx.SelectedListItem,
            OcrStatusBar = ctx.AddressOrPathVisible,
            StatusBarText = ctx.AddressOrPathVisible,
            OcrConfidence = 0.75
        };

        WorkflowSessionState? session = null;
        var priorTurns = BuildPriorTurns(ctx);
        foreach (var prior in priorTurns.Where(t => t.Role == "user"))
        {
            var priorDecision = DecisionEngine.Decide(new WorkflowDecisionInput
            {
                UserQuestion = prior.Content,
                VisualContext = visual,
                PriorTurns = Array.Empty<ConversationTurn>(),
                SessionState = session
            });
            if (priorDecision.Context.Intent != AdminUserIntent.Unknown
                && !priorDecision.Context.IsClarification
                && !priorDecision.ShouldRefuse)
            {
                session = WorkflowSessionState.FromDecision(priorDecision, prior.Content);
            }
        }

        var decision = DecisionEngine.Decide(new WorkflowDecisionInput
        {
            UserQuestion = question,
            VisualContext = visual,
            PriorTurns = priorTurns,
            SessionState = session
        });

        var response = ResponseGenerator.Generate(decision, $"qa-{Guid.NewGuid():N}");
        var guidance = response.FormattedAnswer ?? string.Empty;
        var experienceHintText = string.Join(" ", decision.ExperienceHints.Select(h => h.Text));
        var confidence = response.Confidence;
        if (string.IsNullOrWhiteSpace(confidence))
            confidence = decision.Context.ConfidenceCandidate;

        var gLower = guidance.ToLowerInvariant();
        var wrongScreen = decision.Context.IsWrongScreen
            || decision.ShouldRefuse
            || decision.Context.WorkflowName == "wrong-screen-guard";

        return new QaActual
        {
            DetectedScreen = decision.Context.ScreenDisplayName.Length > 0
                ? decision.Context.ScreenDisplayName
                : detectedKind == MmcConsoleKind.Unknown ? "Unknown" : MmcConsoleDetector.SnapInDisplayName(detectedKind),
            DetectedIntent = decision.Context.Intent.ToString(),
            Confidence = confidence,
            GuidanceSnippet = Truncate(guidance, 280),
            WrongScreenDetected = wrongScreen,
            AsksQuestion = decision.ShouldAskQuestion
                || decision.Context.IsClarification
                || gLower.Contains('?')
                || gLower.Contains("eksik bilgi")
                || gLower.Contains("missing information")
                || gLower.Contains("belirtilmedi")
                || gLower.Contains("not specified"),
            RefusesWrongTool = decision.ShouldRefuse
                || gLower.Contains("uygun değil")
                || gLower.Contains("not suitable")
                || gLower.Contains("yapılmaz")
                || gLower.Contains("cannot be"),
            MentionsHiddenTarget = gLower.Contains("görünmüyor")
                || gLower.Contains("not visible")
                || gLower.Contains("navigate first"),
            UsesWrongSelection = !string.IsNullOrWhiteSpace(ctx.SelectedListItem)
                && gLower.Contains(ctx.SelectedListItem.ToLowerInvariant())
                && decision.Context.Intent == AdminUserIntent.FolderPermission
                && !string.IsNullOrWhiteSpace(FolderPermissionExtractor.ExtractPath(question))
                && gLower.Contains("sağ tık", StringComparison.OrdinalIgnoreCase),
            HasVisualEvidence = decision.Context.Evidence.Count > 0
                || gLower.Contains("ekranda gördüğüm")
                || gLower.Contains("on-screen observation")
                || gLower.Contains("görünen kanıt")
                || gLower.Contains("visible evidence")
                || gLower.Contains("ekranda")
                || gLower.Contains("on-screen"),
            HallucinationRisk = UsesInventedTarget(gLower, ctx, question),
            HasExperienceSection = gLower.Contains("dikkat:")
                || gLower.Contains("attention:")
                || decision.ExperienceHints.Count > 0,
            ExperienceHintText = experienceHintText,
            Notes = $"Workflow={decision.Context.WorkflowName}; Refuse={decision.ShouldRefuse}; Score={decision.Context.ConfidenceScore}; ExpHints={decision.ExperienceHints.Count}"
        };
    }

    private static bool UsesInventedTarget(string guidance, QaContext ctx, string question)
    {
        if (string.IsNullOrWhiteSpace(ctx.SelectedListItem))
            return false;
        var path = FolderPermissionExtractor.ExtractPath(question);
        if (path == null)
            return false;
        var folder = path.Split('\\').LastOrDefault() ?? string.Empty;
        return guidance.Contains(ctx.SelectedListItem, StringComparison.OrdinalIgnoreCase)
               && !guidance.Contains(folder, StringComparison.OrdinalIgnoreCase)
               && guidance.Contains("sağ tık", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    private static IReadOnlyList<ConversationTurn> BuildPriorTurns(QaContext ctx)
    {
        var turns = new List<ConversationTurn>();
        foreach (var prior in ctx.PriorTurns)
            turns.Add(new ConversationTurn { Role = "user", Content = prior });

        if (!string.IsNullOrWhiteSpace(ctx.PriorUserMessage))
            turns.Add(new ConversationTurn { Role = "user", Content = ctx.PriorUserMessage });

        return turns;
    }
}
