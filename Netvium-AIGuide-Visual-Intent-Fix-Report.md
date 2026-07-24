# Netvium AI Guide Görsel Niyet Düzeltmesi

## 1. Kök neden

Sorun OCR kalitesinden tek başına kaynaklanmıyordu. Akışta dört ayrı mimari hata vardı:

1. Kullanıcı soru sorarak yeni ekran yakalattığında `VisionRequest.UserPrompt` alanı `null` bırakılıyordu. İlk vision çağrısı kullanıcının ne aradığını bilmeden genel ekran özeti üretiyordu.
2. Gerçek PNG yalnızca ilk vision observation çağrısına gönderiliyordu. Kullanıcıya nihai cevabı üreten `ConfigurableGuidanceProvider` görüntüyü almıyor; yalnızca OCR ağırlıklı `VisionObservation` ve `ScreenVisualContext` JSON'u görüyordu.
3. Nihai prompt, workflow kararını değiştirilemez kabul ediyor ve modele yalnızca önceden belirlenmiş tek bir genel adımı yeniden yazdırıyordu. Bilinmeyen/SSMS benzeri ekranlarda bu adım “mevcut seçeneklerle devam edin” seviyesinde kalıyordu.
4. Formatter her cevapta sabit ekran, gözlem, manuel adım, önemli not, alternatif ve güven bölümlerini basıyor; confidence ve ham evidence normal sohbet balonunda gösteriliyordu.

## 2. Değiştirilen dosyalar

- `Services/AIGuide/AIGuideService.cs`
  - Güncel soru ilk vision analizine aktarılıyor.
  - Aynı güncel PNG, boyutları ve `ObserveOnly` modu guidance isteğine ekleniyor.
  - Güvenli/refusal durumları dışında görüntü mevcutsa image-first guidance kullanılıyor.
- `Models/AIGuide/GuidanceRequest.cs`
  - Bellek içi `ScreenshotImageBytes`, genişlik, yükseklik ve guide mode alanları eklendi.
- `Services/AIGuide/GuidanceEngineService.cs`
  - Görüntü safe request'e taşınıyor; vision provider/model/key seçiliyor.
- `Services/AIGuide/ConfigurableVisionProvider.cs`
  - İlk observation çağrısı kullanıcı sorusunu odak bağlamı olarak alıyor.
  - OCR artık görüntünün yerine geçen authoritative kaynak değil, destekleyici metin kanıtı.
- `Services/AIGuide/ConfigurableGuidanceProvider.cs`
  - Nihai cevap çağrısı multimodal yapıldı.
  - Tam çözünürlüklü PNG `data:image/png;base64,...`, `detail=high` olarak gönderiliyor.
  - Soru, görüntü, uygulama, pencere başlığı, konuşma, OCR ve capabilities ayrı alanlara ayrıldı.
- `Models/AIGuide/GuidanceResponse.cs` ve `Services/AIGuide/GuidanceResponseParser.cs`
  - Niyet odaklı kompakt cevap sözleşmesi eklendi: doğrudan cevap, en fazla beş adım, alternatif, görünürlük ve yalnızca gerekirse güvenlik notu.
- `Services/AIGuide/VisualQuestionIntentClassifier.cs`
  - Uygulamadan bağımsız locate-control, error-explanation ve visible-action niyetleri eklendi.
- `Services/AIGuide/VisualGuidanceResponseGuard.cs`
  - OCR satır dökümü, fazla adım ve “tıkladım/açtım/yaptım” gibi işlem yapılmış iddiaları filtreleniyor.
- `Services/AIGuide/AIGuideSecretRedactor.cs`
  - Connection string, parola/token, e-posta, GUID ve aktivasyon kodu maskelemesi genişletildi.
- `MainWindow.xaml.cs`
  - Confidence ve visible evidence normal sohbetten kaldırıldı; yalnızca AI Guide capture diagnostics açıksa gösteriliyor.
- `Netvium.AIGuide.Tests/VisualGuidanceContractTests.cs`
  - Sekiz yeni görsel rehber sözleşme testi eklendi.

## 3. Görselin modele gönderildiğine dair kod kanıtı

`AIGuideService`, güncel `_currentScreen.ImageBytes` değerini `GuidanceRequest.ScreenshotImageBytes` alanına koyuyor. `GuidanceEngineService` bu alanı safe request'e aynen taşıyor. `ConfigurableGuidanceProvider.BuildRequestPayloadJson`, byte dizisini Base64 PNG'ye çevirip kullanıcı mesajına aşağıdaki multimodal parçayı ekliyor:

```text
type = image_url
url = data:image/png;base64,...
detail = high
```

Capture katmanı görüntüyü PNG olarak bellekte tutuyor; guidance aşamasında ilave JPEG sıkıştırması veya küçültme uygulanmıyor. Loglara Base64, görüntü içeriği veya API anahtarı yazılmıyor.

## 4. Yeni prompt ve bağlam önceliği

Yeni kesin öncelik:

1. `UserQuestion`
2. Gerçek `Screenshot/Image`
3. `ActiveApplication` ve `WindowTitle`
4. `RecentConversation`
5. `OCRText` ve workflow safety hints

Ek alanlar:

- `GuideMode = ObserveOnly`
- `AvailableCapabilities`
- `DetectedQuestionIntent`
- `VisionObservation`

Workflow çıktısı artık kullanıcının sorusunu ve görüntüyü bastıran authoritative tek cevap değil; yalnızca daha düşük öncelikli güvenlik/iş akışı ipucudur. Refusal, clarification ve wrong-screen güvenlik kararları yerel katmanda korunur.

## 5. Eklenen testler

1. “Yeni sorgu nerden yazcam?”: image payload, soru önceliği ve kompakt New Query yönlendirme sözleşmesi.
2. “Bu hata ne?”: hata açıklama niyeti, muhtemel neden ve güvenli kontrol direktifi.
3. “Neye basacağım?”: okunmayan confirmation butonlarını uydurmama.
4. Crop dışındaki araç çubuğu: tek alternatif yol ve görünürlük notu.
5. Gürültülü SQL OCR: `dbo.*`, `NULL`, tablo/hücre dökümünü kullanıcı cevabından çıkarma.
6. Observe Only: “tıkladım/açtım/çalıştırdım” iddialarını engelleme.
7. Hassas ekran: parola, token, connection string, e-posta, GUID ve aktivasyon kodunu maskeleme.
8. Hatalı/doğal Türkçe: “nerden yazcam” ifadesini uygulamaya özel hard-code olmadan locate-control niyetine çevirme.

## 6. Build ve test sonuçları

- Yeni görsel rehber testleri: **8/8 geçti**.
- AI Guide ilgili paket, lisans yazma testleri hariç: **86/86 geçti**.
- Tam AI Guide paketi: **102 geçti, 2 başarısız, toplam 104**.
  - Eski offline lisans testleri FAZ 6E kapsamında production dışı bırakılmıştır; aktif entitlement testleri account plan kaynağını doğrular.
  - Yerel `license.dat` artık aktif entitlement kaynağı değildir ve gerçek kullanıcı dosyasına dokunulmaz.
  - Lisans kodu bu görevde değiştirilmedi.
- `Netvium.sln` build: **0 hata, 0 uyarı**.
- Test restore aşamasında restricted ağ nedeniyle NuGet vulnerability feed'e erişilemedi ve mevcut `NU1900` uyarısı görüldü; paket derleme/test çalışmasını engellemedi.

## 7. Örnek eski ve yeni cevap

Eski davranış:

```text
Geçerli ekran:
10.4002 dbo.tb_KitapTalep dbo.tb_KitapTur ...

Manuel adımlar:
1. Mevcut ekranda görünen seçeneklerle devam edin.

Güven: Düşük
```

Yeni hedef davranış:

```text
SQL Server Management Studio’da yeni sorgu yazmak için üst araç çubuğundaki “New Query / Yeni Sorgu” düğmesini kullanın.

1. Üst araç çubuğunda “New Query” düğmesini seçin.

Alternatif: Object Explorer’da ilgili veritabanına sağ tıklayıp “New Query” seçeneğini açın.

Görünürlük notu: Üst araç çubuğu görüntüde kısmen kırpılmışsa düğme görünmeyebilir.
```

## 8. Kalan riskler

- Görsel cevabın doğruluğu seçilen vision modelinin kalitesine ve API erişimine bağlıdır.
- Gerçek canlı RDP yeniden oynatımı yapılmadı; ekli görsel AI Guide cevap panelini gösteriyor ve bu ortamda kullanıcının canlı RDP oturumu/vision API anahtarı yoktu. Multimodal payload otomatik test ile doğrulandı.
- Ekran görüntüsü doğal olarak ekrandaki verileri içerir ve vision sağlayıcısına gönderilir. Mevcut entitlement, vision-enable ayarı ve API anahtarı sınırları korunmuştur; görüntü veya Base64 loglanmaz.
- Çok küçük ya da fiziksel olarak kırpılmış kontroller model tarafından okunamayabilir. Yeni prompt bu durumda görünmezlik notu ve açıkça inferred alternatif yol ister.
