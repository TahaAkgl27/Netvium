using System.Text.RegularExpressions;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide;

internal static partial class VisualGuidanceResponseGuard
{
    public static GuidanceResponse Apply(GuidanceResponse response) => Apply(response, null);

    public static GuidanceResponse Apply(GuidanceResponse response, GuidanceRequest? request)
    {
        var guarded = new GuidanceResponse
        {
            RequestId = response.RequestId,
            Success = response.Success,
            ErrorMessage = Clean(response.ErrorMessage),
            DirectAnswer = Clean(response.DirectAnswer),
            CurrentScreen = Clean(response.CurrentScreen),
            WhatICanSee = Clean(response.WhatICanSee),
            ManualSteps = response.ManualSteps
                .Select(Clean)
                .Where(value => !string.IsNullOrWhiteSpace(value) && !LooksLikeRawOcrDump(value))
                .Take(5)
                .ToList(),
            IfYouDoNotSeeIt = Clean(response.IfYouDoNotSeeIt),
            VisibilityNote = Clean(response.VisibilityNote),
            ImportantNote = Clean(response.ImportantNote),
            Confidence = response.Confidence,
            ConfidenceDetail = response.ConfidenceDetail,
            VisibleEvidence = response.VisibleEvidence
                .Where(item => !LooksLikeRawOcrDump(item.Detail))
                .Select(item => new GuidanceEvidence
                {
                    Label = Clean(item.Label),
                    Detail = Truncate(Clean(item.Detail), 180)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Detail))
                .Take(3)
                .ToList()
        };

        if (LooksLikeRawOcrDump(guarded.DirectAnswer)) guarded.DirectAnswer = string.Empty;
        if (LooksLikeRawOcrDump(guarded.WhatICanSee)) guarded.WhatICanSee = string.Empty;

        if (request != null
            && VisualQuestionIntentClassifier.Classify(request.UserQuestion) == VisualQuestionIntent.VerifyCurrentScreen)
        {
            return BuildCurrentScreenVerification(guarded, request);
        }

        guarded.FormattedAnswer = GuidanceResponseFormatter.Format(guarded);
        return guarded;
    }

    private static GuidanceResponse BuildCurrentScreenVerification(
        GuidanceResponse guarded,
        GuidanceRequest request)
    {
        var screenKind = request.VisualContext?.MmcConsole ?? MmcConsoleKind.Unknown;
        if (screenKind == MmcConsoleKind.Unknown
            && request.WorkflowContext?.ScreenType is { } workflowScreen
            && workflowScreen != MmcConsoleKind.Unknown)
        {
            screenKind = workflowScreen;
        }

        var screenName = screenKind != MmcConsoleKind.Unknown
            ? MmcConsoleDetector.SnapInDisplayName(screenKind)
            : FirstUseful(
                request.VisualContext?.MmcSnapIn,
                request.Observation.Application,
                request.VisualContext?.ActiveWindowTitle,
                request.Observation.WindowTitle,
                "Unknown");

        var workflow = request.WorkflowContext;
        var hasPreservedGoal = workflow?.Intent != AdminUserIntent.Unknown;
        var isCorrectScreen = hasPreservedGoal
                              && workflow?.IsWrongScreen != true
                              && screenKind != MmcConsoleKind.Unknown;
        var useTurkish = AIGuideLanguage.UseTurkishForResponse(request.UserQuestion);

        string directAnswer;
        if (!hasPreservedGoal)
        {
            directAnswer = useTurkish
                ? $"Şu an açık ekran: {screenName}. Bunun yapmak istediğiniz işlem için doğru ekran olup olmadığını söylemem için hedef işlemi belirtin."
                : $"The current screen is {screenName}. Tell me the task you want to perform so I can verify whether this is the correct screen.";
        }
        else if (isCorrectScreen)
        {
            directAnswer = useTurkish
                ? $"Evet, doğru ekrandasınız. Şu an {screenName} açık."
                : $"Yes, you are on the correct screen. {screenName} is currently open.";
        }
        else
        {
            var expected = FirstUseful(
                request.ToolGuidance?.RecommendedToolName,
                workflow?.AllowedScreens.FirstOrDefault(),
                useTurkish ? "hedef işlem için gereken konsol" : "the console required for the task");
            directAnswer = useTurkish
                ? $"Hayır, şu an {screenName} açık. Hedef işleminiz için {expected} ekranına geçmeniz gerekiyor."
                : $"No. {screenName} is currently open; switch to {expected} for the intended task.";
        }

        var steps = new List<string>();
        var nextStep = request.WorkflowDecision?.NextStep.Instruction;
        if (isCorrectScreen && !string.IsNullOrWhiteSpace(nextStep))
            steps.Add(Clean(nextStep));

        var result = new GuidanceResponse
        {
            RequestId = guarded.RequestId,
            Success = guarded.Success,
            ErrorMessage = guarded.ErrorMessage,
            DirectAnswer = directAnswer,
            CurrentScreen = screenName,
            WhatICanSee = screenName,
            ManualSteps = steps,
            IfYouDoNotSeeIt = string.Empty,
            VisibilityNote = useTurkish
                ? $"Güncel görüntüde {screenName} görünüyor."
                : $"The current capture shows {screenName}.",
            ImportantNote = guarded.ImportantNote,
            Confidence = screenKind == MmcConsoleKind.Unknown ? "Medium" : "High",
            ConfidenceDetail = guarded.ConfidenceDetail,
            VisibleEvidence =
            [
                new GuidanceEvidence
                {
                    Label = useTurkish ? "Güncel ekran" : "Current screen",
                    Detail = screenName
                }
            ]
        };

        result.FormattedAnswer = GuidanceResponseFormatter.Format(result);
        return result;
    }

    internal static bool LooksLikeRawOcrDump(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var dboCount = DboIdentifierRegex().Matches(value).Count;
        var guidCount = GuidRegex().Matches(value).Count;
        var nullCount = Regex.Matches(value, @"\bNULL\b", RegexOptions.IgnoreCase).Count;
        return dboCount >= 2 || guidCount >= 2 || (dboCount >= 1 && nullCount >= 2);
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = AIGuideSecretRedactor.Redact(value).Trim();
        text = PerformedActionRegex().Replace(text, match => match.Value.ToLowerInvariant() switch
        {
            "tıkladım" => "tıklayın",
            "açtım" => "açın",
            "yaptım" => "yapın",
            "çalıştırdım" => "çalıştırın",
            "sildim" => "silmeden önce doğrulayın",
            "clicked" => "click",
            "opened" => "open",
            "executed" => "run manually",
            "deleted" => "verify before deleting",
            _ => match.Value
        });
        return string.Join(Environment.NewLine,
            text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !LooksLikeRawOcrDump(line)));
    }

    private static string FirstUseful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    [GeneratedRegex(@"\bdbo\.[A-Za-z0-9_]+", RegexOptions.IgnoreCase)]
    private static partial Regex DboIdentifierRegex();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\b(tıkladım|açtım|yaptım|çalıştırdım|sildim|clicked|opened|executed|deleted)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PerformedActionRegex();
}
