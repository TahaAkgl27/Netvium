namespace Netvium.Services.AIGuide;

internal enum VisualQuestionIntent
{
    General,
    LocateControl,
    ExplainError,
    ChooseVisibleAction,
    VerifyCurrentScreen
}

internal static class VisualQuestionIntentClassifier
{
    public static VisualQuestionIntent Classify(string? question)
    {
        var text = QuestionTextNormalizer.Normalize(question);
        if (text.Length == 0) return VisualQuestionIntent.General;

        if (ContainsAny(text,
                "dogru yer", "dogru ekran", "dogru yerde", "yerde miyim", "yerdemiyim",
                "burasi dogru", "hangi ekrandayim", "su an hangi ekran", "neredeyim",
                "correct screen", "right screen", "right place", "where am i"))
            return VisualQuestionIntent.VerifyCurrentScreen;

        if (ContainsAny(text, "hata", "error", "uyari", "warning", "failed", "basarisiz", "ne demek"))
            return VisualQuestionIntent.ExplainError;

        if (ContainsAny(text, "neye bas", "hangisine bas", "hangi buton", "which button", "what should i click", "confirm", "onay"))
            return VisualQuestionIntent.ChooseVisibleAction;

        if (ContainsAny(text,
                "nerden", "nereden", "nerede", "nereye", "nasil ac", "nasil yazar", "nasil gir",
                "where", "how do i open", "how can i open", "where do i", "where can i", "what do i click"))
            return VisualQuestionIntent.LocateControl;

        return VisualQuestionIntent.General;
    }

    public static string PromptDirective(VisualQuestionIntent intent) => intent switch
    {
        VisualQuestionIntent.VerifyCurrentScreen =>
            "This is a CURRENT-SCREEN verification question. Identify the application from the attached image first. Never repeat an application name from an earlier assistant answer. Compare the current application with the preserved user goal and state both explicitly.",
        VisualQuestionIntent.LocateControl =>
            "Lead with the control label and approximate location. If the control is cropped or not visible, say that and give one standard application path as an inferred alternative.",
        VisualQuestionIntent.ExplainError =>
            "Explain the visible error message first, then give a likely cause and one safe verification step. Quote only the minimum useful error fragment.",
        VisualQuestionIntent.ChooseVisibleAction =>
            "Explain which visible action matches the user's goal only when button labels are readable. If they are unreadable, do not invent labels; ask for a clearer view or explain the decision criteria.",
        _ =>
            "Answer the user's current question directly using the screenshot as primary evidence."
    };

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
