using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    /// <summary>
    /// Expert Windows IT assistant analysis: intent, tool compatibility, modal priority, expert modules.
    /// </summary>
    internal static class AdminToolExpertEngine
    {
        private static readonly string[] ConnectionPatterns =
        [
            "netvium", "sunucu durumu", "server status", "uptime", "cpu", "ram", "telemetry"
        ];

        private static readonly string[] RdpPatterns =
        [
            "rdp", "remote desktop", "uzak masaüstü", "uzak masaustu", "mstsc", "bağlanamıyor", "baglanamiyor"
        ];

        public static bool QuestionMentionsConnection(string question)
        {
            var q = Normalize(question);
            return ConnectionPatterns.Any(p => q.Contains(p, StringComparison.Ordinal));
        }

        public static AdminToolGuidanceHint Analyze(ScreenVisualContext? visualContext, string userQuestion)
        {
            var visibleTool = ResolveVisibleTool(visualContext);
            var visibleName = ResolveVisibleName(visualContext, visibleTool);

            var modalPriority = visualContext?.ModalPriority == true || visualContext?.HasModalDialog == true;
            var modalSecurity = visibleTool == MmcConsoleKind.EndpointSecurity
                || (modalPriority
                    && !string.IsNullOrWhiteSpace(visualContext?.ModalBodyText)
                    && ModalDialogDetector.Detect(
                        visualContext!.VisibleDialogTitle,
                        visualContext.ModalBodyText,
                        visualContext.OcrWindowTitle).IsSecurityAlert);

            if (modalPriority && visibleTool != MmcConsoleKind.EndpointSecurity)
            {
                var modalCheck = ModalDialogDetector.Detect(
                    visualContext?.VisibleDialogTitle ?? string.Empty,
                    visualContext?.ModalBodyText ?? string.Empty,
                    visualContext?.OcrWindowTitle ?? string.Empty);
                if (modalCheck.IsSecurityAlert)
                    visibleTool = MmcConsoleKind.EndpointSecurity;
            }

            var intent = DetectIntent(userQuestion, visualContext);
            var recommended = GetRecommendedTool(intent, modalSecurity);
            var recommendedName = MmcConsoleDetector.SnapInDisplayName(recommended);

            var isCorrectTool = intent != AdminUserIntent.Unknown
                && intent != AdminUserIntent.ConnectionStatus
                && intent != AdminUserIntent.SecurityAlertInterpretation
                && visibleTool != MmcConsoleKind.Unknown
                && IsToolCompatible(visibleTool, intent);

            var mismatch = intent != AdminUserIntent.Unknown
                && intent != AdminUserIntent.ConnectionStatus
                && visibleTool != MmcConsoleKind.Unknown
                && !isCorrectTool
                && recommended != MmcConsoleKind.Unknown;

            var moduleId = AdminToolExpertCatalog.SelectModuleId(visibleTool, intent, modalSecurity, modalPriority);
            var expertKnowledge = AdminToolExpertCatalog.GetExpertKnowledge(moduleId, intent);

            var focusedContext = visualContext?.FocusedContext ?? string.Empty;
            if (string.IsNullOrWhiteSpace(focusedContext) && modalPriority)
                focusedContext = visualContext?.ModalTitle ?? visibleName;

            var hint = new AdminToolGuidanceHint
            {
                VisibleTool = visibleTool,
                VisibleToolName = visibleName,
                UserIntent = intent,
                RecommendedTool = recommended,
                RecommendedToolName = recommendedName,
                IsToolMismatch = mismatch,
                IsCorrectTool = isCorrectTool,
                ExpertModuleId = moduleId,
                ExpertKnowledge = expertKnowledge,
                HasModalPriority = modalPriority,
                ModalTitle = visualContext?.ModalTitle ?? string.Empty,
                ModalSummary = visualContext?.ModalBodyText ?? string.Empty,
                FocusedContext = focusedContext,
                PromptDirective = BuildPromptDirective(visibleName, visibleTool, intent, recommendedName, mismatch, isCorrectTool, moduleId, focusedContext, modalPriority)
            };

            return hint;
        }

        private static MmcConsoleKind ResolveVisibleTool(ScreenVisualContext? ctx) =>
            ctx?.MmcConsole ?? MmcConsoleKind.Unknown;

        private static string ResolveVisibleName(ScreenVisualContext? ctx, MmcConsoleKind tool)
        {
            if (ctx?.HasModalDialog == true && !string.IsNullOrWhiteSpace(ctx.ModalTitle))
                return ctx.ModalTitle;

            if (!string.IsNullOrWhiteSpace(ctx?.MmcSnapIn))
                return ctx!.MmcSnapIn;

            var name = MmcConsoleDetector.SnapInDisplayName(tool);
            return string.IsNullOrWhiteSpace(name) ? ctx?.ActiveWindowTitle ?? string.Empty : name;
        }

        private static AdminUserIntent DetectIntent(string question, ScreenVisualContext? ctx)
        {
            var q = Normalize(question);
            if (string.IsNullOrWhiteSpace(q))
                return AdminUserIntent.Unknown;

            if (ctx?.DetectedEventId == "1074" || q.Contains("1074"))
                return AdminUserIntent.EventId1074Analysis;

            if (ctx?.ModalPriority == true || ctx?.HasModalDialog == true)
            {
                var modalText = (ctx.ModalBodyText + " " + ctx.ModalTitle).ToLowerInvariant();
                if (modalText.Contains("check point") || modalText.Contains("defender")
                    || modalText.Contains("endpoint") || modalText.Contains("zararlı")
                    || modalText.Contains("malicious") || modalText.Contains("blocked"))
                    return AdminUserIntent.SecurityAlertInterpretation;
            }

            if (QuestionMentionsConnection(question))
                return AdminUserIntent.ConnectionStatus;

            if (RdpPatterns.Any(p => q.Contains(p, StringComparison.Ordinal)))
                return AdminUserIntent.RdpTroubleshooting;

            if (ContainsAny(q, "başka cihaz", "baska cihaz", "ipyi almasın", "ip almasın", "rezervasyon", "reservation", "mac adres", "sabit ip", "sabitlenecek ip"))
                return AdminUserIntent.DhcpReservation;

            if (ContainsAny(q, "ip çakış", "ip cakis", "conflict", "çakışma", "cakisma"))
                return AdminUserIntent.IpConflictPrevention;

            if (ContainsAny(q, "kilidi", "unlock", "kilitli", "locked out", "hesap kilit"))
                return AdminUserIntent.UnlockUser;

            if (ContainsAny(q, "şifre", "sifre", "password", "reset password", "parola", "şifresini", "sifresini"))
                return AdminUserIntent.PasswordReset;

            if (ContainsAny(q, "gruba ekle", "group membership", "üye", "uye", "grup üyel", "member of"))
                return AdminUserIntent.GroupMembership;

            if (ContainsAny(q, "kullanıcı oluştur", "create user", "yeni kullanıcı", "delete user", "kullanıcı sil"))
                return AdminUserIntent.UserManagement;

            if (ContainsAny(q, "a kaydı", "a record", "cname", "dns kaydı", "dns kaydi", "ptr kaydı"))
                return AdminUserIntent.DnsRecordCreation;

            if (ContainsAny(q, "binding", "app pool", "application pool", "iis", "site restart"))
                return AdminUserIntent.IisBindingOrAppPool;

            if (ContainsAny(q, "servis yeniden", "restart service", "hizmet yeniden", "servisi başlat"))
                return AdminUserIntent.ServiceRestart;

            if (ContainsAny(q, "firewall", "güvenlik duvarı", "ağ sorun", "network", "ping", "dns çözümleme", "internet yok"))
                return AdminUserIntent.FirewallNetworkTroubleshooting;

            if (ContainsAny(q, "checkpoint", "check point", "defender", "endpoint", "uyarı", "alert", "zararlı", "malicious", "tehdit"))
                return AdminUserIntent.SecurityAlertInterpretation;

            if (q.Contains("dns"))
                return AdminUserIntent.DnsManagement;

            if (q.Contains("dhcp") || q.Contains("scope"))
                return AdminUserIntent.DhcpManagement;

            if (q.Contains("event") || q.Contains("olay"))
                return AdminUserIntent.EventLogReview;

            if (q.Contains("group policy") || q.Contains("gpo") || q.Contains("grup ilkesi"))
                return AdminUserIntent.GroupPolicy;

            if (FolderPermissionExtractor.IsPermissionQuestion(question))
                return AdminUserIntent.FolderPermission;

            if (q.Contains("service") || q.Contains("hizmet"))
                return AdminUserIntent.ServiceManagement;

            return AdminUserIntent.Unknown;
        }

        private static MmcConsoleKind GetRecommendedTool(AdminUserIntent intent, bool modalSecurity) => intent switch
        {
            AdminUserIntent.SecurityAlertInterpretation => modalSecurity ? MmcConsoleKind.EndpointSecurity : MmcConsoleKind.Unknown,
            AdminUserIntent.PasswordReset or AdminUserIntent.UnlockUser or AdminUserIntent.UserManagement or AdminUserIntent.GroupMembership
                => MmcConsoleKind.ActiveDirectoryUsersAndComputers,
            AdminUserIntent.DhcpReservation or AdminUserIntent.IpConflictPrevention or AdminUserIntent.DhcpManagement
                => MmcConsoleKind.Dhcp,
            AdminUserIntent.DnsRecordCreation or AdminUserIntent.DnsManagement => MmcConsoleKind.DnsManager,
            AdminUserIntent.EventLogReview or AdminUserIntent.EventId1074Analysis => MmcConsoleKind.EventViewer,
            AdminUserIntent.GroupPolicy => MmcConsoleKind.GroupPolicyManagement,
            AdminUserIntent.ServiceRestart or AdminUserIntent.ServiceManagement => MmcConsoleKind.Services,
            AdminUserIntent.IisBindingOrAppPool => MmcConsoleKind.IisManager,
            AdminUserIntent.FirewallNetworkTroubleshooting => MmcConsoleKind.NetworkSettings,
            AdminUserIntent.FolderPermission => MmcConsoleKind.FileExplorer,
            AdminUserIntent.RdpTroubleshooting => MmcConsoleKind.Unknown,
            _ => MmcConsoleKind.Unknown
        };

        private static bool IsToolCompatible(MmcConsoleKind visible, AdminUserIntent intent)
        {
            var recommended = GetRecommendedTool(intent, intent == AdminUserIntent.SecurityAlertInterpretation);
            if (recommended == MmcConsoleKind.Unknown)
                return true;

            if (visible == recommended)
                return true;

            if (intent is AdminUserIntent.DhcpReservation or AdminUserIntent.IpConflictPrevention
                && visible == MmcConsoleKind.Dhcp)
                return true;

            if (intent is AdminUserIntent.PasswordReset or AdminUserIntent.UnlockUser or AdminUserIntent.GroupMembership
                && visible == MmcConsoleKind.ActiveDirectoryUsersAndComputers)
                return true;

            if (intent == AdminUserIntent.ServiceRestart
                && (visible == MmcConsoleKind.Services || visible == MmcConsoleKind.ComputerManagement))
                return true;

            if (intent == AdminUserIntent.SecurityAlertInterpretation
                && visible == MmcConsoleKind.EndpointSecurity)
                return true;

            if (intent == AdminUserIntent.FolderPermission)
            {
                if (visible is MmcConsoleKind.FileExplorer or MmcConsoleKind.ComputerManagement)
                    return true;
                return false;
            }

            return false;
        }

        private static string BuildPromptDirective(
            string visibleName,
            MmcConsoleKind visibleTool,
            AdminUserIntent intent,
            string recommendedName,
            bool mismatch,
            bool isCorrectTool,
            string moduleId,
            string focusedContext,
            bool modalPriority)
        {
            var sb = new System.Text.StringBuilder();

            if (modalPriority)
                sb.Append("PRIORITY: Analyze the visible modal/popup/dialog first, not background windows. ");

            if (!string.IsNullOrWhiteSpace(focusedContext))
                sb.Append($"Focused context: {focusedContext}. ");

            sb.Append($"Visible tool: {visibleName}. User intent: {intent}. Expert module: {moduleId}. ");

            if (mismatch)
            {
                sb.Append("Bu işlem bu ekrandan yapılmaz. ");
                sb.Append($"Doğru araç: {recommendedName}. ");
                sb.Append("Açıkça yanlış aracı belirt, doğru aracı söyle, kısa adımları ver. ");
                sb.Append("Yanlış araç içinde adım uydurma. ");
            }
            else if (isCorrectTool)
            {
                sb.Append("Kullanıcı doğru ekranda; eksik adımları (scope açma, menü yolu vb.) tamamla. ");
            }

            if (intent == AdminUserIntent.FolderPermission)
            {
                sb.Append("Kullanıcı path verdiyse path her zaman ana kanıttır; ekrandaki seçili öğeyi hedef sanma. ");
                sb.Append("Hedef klasör görünmüyorsa açıkça söyle. Güven: Yüksek yalnızca ekran ve hedef uyuşuyorsa. ");
            }

            sb.Append("Kısa, uygulanabilir, kıdemli sysadmin cevabı ver. ");
            sb.Append("Netvium/RDP bağlantısından bahsetme (kullanıcı sormadıysa). ");
            sb.Append("Resmi Microsoft araç adlarını koru; Türkçe doğal cevap ver.");

            return sb.ToString();
        }

        private static bool ContainsAny(string haystack, params string[] needles) =>
            needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

        private static string Normalize(string text) =>
            text.Trim().ToLowerInvariant();
    }
}
