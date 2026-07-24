using System.Globalization;
using System.Text;
using Netvium.Helpers;
using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// AI Guide UI strings and AI response language rules tied to application settings.
    /// </summary>
    public static class AIGuideLanguage
    {
        public static bool IsTurkish => string.Equals(
            SettingsRuntime.Current.Language,
            "tr",
            StringComparison.OrdinalIgnoreCase);

        public static string T(string english, string turkish) =>
            IsTurkish ? turkish : english;

        // Section headers
        public static string CurrentScreen => T("Current Screen", "Geçerli Ekran");
        public static string ThingsNoticed => T("Things I noticed", "Gördüklerim");
        public static string CurrentWorkflow => T("Current Workflow", "İzlenen İş Akışı");
        public static string SuggestedQuestions => T("Suggested Questions", "Önerilen Sorular");
        public static string LearnAboutScreen => T("Learn About This Screen", "Bu Ekran Hakkında");
        public static string Warnings => T("Warnings", "Uyarılar");
        public static string Recommendations => T("Recommendations", "Öneriler");
        public static string Errors => T("Errors", "Hatalar");
        public static string Diagnostics => T("Diagnostics", "Tanılama");
        public static string Evidence => T("Evidence", "Kanıt");
        public static string WorkflowHistory => T("Workflow history", "İş akışı geçmişi");
        public static string CaptureDiagnostics => T("Capture Diagnostics", "Yakalama Tanılaması");
        public static string TechnicalSummary => T("Technical summary", "Teknik özet");
        public static string ContextAwareness => T("Context awareness", "Bağlam farkındalığı");

        // Status badges / system messages
        public static string ScreenReady => T("Screen Ready", "Ekran Hazır");
        public static string NeedsFocus => T("Needs Focus", "Odak Gerekli");
        public static string Failed => T("Failed", "Başarısız");
        public static string Ready => T("Ready", "Hazır");
        public static string Waiting => T("Waiting...", "Bekleniyor...");
        public static string Capturing => T("Capturing...", "Yakalanıyor...");
        public static string AnalyzingScreen => T("Analyzing current screen...", "Geçerli ekran analiz ediliyor...");
        public static string SummarizingScreen => T("Summarizing screen...", "Ekran özetleniyor...");
        public static string AnalysisComplete => T("Analysis Complete", "Analiz Tamamlandı");
        public static string Guiding => T("Guiding...", "Yanıtlanıyor...");
        public static string Cancelled => T("Cancelled", "İptal edildi");
        public static string GeneratingGuidance => T("Generating guidance...", "Yanıt oluşturuluyor...");

        public static string OpenHint => T(
            "Press Ctrl+M to analyze the current screen.",
            "Geçerli ekranı analiz etmek için Ctrl+M tuşlarına basın.");

        public static string RetryHint => T(
            "Press Ctrl+M to retry.",
            "Yeniden denemek için Ctrl+M tuşlarına basın.");

        public static string InputHintReady => T(
            "Ask a question about the visible screen. Enter to send, Shift+Enter for newline.",
            "Görünen ekran hakkında soru sorun. Göndermek için Enter, yeni satır için Shift+Enter.");

        public static string InputHintAnalysisRequired => T(
            "Complete screen analysis before asking a question.",
            "Soru sormadan önce ekran analizini tamamlayın.");

        public static string Reminder => T(
            "AI Guide explains the visible screen only. It never controls your computer.",
            "AI Guide yalnızca görünen ekranı açıklar. Bilgisayarınızı asla kontrol etmez.");

        public static string ScreenAnalyzedFallback => T(
            "Screen analyzed — review observations below.",
            "Ekran analiz edildi — aşağıdaki gözlemleri inceleyin.");

        public static string CopyCaptureDiagnostics => T("Copy Capture Diagnostics", "Yakalama Tanılamasını Kopyala");
        public static string TeachTopicTooltip => T("Teach me about this topic", "Bu konu hakkında bilgi ver");
        public static string SuggestedQuestionTooltip => T("Fill input with this question", "Giriş alanına bu soruyu yaz");

        public static string RdpNotReadyMessage => T(
            "RDP screen is not ready for analysis yet. Bring the session into view and try again.",
            "RDP ekranı analiz için henüz hazır değil. Oturumu görünür hale getirip tekrar deneyin.");

        public static string RdpConnectingMessage => T(
            "RDP connection is still in progress. Wait until the session is connected.",
            "RDP bağlantısı devam ediyor. Oturum bağlanana kadar bekleyin.");

        public static string RdpLoadingMessage => T(
            "RDP screen is still loading. Try again in a few seconds.",
            "RDP ekranı hala yükleniyor. Birkaç saniye sonra tekrar deneyin.");

        public static string AiGuidePausedForConnection => T(
            "AI Guide paused while RDP is connecting...",
            "RDP bağlanırken AI Guide duraklatıldı...");

        public static string CannotConfirmFromScreen => T(
            "I cannot confirm that from the current screen.",
            "Bunu geçerli ekrandan doğrulayamıyorum.");

        public static string EnterQuestionHint => T(
            "Enter a question about the current screen.",
            "Geçerli ekran hakkında bir soru girin.");

        public static string GuidanceFailed => T("Guidance failed.", "Yanıt oluşturulamadı.");

        public static string FreshCaptureFailedNoFallback => T(
            "A new screenshot could not be captured. I will not answer based on the previous screen.",
            "Yeni ekran görüntüsü alınamadı, önceki ekrana göre cevap vermiyorum.");

        public static string RdpReadyWithWarning => T(
            "RDP screen is ready with a warning — capture quality may be limited.",
            "RDP ekranı hazır (uyarı: yakalama kalitesi sınırlı olabilir).");

        public static string CapturingForQuestion => T(
            "Capturing current screen before answering...",
            "Yanıtlamadan önce geçerli ekran yakalanıyor...");

        public static string NoVisibleCaptureTarget => T(
            "No visible capture target.",
            "Görünür yakalama hedefi yok.");

        public static string ManualControlNote => T(
            "I will not perform the action. You stay in control.",
            "Bu işlemi ben yapmayacağım. Kontrol sizde kalır.");

        public static string AnalysisCancelledDetail => T("Analysis cancelled.", "Analiz iptal edildi.");

        public static string LocalizeConfidenceLabel(string? label) =>
            label switch
            {
                "High" => T("High", "Yüksek"),
                "Medium" => T("Medium", "Orta"),
                "Low" => T("Low", "Düşük"),
                _ => label ?? T("Medium", "Orta")
            };

        public static string LocalizeConfidenceLabel(double score) =>
            LocalizeConfidenceLabel(ContextConfidence.ToLabel(score));

        public static string FormatWorkflowProgress(int current, int total) =>
            total > 0
                ? T($"{current} of {total}", $"{current} / {total}")
                : "—";

        public static string FormatPercentComplete(double percent) =>
            T($"{percent:0}% complete", $"%{percent:0} tamamlandı");

        public static string FormatServerLabel(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : T($"Server: {name}", $"Sunucu: {name}");

        public static string FormatRoleLabel(string? role) =>
            string.IsNullOrWhiteSpace(role)
                ? string.Empty
                : T($"Role: {role}", $"Rol: {role}");

        public static string FormatApplicationLabel(string? app) =>
            string.IsNullOrWhiteSpace(app)
                ? string.Empty
                : T($"Application: {app}", $"Uygulama: {app}");

        public static string FormatWorkflowLabel(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? T("Workflow: —", "İş akışı: —")
                : T($"Workflow: {name}", $"İş akışı: {name}");

        public static string FormatConfidenceDetail(string label, double score) =>
            T($"Confidence: {label} ({score:P0})", $"Güven: {label} ({score:P0})");

        public static string FormatCurrentStep(string progress, string stepName) =>
            T($"Current step: {progress} — {stepName}", $"Geçerli adım: {progress} — {stepName}");

        public static string FormatWorkflowConfidence(string label, double score) =>
            T($"Workflow confidence: {label} ({score:P0})", $"İş akışı güveni: {label} ({score:P0})");

        public static string FormatApplicationChanged(string prev, string curr, string continued) =>
            T(
                $"Application changed ({prev} → {curr}). Workflow continued: {continued}.",
                $"Uygulama değişti ({prev} → {curr}). İş akışı devam etti: {continued}.");

        public static string FormatContinuedYesNo(bool continued) =>
            continued ? T("yes", "evet") : T("no", "hayır");

        public static string FormatStageLabel(AIGuideFailureStage stage) =>
            T($"Stage: {stage}", $"Aşama: {stage}");

        public static string FormatAnalysisId(long id) =>
            T($"Analysis Id: {id}", $"Analiz Kimliği: {id}");

        public static string FormatDetail(string detail) =>
            string.IsNullOrWhiteSpace(detail) ? string.Empty : T($"Detail: {detail}", $"Ayrıntı: {detail}");

        public static bool IsTurkishText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return IsTurkish;

            if (text.Any(c => "çğıöşüÇĞİÖŞÜ".Contains(c)))
                return true;

            var lower = $" {text.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} ";
            ReadOnlySpan<string> trWords =
            [
                " nasıl ", " nereden ", " neden ", " hangi ", " kullanıcı ", " şifre ",
                " oluştur ", " değiştir ", " sıfırla ", " ekle ", " kaldır ", " için ",
                " bir ", " bu ", " oturum ", " sunucu ", " grup ", " hesap "
            ];
            ReadOnlySpan<string> enWords =
            [
                " how ", " where ", " why ", " what ", " user ", " password ",
                " create ", " change ", " reset ", " add ", " remove ", " from ",
                " the ", " this ", " server ", " account ", " group "
            ];

            var trHits = trWords.ToArray().Count(w => lower.Contains(w, StringComparison.Ordinal));
            var enHits = enWords.ToArray().Count(w => lower.Contains(w, StringComparison.Ordinal));

            if (trHits > enHits)
                return true;
            if (enHits > trHits)
                return false;

            return IsTurkish;
        }

        public static bool UseTurkishForResponse(string? userQuestion = null) =>
            userQuestion != null ? IsTurkishText(userQuestion) : IsTurkish;

        public static string BuildResponseLanguageRule(string? userQuestion = null)
        {
            var useTurkish = UseTurkishForResponse(userQuestion);
            var sb = new StringBuilder();
            sb.AppendLine(useTurkish
                ? "LANGUAGE (mandatory): Write ALL user-visible text entirely in Turkish. Never mix Turkish and English in the same response."
                : "LANGUAGE (mandatory): Write ALL user-visible text entirely in English. Never mix languages in the same response.");
            sb.AppendLine("Keep official technical/product terms in English when standard: Active Directory, Domain Controller, RDP, DNS, DHCP, PowerShell, Group Policy, OU, Hyper-V, IIS.");
            sb.AppendLine(useTurkish
                ? "For Windows admin tools, prefer official Microsoft names with Turkish in parentheses when helpful, e.g. \"Active Directory Users and Computers (Active Directory Kullanıcıları ve Bilgisayarları)\"."
                : "Use official Microsoft tool names in English.");
            sb.AppendLine("Never translate product names, server names, hostnames, or Netvium.");
            if (userQuestion != null)
            {
                sb.AppendLine(useTurkish
                    ? "The user question is in Turkish — every field in your JSON response must be Turkish."
                    : "The user question is in English — every field in your JSON response must be English.");
            }
            else
            {
                sb.AppendLine(useTurkish
                    ? "The application UI language is Turkish — every field in your JSON response must be Turkish."
                    : "The application UI language is English — every field in your JSON response must be English.");
            }

            return sb.ToString().TrimEnd();
        }

        public static string LocalizeWorkflowName(string? workflowId, string? englishName)
        {
            var name = englishName ?? string.Empty;
            if (!IsTurkish || string.IsNullOrWhiteSpace(workflowId))
                return name;

            return AIGuideWorkflowLocalization.LocalizeName(workflowId, name);
        }

        public static string LocalizeWorkflowStepName(string? workflowId, string? stepId, string englishName) =>
            AIGuideWorkflowLocalization.LocalizeStepName(workflowId, stepId, englishName);

        public static string LocalizeWorkflowStatusMessage(string? englishMessage) =>
            englishMessage switch
            {
                "Workflow appears complete based on visible evidence." =>
                    T("Workflow appears complete based on visible evidence.",
                        "Görünen kanıtlara göre iş akışı tamamlanmış görünüyor."),
                "Workflow in progress." =>
                    T("Workflow in progress.", "İş akışı devam ediyor."),
                "Workflow detected." =>
                    T("Workflow detected.", "İş akışı algılandı."),
                "Workflow status updated." =>
                    T("Workflow status updated.", "İş akışı durumu güncellendi."),
                "The screen could not be clearly identified for workflow detection." =>
                    T("The screen could not be clearly identified for workflow detection.",
                        "İş akışı algılaması için ekran net olarak tanımlanamadı."),
                "No supported workflow matched the visible evidence." =>
                    T("No supported workflow matched the visible evidence.",
                        "Görünen kanıtlarla eşleşen desteklenen bir iş akışı bulunamadı."),
                "Insufficient evidence to identify a workflow." =>
                    T("Insufficient evidence to identify a workflow.",
                        "Bir iş akışını tanımlamak için yeterli kanıt yok."),
                _ => englishMessage ?? string.Empty
            };

        public static string LocalizeEvidenceLabel(string? label) =>
            label switch
            {
                "Control" => T("Control", "Denetim"),
                "Text" => T("Text", "Metin"),
                "Error" => T("Error", "Hata"),
                "Warning" => T("Warning", "Uyarı"),
                "Runtime context" => T("Runtime context", "Çalışma zamanı bağlamı"),
                "Confidence" => T("Confidence", "Güven"),
                "Application" => T("Application", "Uygulama"),
                "Server" => T("Server", "Sunucu"),
                "Role" => T("Role", "Rol"),
                "Visible control" => T("Visible control", "Görünen denetim"),
                _ => label ?? string.Empty
            };
    }
}
