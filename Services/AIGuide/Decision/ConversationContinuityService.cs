using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;

namespace Netvium.Services.AIGuide.Decision;

public sealed class ConversationContinuityResult
{
    public bool IsContinuation { get; init; }
    public bool IsFolderOpenedAck { get; init; }
    public bool IsScreenVerification { get; init; }
    public bool ShouldAdvanceStep { get; init; }
    public string? AnchorQuestion { get; init; }
    public AdminUserIntent? IntentOverride { get; init; }
    public WorkflowTarget? TargetOverride { get; init; }
    public WorkflowSessionState? ActiveSession { get; init; }
}

/// <summary>
/// Restores only the user's task goal for follow-up turns. Screen facts always come from the
/// current capture and are never restored from conversation history.
/// </summary>
public sealed class ConversationContinuityService
{
    private static readonly string[] ContinuationPhrases =
    [
        "devam", "devam et", "devam edelim", "continue", "sonraki adim", "next step",
        "simdi ne yapacagim", "ne yapmaliyim", "ne yapacagim"
    ];

    private static readonly string[] FolderOpenedPhrases =
    [
        "klasoru actim", "klasor acildi", "actim", "hazir", "gittim", "ulastim",
        "tamam klasor"
    ];

    private static readonly string[] ScreenVerificationPhrases =
    [
        "dogru yer", "dogru ekran", "dogru yerde", "yerde miyim", "yerdemiyim",
        "burasi dogru", "hangi ekrandayim", "su an hangi ekran", "neredeyim",
        "correct screen", "right screen", "right place", "where am i"
    ];

    private static readonly string[] AnchorKeywords =
    [
        "izin", "permission", "read", "write", "ntfs", "payla", "share",
        "sifre", "password", "unlock", "kilidi", "kilit",
        "rezervasyon", "reservation", "dhcp", "dns", "kayit",
        "kullanici", "user", "grup", "group", "ip"
    ];

    public static bool LooksLikeContinuation(string userQuestion)
    {
        var question = Normalize(userQuestion);
        return IsContinuationPhrase(question)
               || IsFolderOpenedAck(question)
               || IsScreenVerificationPhrase(question)
               || question == "tamam";
    }

    public ConversationContinuityResult Resolve(WorkflowDecisionInput input)
    {
        var question = Normalize(input.UserQuestion);
        var isFolderAck = IsFolderOpenedAck(question);
        var isScreenVerification = IsScreenVerificationPhrase(question);
        var isContinuation = IsContinuationPhrase(question)
                             || isFolderAck
                             || isScreenVerification
                             || question == "tamam";

        if (!isContinuation)
            return new ConversationContinuityResult();

        var session = input.SessionState;
        var anchor = session?.AnchorQuestion ?? FindAnchorQuestion(input.PriorTurns);
        if (string.IsNullOrWhiteSpace(anchor) && session == null)
            return new ConversationContinuityResult
            {
                IsScreenVerification = isScreenVerification
            };

        var shouldAdvance = IsContinuationPhrase(question)
                            && !isFolderAck
                            && !isScreenVerification;
        if (question == "tamam" && session != null)
            shouldAdvance = false;

        return new ConversationContinuityResult
        {
            IsContinuation = true,
            IsFolderOpenedAck = isFolderAck,
            IsScreenVerification = isScreenVerification,
            ShouldAdvanceStep = shouldAdvance,
            AnchorQuestion = anchor,
            IntentOverride = session?.Intent,
            TargetOverride = session?.Target,
            ActiveSession = session
        };
    }

    public static WorkflowTarget MergeTargets(WorkflowTarget extracted, WorkflowTarget? session)
    {
        if (session == null)
            return extracted;

        return new WorkflowTarget
        {
            Path = Coalesce(extracted.Path, session.Path),
            FolderName = Coalesce(extracted.FolderName, session.FolderName),
            UserName = Coalesce(extracted.UserName, session.UserName),
            PermissionLevel = Coalesce(extracted.PermissionLevel, session.PermissionLevel),
            Operation = Coalesce(extracted.Operation, session.Operation),
            IpAddress = extracted.IpAddress ?? session.IpAddress,
            MacAddress = Coalesce(extracted.MacAddress, session.MacAddress),
            RecordName = Coalesce(extracted.RecordName, session.RecordName),
            SiteName = Coalesce(extracted.SiteName, session.SiteName),
            HasUserProvidedPath = extracted.HasUserProvidedPath || session.HasUserProvidedPath,
            TargetFromSelection = extracted.TargetFromSelection || session.TargetFromSelection
        };
    }

    private static string? FindAnchorQuestion(IReadOnlyList<ConversationTurn> priorTurns)
    {
        for (var index = priorTurns.Count - 1; index >= 0; index--)
        {
            var turn = priorTurns[index];
            if (!string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = turn.Content ?? string.Empty;
            var normalized = Normalize(content);
            if (string.IsNullOrWhiteSpace(normalized)
                || IsContinuationPhrase(normalized)
                || IsFolderOpenedAck(normalized)
                || IsScreenVerificationPhrase(normalized))
                continue;

            if (HasAnchorKeywords(normalized))
                return content;
        }

        return null;
    }

    private static bool HasAnchorKeywords(string question) =>
        AnchorKeywords.Any(keyword => question.Contains(keyword, StringComparison.Ordinal));

    private static bool IsContinuationPhrase(string question) =>
        ContinuationPhrases.Any(phrase => question.Contains(phrase, StringComparison.Ordinal));

    private static bool IsFolderOpenedAck(string question) =>
        FolderOpenedPhrases.Any(phrase => question.Contains(phrase, StringComparison.Ordinal));

    private static bool IsScreenVerificationPhrase(string question) =>
        ScreenVerificationPhrases.Any(phrase => question.Contains(phrase, StringComparison.Ordinal));

    private static string Coalesce(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static string Normalize(string text) => QuestionTextNormalizer.Normalize(text);
}
