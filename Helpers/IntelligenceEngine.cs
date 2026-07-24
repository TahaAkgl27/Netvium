using System;
using System.Collections.Generic;
using System.Linq;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class IntelligenceEngine
    {
        public static ServerIntelligence GenerateIntelligence(WmiSnapshot snap, string serverName, List<WmiSnapshot>? history = null)
        {
            if (snap == null || !snap.Connected)
            {
                return new ServerIntelligence(
                    ServerName: serverName,
                    DetectedRole: "Bilinmiyor / Çevrimdışı",
                    Confidence: 0,
                    Components: new List<DetectedComponent>(),
                    Insights: new List<IntelligenceInsight> { new IntelligenceInsight("Erişim Sorunu", "Sunucuya WMI üzerinden ulaşılamıyor.", InsightSeverity.Critical, "🚫") },
                    Recommendations: new List<IntelligenceRecommendation> { new IntelligenceRecommendation("Bağlantı Kontrolü", "Ağ bağlantısını ve WMI izinlerini doğrulayın.", "High") },
                    SmartInsights: new List<SmartInsight> { new SmartInsight("Sunucu Çevrimdışı", InsightSeverity.Critical, "🚫", true, "Veri toplanamadığı için yapay zeka analizi askıya alındı.") }
                );
            }

            // ── DETECTION FLAGS ──────────────────────────────────────────────
            bool isIIS  = snap.Services.Any(s => s.Name.Equals("W3SVC",        StringComparison.OrdinalIgnoreCase) && s.State == "Running");
            bool isSQL  = snap.Services.Any(s => (s.Name.Contains("MSSQLSERVER") || s.Name.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase)) && s.State == "Running")
                          || snap.ListenPorts.Contains(1433)
                          || snap.TopProcesses.Any(p => p.Name.Contains("sqlservr", StringComparison.OrdinalIgnoreCase));
            
            bool isMikro = snap.TopProcesses.Any(p => p.Name.Contains("Mikro", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("V16", StringComparison.OrdinalIgnoreCase));
            if (isMikro) isSQL = true; // Mikro implies SQL backend usually

            bool isAD   = snap.Services.Any(s => s.Name.Equals("NTDS",         StringComparison.OrdinalIgnoreCase) && s.State == "Running")
                          || snap.ListenPorts.Contains(389) 
                          || snap.ListenPorts.Contains(3268);
            
            bool isFile = snap.Services.Any(s => s.Name.Equals("LanmanServer", StringComparison.OrdinalIgnoreCase) && s.State == "Running");
            bool hasLargeStorage = (snap.DiskTotalGb > 800); // 800GB+ is likely a storage/file server
            bool hasSharedFolders = snap.SharedFolders.Count(f => !f.EndsWith("$")) >= 1;

            // TermService runs on ALL Windows servers by default for remote admin.
            // Only classify as Terminal Server (RDS) if there is real evidence:
            //   - Multiple active user sessions (actual RDS usage), OR
            //   - Specific RDS role services are running (UmRdpService + SessionEnv)
            bool termServiceOn = snap.Services.Any(s => s.Name.Equals("TermService", StringComparison.OrdinalIgnoreCase) && s.State == "Running");
            bool hasRdsRoleServices = snap.Services.Any(s => s.Name.Equals("UmRdpService",  StringComparison.OrdinalIgnoreCase) && s.State == "Running")
                                   && snap.Services.Any(s => s.Name.Equals("SessionEnv",    StringComparison.OrdinalIgnoreCase) && s.State == "Running");
            int  activeUserSessions = snap.Sessions?.Count(s => !string.IsNullOrEmpty(s.User) && s.User != "-" && s.User != "System") ?? 0;
            bool isRDS = termServiceOn && (hasRdsRoleServices || activeUserSessions > 1) && !isIIS && !isSQL;

            var detectedRoles = new List<string>();
            if (isAD)   detectedRoles.Add("Domain Controller");
            if (isSQL)  detectedRoles.Add(isMikro ? "Mikro SQL Server" : "Database Server");
            if (isIIS)  detectedRoles.Add("Web Server (IIS)");
            if (isRDS)  detectedRoles.Add("Terminal Server (RDS)");
            
            // File Server logic: If it has large storage and shares, it's a file server.
            // If it's ONLY a file server (no other roles), we add it.
            // If it has other roles but HUGE storage, we might still want to call it Mixed or prioritize it.
            if (isFile && (hasSharedFolders || hasLargeStorage))
            {
                if (snap.DiskTotalGb > 1500 || snap.SharedFolders.Count(f => !f.EndsWith("$")) >= 3)
                    detectedRoles.Add("File Server");
                else if (detectedRoles.Count == 0 && hasSharedFolders)
                    detectedRoles.Add("File Server");
            }

            string role      = detectedRoles.Count > 1  ? "Mixed Role Server"     :
                               detectedRoles.Count == 1 ? detectedRoles[0]         : "Application Server";
            int    confidence = detectedRoles.Count > 1  ? 85 :
                               detectedRoles.Count == 1 ? 95  : 60;

            var components = new List<DetectedComponent>();
            
            // ── BASE COMPONENTS (Fallback) ──────────────────────────────────
            components.Add(new DetectedComponent("Windows OS Core", "System", "Aktif", "🪟"));
            if (snap.IPAddresses.Any()) 
                components.Add(new DetectedComponent("TCP/IP Stack", "Network", $"{snap.IPAddresses.Count} IP", "🌐"));
            
            bool winrmOn = snap.Services.Any(s => s.Name.Equals("WinRM", StringComparison.OrdinalIgnoreCase) && s.State == "Running");
            if (winrmOn) components.Add(new DetectedComponent("WinRM Management", "Service", "Aktif", "⚙️"));

            if (termServiceOn)
                components.Add(new DetectedComponent("RDP Access", "Service", activeUserSessions > 0 ? $"{activeUserSessions} Oturum" : "Beklemede", "💻"));

            // ── ROLE COMPONENTS ──────────────────────────────────────────────
            if (isIIS)  components.Add(new DetectedComponent("IIS Web Server",  "Role", "Aktif", "🌐"));
            if (isSQL)  components.Add(new DetectedComponent("SQL Server",      "Role", "Aktif", "💾"));
            if (isAD)   components.Add(new DetectedComponent("Active Directory","Role", "Aktif", "🛡️"));
            if (isRDS)  components.Add(new DetectedComponent("RDS Farm Node",    "Role", "Aktif", "🏢"));
            if (isFile && hasSharedFolders)
                        components.Add(new DetectedComponent("Dosya Paylaşımı", "Role", "Aktif", "📁"));
            
            if (hasLargeStorage)
                        components.Add(new DetectedComponent("Yüksek Kapasiteli Depolama", "Storage", $"{snap.DiskTotalGb:F0} GB", "💾"));

            // System security & infrastructure probes
            var statusComponents = snap.SystemStatusComponents?.Count > 0
                ? snap.SystemStatusComponents
                : SystemStatusProbe.BuildComponents(snap);
            foreach (var sc in statusComponents)
            {
                if (!components.Any(c => c.Name.Equals(sc.Name, StringComparison.OrdinalIgnoreCase)))
                    components.Add(sc);
            }
            
            if (isIIS && !isRDS && !isAD && !isSQL)
                        components.Add(new DetectedComponent("Web Uygulaması",   "Service", "Barındırılıyor", "🌐"));

            var smartInsights = new List<SmartInsight>();

            // ── SECTION 1: SERVER ROLE ────────────────────────────────────────
            string roleReason = "Algılanan servis ve bileşenlere göre rol tespiti yapıldı.";
            if (isAD && isRDS)        roleReason = "NTDS (Active Directory) ve TermService (RDS) servisleri aktif — çift rol tespit edildi.";
            else if (isAD)            roleReason = "LDAP/NTDS aktif — bu sunucu ağ kimlik doğrulaması ve AD servislerini yönetiyor.";
            else if (isRDS)           roleReason = "TermService (Uzak Masaüstü Hizmetleri) aktif — kullanıcılar bu sunucuya RDP ile bağlanıyor.";
            else if (isSQL && isIIS)  roleReason = "W3SVC (IIS) ve SQL servisleri aktif — web uygulaması ve veritabanı aynı sunucuda.";
            else if (isSQL)           roleReason = isMikro ? "Mikro ERP süreçleri ve SQL servisleri tespit edildi." : "SQL Server servisi veya portu (1433) aktif — veritabanı motoru barındırıyor.";
            else if (isIIS)           roleReason = "W3SVC (IIS) servisi aktif — web servisleri sunuyor.";
            else if (role == "File Server") roleReason = $"Yüksek kapasiteli disk ({snap.DiskTotalGb:F0} GB) ve {snap.SharedFolders.Count(f => !f.EndsWith("$"))} aktif paylaşım tespit edildi.";

            smartInsights.Add(new SmartInsight("SERVER ROLE", InsightSeverity.Info, "🖥️", true,
                $"{role}\n\n{roleReason}"));

            // ── SECTION 2: AI INSIGHTS ────────────────────────────────────────
            var insightLines = new List<string>();

            if (isRDS)
            {
                int activeSessions = (snap.Sessions ?? Enumerable.Empty<SessionRow>()).Count(s => !string.IsNullOrEmpty(s.User) && s.User != "-");
                insightLines.Add($"Bu sistem, {activeSessions} aktif kullanıcı oturumu barındıran bir Uzak Masaüstü sunucusu olarak çalışıyor.");
            }
            else if (isAD)
            {
                insightLines.Add("Bu sistem, ağdaki tüm kullanıcı kimlik doğrulama isteklerini ve politika dağıtımını (GPO) yönetiyor.");
            }
            else if (isSQL)
            {
                insightLines.Add("Bu sistem, kurumsal veritabanı sorgularını işleyen ve veriyi depolayan bir merkezi database engine üzerinde çalışıyor.");
            }
            else if (isIIS)
            {
                insightLines.Add("Bu sistem, web isteklerini karşılayan ve uygulama havuzlarını yöneten bir IIS web sunucusu olarak hizmet veriyor.");
            }

            double cpu  = snap.CpuPct     ?? 0;
            double ram  = snap.RamUsedPct ?? 0;
            double disk = snap.DiskUsedPct ?? 0;

            if (cpu < 40 && ram < 60 && disk < 80)
                insightLines.Add("Güncel kaynak kullanımı güvenli sınırlar içinde seyrediyor.");
            else if (cpu > 70)
                insightLines.Add($"İşlemci yükü %{cpu} seviyesiyle yüksek — aktif bir iş yükü veya arkaplan sürecinin kaynağı araştırılmalı.");
            else if (ram > 80)
                insightLines.Add($"Bellek kullanımı %{ram:F0} ile kritik seviyeye yaklaşıyor — sistem disk üzerinden swap kullanmaya başlayabilir.");

            if (disk > 70 && disk < 85)
                insightLines.Add($"Disk kullanımı orta-yüksek (%{disk:F0}) — henüz kritik değil ama büyüme hızı izlenmeli.");
            else if (disk >= 85)
                insightLines.Add($"Disk kullanımı %{disk:F0} ile kritik eşiğe yaklaşmış — acil temizlik veya kapasite planlaması gerekiyor.");

            smartInsights.Add(new SmartInsight("AI INSIGHTS", InsightSeverity.Info, "🧠", true,
                string.Join("\n\n", insightLines)));

            // ── SECTION 3: INFRASTRUCTURE FINDINGS ───────────────────────────
            var findings = new List<string>();

            if (isRDS)
            {
                int active = (snap.Sessions ?? Enumerable.Empty<SessionRow>()).Count(s => !string.IsNullOrEmpty(s.User) && s.User != "-");
                findings.Add($"Aktif RDP oturumu: {active}");
            }

            var topProc = snap.TopProcesses?.OrderByDescending(p => p.CpuPct).FirstOrDefault();
            if (topProc != null)
                findings.Add($"En yoğun süreç: {topProc.Name} (%{topProc.CpuPct:F1} CPU, PID: {topProc.Id})");

            findings.Add($"Bellek kullanımı: %{ram:F0}");
            findings.Add($"Disk kullanımı: %{disk:F0}");
            findings.Add($"CPU kullanımı: %{cpu:F0}");

            if (isFile)
            {
                var shares = snap.SharedFolders.Where(f => !f.EndsWith("$")).ToList();
                findings.Add($"Aktif paylaşım klasörü: {shares.Count}" + (shares.Count > 0 ? $" ({string.Join(", ", shares.Take(3))})" : ""));
            }

            if (isSQL)
            {
                var sqlProc = snap.TopProcesses?.FirstOrDefault(p => p.Name.Contains("sqlservr", StringComparison.OrdinalIgnoreCase));
                if (sqlProc != null) findings.Add($"SQL motoru bellek kullanımı: {sqlProc.RamMb:F0} MB");
            }

            // Disk prediction
            if (history?.Count > 4 && disk > 50)
            {
                var growth = disk - (history.First().DiskUsedPct ?? 0);
                if (growth > 0)
                {
                    var days = (snap.CollectedAt - history.First().CollectedAt).TotalDays;
                    if (days >= 1)
                    {
                        var daily = growth / days;
                        var daysLeft = Math.Round((95 - disk) / daily);
                        findings.Add($"Disk büyüme hızı: günlük %{daily:F2} — mevcut hızla %95'e ulaşmak yaklaşık {daysLeft} gün sürecek");
                    }
                }
            }

            // Stopped critical services
            var stoppedCritical = snap.Services.Where(s => s.IsCritical && s.State != "Running").ToList();
            if (stoppedCritical.Count > 0)
                findings.Add($"⚠️ Durmuş kritik servis: {string.Join(", ", stoppedCritical.Select(s => s.Name))}");

            smartInsights.Add(new SmartInsight("INFRASTRUCTURE FINDINGS", InsightSeverity.Warning, "🔍", true,
                string.Join("\n", findings)));

            // ── SECTION 4: ACTIONABLE RECOMMENDATIONS ────────────────────────
            var actions = new List<string>();

            if (isRDS)
            {
                actions.Add("Boşta bekleyen (Idle) oturumlar için GPO üzerinden oturum zaman aşımı kuralı tanımlayın.");
                if ((snap.Sessions?.Count ?? 0) > 10) actions.Add("Eş zamanlı oturum sayısı artıyorsa RDS lisans kapasitesini gözden geçirin.");
            }
            if (isAD)
            {
                actions.Add("'dcdiag' ve 'repadmin /replsummary' komutlarıyla AD replikasyon sağlığını haftalık kontrol edin.");
                actions.Add("Kullanılmayan bilgisayar ve kullanıcı nesnelerini düzenli temizleyerek dizin bütünlüğünü koruyun.");
            }
            if (isSQL)
            {
                actions.Add("SQL Transaction Log yedek zincirini doğrulayın; büyük LDF dosyalarını gerektiğinde shrink edin.");
                actions.Add("TempDB otomatik büyüme (auto-growth) ayarlarını gözden geçirin.");
            }
            if (isIIS)
            {
                actions.Add("IIS log klasörünü ('C:\\inetpub\\logs') düzenli olarak temizleyin veya log rotasyonu tanımlayın.");
                actions.Add("Application Pool recycling zamanlamasını düşük trafiğin olduğu saatlere (örn. 03:00) göre ayarlayın.");
            }
            if (disk > 70)
                actions.Add($"Disk kullanımı %{disk:F0} — büyüme devam ederse depolama temizliği veya genişletme planı oluşturun.");
            if (disk >= 85)
                actions.Add("'C:\\Windows\\Temp' ve kullanıcı Temp klasörlerini temizleyerek acil disk alanı açın.");
            if (cpu > 70 && topProc != null)
                actions.Add($"'{topProc.Name}' süreci %{topProc.CpuPct:F1} CPU tüketiyor — davranışını Process Monitor ile inceleyin.");

            if (actions.Count == 0)
                actions.Add("Şu an için kritik bir müdahale gereksinimi görünmüyor. Düzenli gözlemi sürdürün.");

            smartInsights.Add(new SmartInsight("ACTIONABLE RECOMMENDATIONS", InsightSeverity.Tip, "💡", true,
                string.Join("\n", actions)));

            // ── COMPATIBILITY LISTS ───────────────────────────────────────────
            var insights = new List<IntelligenceInsight>();
            var recommendations = new List<IntelligenceRecommendation>();

            insights.Add(new IntelligenceInsight("Sunucu Rolü", roleReason, InsightSeverity.Info, "🖥️"));
            if (insightLines.Any()) insights.Add(new IntelligenceInsight("AI Analizi", insightLines.First(), InsightSeverity.Info, "🧠"));
            if (findings.Any())     insights.Add(new IntelligenceInsight("Altyapı Bulguları", string.Join(" | ", findings.Take(3)), InsightSeverity.Warning, "🔍"));

            foreach (var a in actions)
                recommendations.Add(new IntelligenceRecommendation("Operasyonel Öneri", a,
                    disk >= 85 || stoppedCritical.Count > 0 ? "High" : "Medium"));

            return new ServerIntelligence(serverName, role, confidence, components, insights, recommendations, smartInsights);
        }

        public static string GetQuickInsight(WmiSnapshot snap)
        {
            if (snap == null || !snap.Connected) return "Sunucuya erişilemiyor.";

            double cpu = snap.CpuPct ?? 0;
            double ram = snap.RamUsedPct ?? 0;
            double disk = snap.DiskUsedPct ?? 0;

            if (disk >= 90) return "Disk doluluğu kritik seviyede, acil müdahale gerek.";
            if (cpu > 80) return "İşlemci yükü çok yüksek, performans kaybı yaşanabilir.";
            if (ram > 90) return "Bellek yetersiz, sistem yavaşlaması bekleniyor.";

            var stoppedCritical = snap.Services.FirstOrDefault(s => s.IsCritical && s.State != "Running");
            if (stoppedCritical != null) return $"{stoppedCritical.Name} servisi durmuş durumda.";

            if (snap.Sessions.Count(s => !string.IsNullOrEmpty(s.User) && s.User != "-") > 15)
                return "Yüksek kullanıcı yoğunluğu tespit edildi.";

            if (cpu < 30 && ram < 60 && disk < 80) return "Sistem normal ve kararlı çalışıyor.";
            
            return "Sistem kaynakları aktif kullanımda.";
        }
    }
}
