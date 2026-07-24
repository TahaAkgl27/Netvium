using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Netvium.Helpers;
using Netvium.Models;

namespace Netvium.Services;

/// <summary>Applies the selected UI language to literal WPF text when a window is created.</summary>
public static class UiLocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> EnglishToTurkish =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Settings"] = "Ayarlar",
            ["General"] = "Genel",
            ["Appearance"] = "Görünüm",
            ["Monitoring"] = "İzleme",
            ["Notifications"] = "Bildirimler",
            ["Connections"] = "Bağlantılar",
            ["Performance"] = "Performans",
            ["Security"] = "Güvenlik",
            ["License"] = "Lisans",
            ["Logs"] = "Kayıtlar",
            ["About"] = "Hakkında",
            ["Save"] = "Kaydet",
            ["Cancel"] = "İptal",
            ["Close"] = "Kapat",
            ["Refresh"] = "Yenile",
            ["Current Status"] = "Mevcut Durum",
            ["Saving..."] = "Kaydediliyor...",
            ["Restore Defaults"] = "Varsayılanları Geri Yükle",
            ["Disabled"] = "Devre Dışı",
            ["SearchSettings"] = "Ayarlarda ara",
            ["All changes saved"] = "Tüm değişiklikler kaydedildi",
            ["Unsaved changes"] = "Kaydedilmemiş değişiklikler",
            ["Choose Your Plan"] = "Planınızı Seçin",
            ["Netvium Plans"] = "Netvium Planları",
            ["Current Plan"] = "Mevcut Plan",
            ["Most Popular"] = "En Popüler",
            ["Enterprise"] = "Kurumsal",
            ["Essential Operations"] = "Temel İşlemler",
            ["AI Intelligence"] = "Yapay Zekâ İçgörüleri",
            ["AI Assistant"] = "Yapay Zekâ Asistanı",
            ["Full server management and monitoring"] = "Tam sunucu yönetimi ve izleme",
            ["Full AI analysis and reporting"] = "Tam yapay zekâ analizi ve raporlama",
            ["Everything in Free, plus:"] = "Free pakete ek olarak:",
            ["Everything in Premium, plus:"] = "Premium pakete ek olarak:",
            ["Enter Code to Activate"] = "Etkinleştirmek İçin Kod Girin",
            ["Included in Your Plan"] = "Planınıza Dahil",
            ["Activate License"] = "Lisansı Etkinleştir",
            ["Activate"] = "Etkinleştir",
            ["Enter Activation Code"] = "Aktivasyon kodunu girin",
            ["Server Management"] = "Sunucu Yönetimi",
            ["RDP & Remote Sessions"] = "RDP ve Uzak Oturumlar",
            ["Performance Monitoring"] = "Performans İzleme",
            ["Dashboard & Widgets"] = "Pano ve Bileşenler",
            ["Events & Telemetry"] = "Olaylar ve Telemetri",
            ["AI Reports & Export"] = "Yapay Zekâ Raporları ve Dışa Aktarım",
            ["AI Risk Analysis"] = "Yapay Zekâ Risk Analizi",
            ["Infrastructure Intelligence"] = "Altyapı İçgörüleri",
            ["Predictions & Recommendations"] = "Tahminler ve Öneriler",
            ["Coming Soon"] = "Yakında",
            ["Premium+ Exclusive"] = "Premium+ Özel",
            ["Admin License Console"] = "Yönetici Lisans Konsolu",
            ["Users"] = "Kullanıcılar",
            ["Activation Codes"] = "Aktivasyon Kodları",
            ["Usage"] = "Kullanım",
            ["Audit"] = "Denetim",
            ["Create Code"] = "Kod Oluştur",
            ["Copy Code"] = "Kodu Kopyala",
            ["Connecting..."] = "Bağlanıyor...",
            ["Please wait"] = "Lütfen bekleyin",
            ["Connection failed"] = "Bağlantı başarısız",
            ["Unable to connect."] = "Bağlantı kurulamadı.",
            ["Diagnostics"] = "Tanılama",
            ["Application Name"] = "Uygulama Adı",
            ["Language"] = "Dil",
            ["Start with Windows"] = "Windows ile Başlat",
            ["Auto Update"] = "Otomatik Güncelleme",
            ["Show Startup Overview"] = "Başlangıç Özetini Göster"
            ,["Plans"] = "Planlar"
            ,["Admin"] = "Yönetici"
            ,["Servers"] = "Sunucular"
            ,["Healthy"] = "Sağlıklı"
            ,["Warning"] = "Uyarı"
            ,["Critical"] = "Kritik"
            ,["Connections"] = "Bağlantılar"
            ,["Folder / Host list"] = "Klasör / Sunucu listesi"
            ,["Connect"] = "Bağlan"
            ,["Disconnect"] = "Bağlantıyı Kes"
            ,["Sort"] = "Sırala"
            ,["Quick Connect"] = "Hızlı Bağlantı"
            ,["Host / IP"] = "Sunucu / IP"
            ,["Username"] = "Kullanıcı adı"
            ,["Password"] = "Parola"
            ,["Active Connection"] = "Aktif Bağlantı"
            ,["Connected"] = "Bağlı"
            ,["Waiting for connection..."] = "Bağlantı bekleniyor..."
            ,["Clipboard"] = "Pano"
            ,["Copy"] = "Kopyala"
            ,["Paste"] = "Yapıştır"
            ,["File transfer"] = "Dosya aktarımı"
            ,["Session Settings"] = "Oturum Ayarları"
            ,["Performance and input"] = "Performans ve giriş"
            ,["Low"] = "Düşük"
            ,["Balanced"] = "Dengeli"
            ,["High"] = "Yüksek"
            ,["Event Log"] = "Olay Günlüğü"
            ,["Add Server"] = "Sunucu Ekle"
            ,["RDP connection unavailable"] = "RDP bağlantısı kullanılamıyor"
            ,["Add a server and connect."] = "Bir sunucu ekleyip bağlanın."
            ,["Console"] = "Konsol"
            ,["Quick Scan"] = "Hızlı Tarama"
            ,["Workspace"] = "Çalışma Alanı"
            ,["Edit Layout"] = "Düzeni Düzenle"
            ,["Save Layout"] = "Düzeni Kaydet"
            ,["Auto Arrange"] = "Otomatik Düzenle"
            ,["Lock Layout"] = "Düzeni Kilitle"
            ,["Reset"] = "Sıfırla"
            ,["Import"] = "İçe Aktar"
            ,["Export"] = "Dışa Aktar"
            ,["System Status"] = "Sistem Durumu"
            ,["Core service health"] = "Temel servis sağlığı"
            ,["Loading system status"] = "Sistem durumu yükleniyor"
            ,["Waiting for the first server health snapshot."] = "İlk sunucu sağlık verisi bekleniyor."
            ,["HEALTHY SERVICES"] = "SAĞLIKLI SERVİSLER"
            ,["WARNINGS"] = "UYARILAR"
            ,["OFFLINE"] = "ÇEVRİMDIŞI"
            ,["OVERALL HEALTH"] = "GENEL SAĞLIK"
            ,["LAST SCAN"] = "SON TARAMA"
            ,["Details"] = "Ayrıntılar"
            ,["Category"] = "Kategori"
            ,["Current status"] = "Geçerli durum"
            ,["Last checked"] = "Son kontrol"
            ,["Performance Overview"] = "Performans Özeti"
            ,["CPU Usage"] = "CPU Kullanımı"
            ,["RAM Usage"] = "RAM Kullanımı"
            ,["Disk Usage"] = "Disk Kullanımı"
            ,["Network"] = "Ağ"
            ,["IP Address"] = "IP Adresi"
            ,["Uptime"] = "Çalışma Süresi"
            ,["Server Information"] = "Sunucu Bilgileri"
            ,["Recent Events"] = "Son Olaylar"
            ,["AI intelligence features are licensed separately. Server management and monitoring remain available on all plans."] = "Yapay zekâ özellikleri ayrı lisanslanır. Sunucu yönetimi ve izleme tüm planlarda kullanılabilir."
            ,["AI Smart Analysis"] = "Yapay Zekâ Akıllı Analiz"
            ,["AI Reports"] = "Yapay Zekâ Raporları"
            ,["AI Risk Engine"] = "Yapay Zekâ Risk Motoru"
            ,["Report Center · PDF · Email"] = "Rapor Merkezi · PDF · E-posta"
            ,["Next-generation guided operations"] = "Yeni nesil rehberli işlemler"
            ,["Live Screen Reading"] = "Canlı Ekran Okuma"
            ,["Visual AI Assistant"] = "Görsel Yapay Zekâ Asistanı"
            ,["Context Awareness"] = "Bağlam Farkındalığı"
            ,["Workflow Detection"] = "İş Akışı Algılama"
            ,["Interactive Help & AI Mentor"] = "Etkileşimli Yardım ve Yapay Zekâ Mentoru"
            ,["Future Enterprise AI"] = "Geleceğin Kurumsal Yapay Zekâsı"
            ,["Enter your activation code to unlock Premium features on this device."] = "Bu cihazda Premium özellikleri açmak için aktivasyon kodunuzu girin."
            ,["Your activation code unlocks Premium features on this device. No payment · No credit card · No subscription."] = "Aktivasyon kodunuz bu cihazda Premium özellikleri açar. Ödeme, kredi kartı veya abonelik yoktur."
            ,["Premium and Premium+ each require a valid activation code. Enter your code above or in Settings → License."] = "Premium ve Premium+ için geçerli bir aktivasyon kodu gerekir. Kodunuzu yukarıdan girin."
            ,["Log Off Session"] = "Oturumu Kapat"
            ,["Start Service"] = "Servisi Başlat"
            ,["Stop Service"] = "Servisi Durdur"
            ,["Restart Service"] = "Servisi Yeniden Başlat"
            ,["Add server to folder"] = "Sunucuyu klasöre ekle"
            ,["Save to List"] = "Listeye Kaydet"
            ,["Add"] = "Ekle"
            ,["Delete"] = "Sil"
            ,["Loading..."] = "Yükleniyor..."
            ,["Activation code created"] = "Aktivasyon kodu oluşturuldu"
            ,["This code is displayed only once. Store it in a secure location."] = "Bu kod yalnızca şimdi görüntülenir. Güvenli bir yerde saklayın."
            ,["ONE-TIME CODE"] = "TEK KULLANIMLIK KOD"
            ,["Copy the code before sharing it."] = "Kodu paylaşmadan önce kopyalayın."
            ,["Code copied to clipboard."] = "Kod panoya kopyalandı."
            ,["Account, license, activation and usage management"] = "Hesap, lisans, aktivasyon ve kullanım yönetimi"
            ,["TOTAL ACCOUNTS"] = "TOPLAM HESAP"
            ,["ACTIVE ACCOUNTS"] = "AKTİF HESAP"
            ,["ACTIVE DEVICES"] = "AKTİF CİHAZ"
            ,["NEW ACTIVATION CODE"] = "YENİ AKTİVASYON KODU"
            ,["Optional note"] = "Not (isteğe bağlı)"
            ,["Generate Code"] = "Kod Üret"
            ,["Sign in"] = "Giriş yap"
        };

    private static IReadOnlyDictionary<string, string> TurkishToEnglish { get; } =
        EnglishToTurkish.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

    public static void ApplyCulture(AppSettings settings)
    {
        var culture = string.Equals(settings.Language, "tr", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("tr-TR")
            : CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static string T(string english, string turkish) =>
        string.Equals(SettingsRuntime.Current.Language, "tr", StringComparison.OrdinalIgnoreCase)
            ? turkish
            : english;

    public static void Apply(Window window, AppSettings settings)
    {
        var translations = string.Equals(settings.Language, "tr", StringComparison.OrdinalIgnoreCase)
            ? EnglishToTurkish
            : TurkishToEnglish;

        window.Title = Translate(window.Title, translations);
        TranslateElement(window, translations);
        foreach (var child in EnumerateDescendants(window))
            TranslateElement(child, translations);
    }

    private static void TranslateElement(DependencyObject element, IReadOnlyDictionary<string, string> translations)
    {
        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Text = Translate(textBlock.Text, translations);
                break;
            case System.Windows.Controls.Button button when button.Content is string content:
                button.Content = Translate(content, translations);
                break;
            case MenuItem menuItem when menuItem.Header is string header:
                menuItem.Header = Translate(header, translations);
                break;
            case TabItem tabItem when tabItem.Header is string header:
                tabItem.Header = Translate(header, translations);
                break;
            case ComboBoxItem comboItem when comboItem.Content is string content:
                comboItem.Content = Translate(content, translations);
                break;
            case System.Windows.Controls.Label label when label.Content is string content:
                label.Content = Translate(content, translations);
                break;
        }

        if (element is FrameworkElement frameworkElement)
        {
            if (frameworkElement.ToolTip is string tooltip)
                frameworkElement.ToolTip = Translate(tooltip, translations);
            if (frameworkElement.Tag is string tag)
                frameworkElement.Tag = Translate(tag, translations);
        }
    }

    private static string Translate(string? value, IReadOnlyDictionary<string, string> translations) =>
        value is not null && translations.TryGetValue(value, out var translation) ? translation : value ?? string.Empty;

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var count = root is Visual or Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
                yield return descendant;
        }
    }
}
