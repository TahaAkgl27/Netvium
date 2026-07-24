namespace Netvium.Services.AIGuide
{
    internal static class AIGuideWorkflowLocalization
    {
        private static readonly Dictionary<string, (string Name, Dictionary<string, string> Steps)> Map = BuildMap();

        public static string LocalizeName(string? workflowId, string englishName)
        {
            if (!AIGuideLanguage.IsTurkish || string.IsNullOrWhiteSpace(workflowId))
                return englishName;

            return Map.TryGetValue(workflowId, out var entry) ? entry.Name : englishName;
        }

        public static string LocalizeStepName(string? workflowId, string? stepId, string englishName)
        {
            if (!AIGuideLanguage.IsTurkish
                || string.IsNullOrWhiteSpace(workflowId)
                || string.IsNullOrWhiteSpace(stepId)
                || !Map.TryGetValue(workflowId, out var entry)
                || !entry.Steps.TryGetValue(stepId, out var localized))
            {
                return englishName;
            }

            return localized;
        }

        private static Dictionary<string, (string Name, Dictionary<string, string> Steps)> BuildMap() =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["ad-password-reset"] = (
                    "Active Directory Kullanıcı Parolasını Sıfırlama",
                    Steps("1", "Kullanıcılar Konteynerine Göz At",
                        "2", "Kullanıcı Seçildi",
                        "3", "Kullanıcı Özellikleri Açık",
                        "4", "Parola Sıfırlama Diyaloğu")),
                ["ad-user-create"] = (
                    "Active Directory Kullanıcısı Oluşturma",
                    Steps("1", "AD Konsolu Açık",
                        "2", "Yeni Kullanıcı İşlemi",
                        "3", "Kullanıcı Ayrıntıları Giriliyor",
                        "4", "Kullanıcı Oluşturma Tamamlandı")),
                ["ad-user-unlock"] = (
                    "Active Directory Kullanıcı Hesabının Kilidini Açma",
                    Steps("1", "Kullanıcılar Konteynerine Göz At",
                        "2", "Kullanıcı Seçildi",
                        "3", "Hesabın Kilidini Açma İşlemi",
                        "4", "Kilit Açma Onayı")),
                ["ad-group-membership"] = (
                    "Active Directory Grup Üyeliğini Yönetme",
                    Steps("1", "Grup veya Kullanıcı Seçildi",
                        "2", "Özellikler Açık",
                        "3", "Üye Ekle veya Çıkar",
                        "4", "Üyelik Güncellendi")),
                ["dns-create-a-record"] = (
                    "DNS A Kaydı Oluşturma",
                    Steps("1", "DNS Manager Açık",
                        "2", "Bölge Seçildi",
                        "3", "Yeni Host Kaydı",
                        "4", "Kayıt Özellikleri")),
                ["dns-reverse-lookup"] = (
                    "Ters Arama Bölgesi Yapılandırma",
                    Steps("1", "DNS Manager Açık",
                        "2", "Ters Bölge Seçildi",
                        "3", "Yeni Pointer Kaydı",
                        "4", "Kayıt Oluşturuldu")),
                ["dns-conditional-forwarder"] = (
                    "Koşullu Yönlendirici Yapılandırma",
                    Steps("1", "DNS Manager Açık",
                        "2", "Yönlendirici Ayarları",
                        "3", "Yönlendirici Etki Alanı Girişi",
                        "4", "Yönlendirici Kaydedildi")),
                ["hyper-v-create-vm"] = (
                    "Hyper-V Sanal Makine Oluşturma",
                    Steps("1", "Yeni Sanal Makine Sihirbazı",
                        "2", "Ad ve Konum Belirt",
                        "3", "Bellek ve Depolama Yapılandır",
                        "4", "VM Oluşturmayı Tamamla")),
                ["hyper-v-start-vm"] = (
                    "Hyper-V Sanal Makinesini Başlatma",
                    Steps("1", "VM Seçildi",
                        "2", "Başlatma İşlemi Görünür",
                        "3", "VM Başlatılıyor",
                        "4", "VM Çalışıyor")),
                ["iis-restart-app-pool"] = (
                    "IIS Uygulama Havuzunu Yeniden Başlatma",
                    Steps("1", "Uygulama Havuzları Görünümü",
                        "2", "Havuz Seçildi",
                        "3", "Yeniden Başlatma İşlemi",
                        "4", "Havuz Yeniden Başlatıldı")),
                ["event-investigation"] = (
                    "Event Viewer İncelemesi",
                    Steps("1", "Günlük Seçildi",
                        "2", "Olay Seçildi",
                        "3", "Olay Ayrıntıları İnceleniyor",
                        "4", "Analiz Tamamlandı")),
                ["service-restart"] = (
                    "Windows Hizmetini Yeniden Başlatma",
                    Steps("1", "Hizmet Seçildi",
                        "2", "Hizmet Durumu İnceleniyor",
                        "3", "Yeniden Başlatma İşlemi Görünür",
                        "4", "Hizmet Yeniden Başlatıldı")),
                ["task-troubleshoot"] = (
                    "Görev Zamanlayıcı Sorun Giderme",
                    Steps("1", "Görev Seçildi",
                        "2", "Geçmiş İnceleniyor",
                        "3", "Hata Analizi",
                        "4", "Çözüm İnceleniyor"))
            };

        private static Dictionary<string, string> Steps(params string[] idNamePairs)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < idNamePairs.Length; i += 2)
                dict[idNamePairs[i]] = idNamePairs[i + 1];
            return dict;
        }
    }
}
