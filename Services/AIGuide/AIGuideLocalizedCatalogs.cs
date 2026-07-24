using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class AIGuideLocalizedCatalogs
    {
        public static string ObservationServerOffline() =>
            AIGuideLanguage.T(
                "Server appears offline based on available telemetry.",
                "Mevcut telemetriye göre sunucu çevrimdışı görünüyor.");

        public static string ObservationHighCpu() =>
            AIGuideLanguage.T("High CPU usage detected.", "Yüksek CPU kullanımı algılandı.");

        public static string ObservationHighRam() =>
            AIGuideLanguage.T("High memory usage detected.", "Yüksek bellek kullanımı algılandı.");

        public static string ObservationLowDisk() =>
            AIGuideLanguage.T("Disk space appears critically low.", "Disk alanı kritik düzeyde düşük görünüyor.");

        public static string ObservationDomainController() =>
            AIGuideLanguage.T(
                "A Domain Controller has been identified.",
                "Bir Domain Controller tanımlandı.");

        public static string ObservationUserSession(string? username) =>
            AIGuideLanguage.T(
                $"Administrator or user session context is visible: {username}.",
                $"Yönetici veya kullanıcı oturum bağlamı görünür: {username}.");

        public static string ObservationAccountDisabled() =>
            AIGuideLanguage.T("The selected account appears disabled.", "Seçili hesap devre dışı görünüyor.");

        public static string ObservationAccountLocked() =>
            AIGuideLanguage.T("The selected account appears locked.", "Seçili hesap kilitli görünüyor.");

        public static string ObservationVisibleErrors() =>
            AIGuideLanguage.T(
                "Event Viewer or the current screen displays visible errors.",
                "Event Viewer veya geçerli ekranda görünür hatalar var.");

        public static string ObservationCriticalEvents() =>
            AIGuideLanguage.T(
                "Event Viewer currently displays critical events.",
                "Event Viewer şu anda kritik olaylar gösteriyor.");

        public static string ObservationIisStopped() =>
            AIGuideLanguage.T("An IIS website appears stopped.", "Bir IIS web sitesi durdurulmuş görünüyor.");

        public static string ObservationVmPoweredOff() =>
            AIGuideLanguage.T("A virtual machine appears powered off.", "Bir sanal makine kapalı görünüyor.");

        public static string ObservationDnsManagerOpen() =>
            AIGuideLanguage.T("DNS Manager is open.", "DNS Manager açık.");

        public static string ObservationAdConsoleOpen() =>
            AIGuideLanguage.T(
                "Active Directory Users and Computers is open.",
                "Active Directory Users and Computers (Active Directory Kullanıcıları ve Bilgisayarları) açık.");

        public static string ObservationWorkflowInProgress(string? name) =>
            AIGuideLanguage.T(
                $"Workflow in progress: {name}.",
                $"Devam eden iş akışı: {name}.");

        public static string ObservationNoTelemetry() =>
            AIGuideLanguage.T(
                "No live telemetry received for this server.",
                "Bu sunucu için canlı telemetri alınmadı.");

        public static string ObservationLowVisionConfidence() =>
            AIGuideLanguage.T(
                "Vision confidence is low for this screen.",
                "Bu ekran için görüntü güveni düşük.");

        public static string ObservationCaptureLimited() =>
            AIGuideLanguage.T(
                "The window may be partially visible or affected by capture limitations.",
                "Pencere kısmen görünür olabilir veya yakalama sınırlamalarından etkilenmiş olabilir.");

        public static string WarningServerOffline() =>
            AIGuideLanguage.T("Server appears offline.", "Sunucu çevrimdışı görünüyor.");

        public static string WarningHighCpu() =>
            AIGuideLanguage.T("CPU usage is currently above 95%.", "CPU kullanımı şu anda %95'in üzerinde.");

        public static string WarningLowDisk() =>
            AIGuideLanguage.T("Disk space below 10%.", "Disk alanı %10'un altında.");

        public static string WarningNoTelemetry() =>
            AIGuideLanguage.T("No telemetry received.", "Telemetri alınmadı.");

        public static string WarningLowVisionConfidence() =>
            AIGuideLanguage.T("Vision confidence is low.", "Görüntü güveni düşük.");

        public static string WarningCaptureLimited() =>
            AIGuideLanguage.T(
                "Window partially visible or capture limited.",
                "Pencere kısmen görünür veya yakalama sınırlı.");

        public static string WarningApplicationChanged() =>
            AIGuideLanguage.T(
                "Application changed — previous workflow may no longer apply.",
                "Uygulama değişti — önceki iş akışı artık geçerli olmayabilir.");

        public static string CurrentWindow(string title) =>
            AIGuideLanguage.T($"Current window: {title}", $"Geçerli pencere: {title}");

        public static string SelectedObjects(string objects) =>
            AIGuideLanguage.T($"Selected objects: {objects}", $"Seçili nesneler: {objects}");

        public static string Dialogs(string dialogs) =>
            AIGuideLanguage.T($"Dialogs: {dialogs}", $"Diyaloglar: {dialogs}");

        public static string VisibleErrorsCount(int count) =>
            AIGuideLanguage.T($"Visible errors: {count}", $"Görünen hatalar: {count}");

        public static string VisibleWarningsCount(int count) =>
            AIGuideLanguage.T($"Visible warnings: {count}", $"Görünen uyarılar: {count}");

        public static string RuntimeContextDetail(string server, string role, string connection) =>
            AIGuideLanguage.T(
                $"Server={server}, Role={role}, Connection={connection}",
                $"Sunucu={server}, Rol={role}, Bağlantı={connection}");

        public static string ConfidenceDetail(string vision, string context, string overall) =>
            AIGuideLanguage.T(
                $"Vision={vision}, Context={context}, Overall={overall}",
                $"Görüntü={vision}, Bağlam={context}, Genel={overall}");

        public static string TimelineScreenAnalyzed() =>
            AIGuideLanguage.T("Screen analyzed.", "Ekran analiz edildi.");

        public static string TimelineWorkflowDetected(string? name) =>
            AIGuideLanguage.T(
                $"Workflow detected: {name ?? "Unknown"}.",
                $"İş akışı algılandı: {name ?? "Bilinmiyor"}.");

        public static string TimelineApplicationChanged(string prev, string curr) =>
            AIGuideLanguage.T(
                $"Application changed: {prev} → {curr}.",
                $"Uygulama değişti: {prev} → {curr}.");

        public static string TimelineRoleDetected(string role) =>
            AIGuideLanguage.T($"Role detected: {role}.", $"Rol algılandı: {role}.");

        public static string TimelineSummaryGenerated() =>
            AIGuideLanguage.T("Smart summary generated.", "Akıllı özet oluşturuldu.");

        public static string TimelineDialogOpened() =>
            AIGuideLanguage.T("Dialog opened.", "Diyalog açıldı.");

        public static string TimelineDialogClosed() =>
            AIGuideLanguage.T("Dialog closed.", "Diyalog kapatıldı.");

        public static string TimelineSelectionChanged() =>
            AIGuideLanguage.T("Selection changed.", "Seçim değişti.");

        public static string TimelineWindowChanged() =>
            AIGuideLanguage.T("Window title changed.", "Pencere başlığı değişti.");

        public static string TimelineUserQuestion() =>
            AIGuideLanguage.T("User question submitted.", "Kullanıcı sorusu gönderildi.");

        public static string TimelineAnswerGenerated() =>
            AIGuideLanguage.T("Answer generated.", "Yanıt oluşturuldu.");

        public static string ScreenStatusLowConfidence() =>
            AIGuideLanguage.T(
                "The screen could not be clearly identified. Bring the target window into focus and press Ctrl+M again.",
                "Ekran net olarak tanımlanamadı. Hedef pencereyi odaklayın ve Ctrl+M ile tekrar deneyin.");

        public static string ScreenStatusSuccess() =>
            AIGuideLanguage.T("Screen analyzed successfully.", "Ekran başarıyla analiz edildi.");

        public static string TeachAbout(string topic) =>
            AIGuideLanguage.T($"Teach me about {topic}.", $"{topic} hakkında bilgi ver.");

        public static string QuestionResetPassword() =>
            AIGuideLanguage.T(
                "How do I reset this user's password manually from what is visible?",
                "Görünen ekrandan bu kullanıcının parolasını manuel olarak nasıl sıfırlarım?");

        public static string QuestionAccountDisabled() =>
            AIGuideLanguage.T("Why is this account disabled?", "Bu hesap neden devre dışı?");

        public static string QuestionAccountLocked() =>
            AIGuideLanguage.T("Why is this account locked?", "Bu hesap neden kilitli?");

        public static string QuestionExplainEventId() =>
            AIGuideLanguage.T(
                "Explain the current Event ID from what is visible.",
                "Görünen Event ID'yi açıkla.");

        public static string QuestionIisStopped() =>
            AIGuideLanguage.T(
                "Why is this IIS website stopped?",
                "Bu IIS web sitesi neden durdurulmuş?");

        public static string QuestionDnsRecord() =>
            AIGuideLanguage.T(
                "How does this DNS record work based on what is visible?",
                "Görünen ekrana göre bu DNS kaydı nasıl çalışır?");

        public static string QuestionRegistryValue() =>
            AIGuideLanguage.T(
                "What does this registry value mean?",
                "Bu kayıt defteri değeri ne anlama geliyor?");

        public static string QuestionVmState() =>
            AIGuideLanguage.T(
                "What is the current state of this virtual machine?",
                "Bu sanal makinenin geçerli durumu nedir?");

        public static string QuestionServiceStopped() =>
            AIGuideLanguage.T(
                "Why is this service stopped?",
                "Bu hizmet neden durdurulmuş?");

        public static string QuestionWorkflowStep(string workflow) =>
            AIGuideLanguage.T(
                $"What step am I on in the {workflow} workflow?",
                $"{workflow} iş akışında hangi adımdayım?");

        public static string QuestionWhatCanYouSee() =>
            AIGuideLanguage.T(
                "What can you see on the current screen?",
                "Geçerli ekranda neler görüyorsun?");

        public static string QuestionManualSteps() =>
            AIGuideLanguage.T(
                "What manual steps are available from what is visible?",
                "Görünen ekrandan hangi manuel adımlar mümkün?");

        public static IReadOnlyList<SuggestedTopic> ResolveSmartTopics(string? application, string? windowTitle, bool lowConfidence)
        {
            if (lowConfidence)
            {
                return
                [
                    new SuggestedTopic
                    {
                        Label = AIGuideLanguage.T("Focus Window", "Pencereyi Odakla"),
                        Question = AIGuideLanguage.T(
                            "The screen is unclear — what should I bring into focus?",
                            "Ekran net değil — neyi odaklamalıyım?")
                    },
                    new SuggestedTopic
                    {
                        Label = AIGuideLanguage.T("Explain Screen", "Ekranı Açıkla"),
                        Question = QuestionWhatCanYouSee()
                    },
                    new SuggestedTopic
                    {
                        Label = AIGuideLanguage.T("What Can I Do?", "Ne Yapabilirim?"),
                        Question = AIGuideLanguage.T(
                            "What can I ask about once the screen is clearer?",
                            "Ekran netleşince ne sorabilirim?")
                    }
                ];
            }

            var haystack = $"{application} {windowTitle}".ToLowerInvariant();
            foreach (var (patterns, topics) in GetSmartTopicCatalog())
            {
                if (patterns.Any(p => haystack.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    return topics.Take(5).ToList();
            }

            return
            [
                new SuggestedTopic
                {
                    Label = AIGuideLanguage.T("Explain Screen", "Ekranı Açıkla"),
                    Question = QuestionWhatCanYouSee()
                },
                new SuggestedTopic
                {
                    Label = AIGuideLanguage.T("Manual Steps", "Manuel Adımlar"),
                    Question = QuestionManualSteps()
                },
                new SuggestedTopic
                {
                    Label = AIGuideLanguage.T("What Can I Ask?", "Ne Sorabilirim?"),
                    Question = AIGuideLanguage.T(
                        "What can I ask about based on this screen?",
                        "Bu ekrana göre ne sorabilirim?")
                }
            ];
        }

        private static IEnumerable<(string[] Patterns, SuggestedTopic[] Topics)> GetSmartTopicCatalog()
        {
            if (AIGuideLanguage.IsTurkish)
            {
                yield return (
                    ["active directory users and computers", "dsa.msc", "users and computers"],
                    [
                        new() { Label = "Parola Sıfırlama", Question = "Görünen ekrandan bir kullanıcı parolasını manuel olarak nasıl sıfırlarım?" },
                        new() { Label = "Hesabın Kilidini Aç", Question = "Görünen ekrandan kilitli bir kullanıcı hesabını nasıl açarım?" },
                        new() { Label = "Kullanıcı Oluştur", Question = "Görünen seçeneklerle yeni bir kullanıcı nasıl oluşturulur?" },
                        new() { Label = "Gruplar", Question = "Bu ekrandan grup üyeliğini nasıl yönetirim?" }
                    ]);
                yield return (
                    ["dns manager", "dnsmgmt.msc", "dns management"],
                    [
                        new() { Label = "A Kaydı", Question = "Görünen ekrandan bir A kaydı nasıl oluşturulur?" },
                        new() { Label = "DNS Bölgesi", Question = "Bu DNS bölgesini görünen ekrandan nasıl kontrol ederim?" },
                        new() { Label = "Ters Arama", Question = "Bu ekranda ters arama bölgesiyle nasıl çalışırım?" },
                        new() { Label = "Ad Çözümleme", Question = "Görünen bilgilerle ad çözümlemesini nasıl sorun gideririm?" }
                    ]);
                yield return (
                    ["services", "services.msc"],
                    [
                        new() { Label = "Hizmet Durumu", Question = "Görünen hizmet durumunu nasıl kontrol ederim?" },
                        new() { Label = "Hizmeti Yeniden Başlat", Question = "Bu ekrandan bir hizmeti manuel olarak nasıl yeniden başlatırım?" },
                        new() { Label = "Başlangıç Türü", Question = "Görünen ekrandan başlangıç türünü nasıl belirlerim?" },
                        new() { Label = "Durdurulmuş Hizmet", Question = "Durdurulmuş bir hizmeti bu ekrandan nasıl sorun gideririm?" }
                    ]);
                yield break;
            }

            yield return (
                ["active directory users and computers", "dsa.msc", "users and computers"],
                [
                    new() { Label = "Reset Password", Question = "How do I reset a user password manually on this screen?" },
                    new() { Label = "Unlock Account", Question = "How do I unlock a locked user account from what is visible?" },
                    new() { Label = "Create User", Question = "How do I create a new user using the visible options?" },
                    new() { Label = "Groups", Question = "How do I manage group membership from this screen?" }
                ]);
            yield return (
                ["dns manager", "dnsmgmt.msc", "dns management"],
                [
                    new() { Label = "A Record", Question = "How do I create an A record from what is visible?" },
                    new() { Label = "DNS Zone", Question = "How do I check this DNS zone from the visible screen?" },
                    new() { Label = "Reverse Lookup", Question = "How do I work with a reverse lookup zone on this screen?" },
                    new() { Label = "Name Resolution", Question = "How do I troubleshoot name resolution using what is visible?" }
                ]);
            yield return (
                ["services", "services.msc"],
                [
                    new() { Label = "Service Status", Question = "How do I check the visible service status?" },
                    new() { Label = "Restart Service", Question = "How do I restart a service manually from this screen?" },
                    new() { Label = "Startup Type", Question = "How do I identify the startup type from what is visible?" },
                    new() { Label = "Stopped Service", Question = "How do I troubleshoot a stopped service from this screen?" }
                ]);
        }
    }
}
