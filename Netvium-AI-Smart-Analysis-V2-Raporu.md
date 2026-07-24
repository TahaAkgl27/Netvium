# Netvium AI Smart Analysis V2 Raporu

**Sprint:** Netvium Sprint 3 – AI Smart Analysis V2  
**Tarih:** 26 Haziran 2026  
**Build:** Debug 0 Error / 0 Warning · Release 0 Error / 0 Warning

---

## 1. Özet

AI Smart Analysis paneli tamamen yeniden tasarlandı. Mevcut XAML/UI düzeni korundu; yalnızca panel içeriği ve analiz motoru değiştirildi. Yeni özellik eklenmedi; snapshot sistemi ve performans hedefleri korundu.

Temel ilke: **AI hiçbir zaman boştan yorum üretmez.** Önce Netvium kendi deterministik motorunu çalıştırır; AI yalnızca kanıtlı teknik veriyi yorumlar.

---

## 2. Yeni AI Mimarisi

```
Raw Data (WMI Snapshot + Ping + History)
        ↓
ServerAnalysisPipeline.Build()
        ↓
Rule Engine (ServerRuleEngine)
        ↓
Risk Engine (ServerRiskEngine)
        ↓
AiAnalysisFormatter (7 bölümlü rapor)
        ↓
[Opsiyonel] OpenAI — yalnızca yapılandırılmış JSON
        ↓
MergeAiWithRuleReport (AI + kural birleştirme)
        ↓
SmartInsight listesi → UI + Snapshot
```

### Katmanlar

| Katman | Dosya | Görev |
|--------|-------|-------|
| Context Builder | `Helpers/ServerAnalysisPipeline.cs` | WMI, servis, olay, rol, ağ verisini `ServerAnalysisContext`'e dönüştürür |
| Rule Engine | `Helpers/ServerRuleEngine.cs` | Eşik tabanlı kanıtlı bulgular üretir |
| Risk Engine | `Helpers/ServerRuleEngine.cs` (`ServerRiskEngine`) | Risk skoru ve Top 5 bulgu seçimi |
| Formatter | `Helpers/AiAnalysisFormatter.cs` | 7 sabit bölümü `SmartInsight` kartlarına çevirir |
| AI Gateway | `Helpers/InsightEngine.cs` | Prompt oluşturma, API çağrısı, JSON parse, kalite filtresi |
| UI Orchestration | `MainWindow.xaml.cs` | TTL, cache-first, arka plan yenileme |

---

## 3. Rule Engine Akışı

`ServerRuleEngine.Evaluate()` aşağıdaki kuralları uygular:

| Kural | Eşik | Severity |
|-------|------|----------|
| Disk kullanımı | > %80 | Critical |
| RAM kullanımı | > %90 | Critical |
| CPU kullanımı | > %85 | Warning |
| Latency | > 50 ms | Warning |
| Kritik servis durdurulmuş | Her biri | Critical |
| Firewall devre dışı | mpssvc stopped | Critical |
| RDP açık | TCP 3389 | Info |
| WinRM aktif | Running | Info |
| Remote Registry durdurulmuş | Stopped | Warning |
| Antivirus yok | WinDefend/MsMpSvc | Warning |
| Son Critical/Error olay | Event log | Critical |

Bulgular `AnalysisFinding` olarak `ServerAnalysisContext.RuleFindings` ve `TopFindings` (max 5) alanlarına yazılır. AI prompt'una **RULE ENGINE FINDINGS** ve **TOP FINDINGS** blokları olarak eklenir.

### Disk trend tahmini (kural tabanlı)

Geçmiş snapshot'larda disk örnekleri varsa ve son değer > %75 ile artış ≥ %3 ise:

> *"Disk doluluk eğilimi devam ederse 2-3 hafta içinde kapasite sorunu oluşabilir."*

Kanıt: disk örnek serisi (`%78 → %81 → %83`). Kanıt yoksa **PREDICTION** bölümü tamamen gizlenir.

---

## 4. Prompt Yapısı

AI'ya yalnızca `ServerAnalysisPipeline.BuildAiPayloadJson()` ile üretilen teknik veri gönderilir:

- OS, CPU, RAM, Disk, Latency, Network, Uptime
- Running / Stopped / Critical Services
- Installed Roles, WinRM, RDP, Firewall, Antivirus
- Remote Registry, SMB, Last User, Open Ports
- Recent Event Logs
- Health Score, Risk Score
- Detected Server Role + kanıt
- Rule Engine bulguları (kanıtlı)
- Top Findings

Prompt (`InsightEngine.BuildAiV2Prompt`):

- Türkçe, somut, kanıtlı JSON çıktı zorunlu
- Yasak ifadeler: *it appears, probably, maybe, muhtemelen, belki*
- `response_format: json_object`, `temperature: 0.2`
- Tahmin yalnızca trend kanıtı varsa `available: true`

### JSON şema

```json
{
  "server_role": { "role": "...", "confidence": 92, "evidence": ["..."] },
  "critical_findings": [{ "text": "...", "evidence": "..." }],
  "performance": [{ "text": "...", "evidence": "..." }],
  "security": [{ "text": "...", "evidence": "...", "risk_level": "Critical|Warning|Info" }],
  "prediction": { "available": true/false, "text": "...", "evidence": "..." },
  "recommended_actions": [{ "priority": "HIGH|MEDIUM|LOW", "text": "...", "evidence": "..." }],
  "evidence": [{ "metric": "CPU", "value": "28%" }]
}
```

Kalite kontrolü: `IsQualityAiReport()` — en az 3 bölüm, belirsiz dil yok, bulgularda kanıt zorunlu. Kalitesiz AI yanıtında kural tabanlı rapor korunur.

---

## 5. Panel Çıktısı (7 Bölüm)

Sabit başlıklar (`SmartInsight.IsSectionHeader = true`):

1. **SERVER ROLE** — rol, confidence %, kanıt (IIS, SQL, AD DS vb.)
2. **CRITICAL FINDINGS** — max 5 madde, her biri kanıtlı
3. **PERFORMANCE** — CPU/RAM/Disk/Latency değerlendirmesi
4. **SECURITY** — RDP, WinRM, Firewall, Remote Registry, SMB, kullanıcı, portlar
5. **PREDICTION** — yalnızca kanıt varsa gösterilir
6. **RECOMMENDED ACTIONS** — HIGH / MEDIUM / LOW öncelik
7. **EVIDENCE** — metrik tabanlı kanıt listesi

Boş bölümler gizlenir. Yeterli veri yoksa ilgili bölüm render edilmez.

---

## 6. Snapshot Entegrasyonu

- `BuildPersistentSnapshot()` tüm V2 panel içeriğini `AiInsights` listesine yazar (`Section`, `SortKey`, `IsSectionHeader` dahil).
- `SectionTimestamps.AiAnalysisUtc` AI cache zaman damgasını tutar.
- Snapshot dosyası: `%LocalAppData%\Netvium\snapshots\*.nsnap` (DPAPI şifreli).

Dashboard açılışında `ApplyPersistentSnapshotToUi()` snapshot'taki AI sonucunu **anında** gösterir; kullanıcı bekletilmez.

---

## 7. TTL Davranışı

| Parametre | Değer |
|-----------|-------|
| TTL | 6 saat (`SnapshotTtl.AiAnalysis`) |
| TTL geçerli | Snapshot + bellek cache kullanılır; AI API çağrılmaz |
| TTL dolmuş | Mevcut cache anında gösterilir; arka planda `RequestSmartAnalysis()` tetiklenir |
| Manuel yenile | `OnRefreshAiAnalysisClick` → `force: true` → TTL bypass |

Akış:

1. Kural motoru sonuçları anında panelde gösterilir (⚙️ ikon).
2. AI çağrısı arka planda çalışır; tamamlanınca 🤖 ile zenginleştirilmiş rapor yazılır.
3. Sonuç `CacheAiInsights()` → snapshot'a persist edilir.

---

## 8. Performans Etkisi

| Senaryo | Davranış | Etki |
|---------|----------|------|
| İlk açılış (cache var) | Snapshot'tan okuma | ~0 ms ek gecikme |
| TTL geçerli | API atlanır | Ağ çağrısı yok |
| TTL dolmuş | Cache göster + arka plan AI | UI bloklanmaz |
| Rule engine | Senkron, hafif WMI verisi | < 5 ms |
| AI API | `Task.Run` + `ConfigureAwait(false)` | UI thread serbest |

Eski 60 sn session cooldown kaldırıldı; tek kaynak 6 saatlik snapshot TTL.

---

## 9. Runtime Akışı

```
Kullanıcı sunucu seçer
    → PersistentSnapshotStore.Load()
    → ApplyPersistentSnapshotToUi() [AI anında]
    → Refresh başlar
    → UpdateUiWithSnapshot() [metrikler güncellenir, eski Analyze() kaldırıldı]
    → Deep phase tamamlanınca:
        TTL geçerliyse → AI skip
        TTL dolmuşsa → RequestSmartAnalysis()
            → TriggerAiAnalysis()
                → ServerAnalysisPipeline.Build()
                → BuildRuleOnlyInsights() [anında UI]
                → GetAiReportAsync() [arka plan, premium+API key]
                → MergeAiWithRuleReport()
                → CacheAiInsights() + PersistFullSnapshotAsync()
```

Premium olmayan veya AI kapalı kullanıcılar: yalnızca kural motoru V2 paneli görür.

---

## 10. Değişen Dosyalar

| Dosya | Durum | Açıklama |
|-------|-------|----------|
| `Models/ServerAnalysisModels.cs` | **Yeni** | Context, Finding, AI rapor DTO'ları |
| `Helpers/ServerRuleEngine.cs` | **Yeni** | Rule + Risk engine |
| `Helpers/ServerAnalysisPipeline.cs` | **Yeni** | Context builder, AI payload, disk trend |
| `Helpers/AiAnalysisFormatter.cs` | **Yeni** | 7 bölüm formatter |
| `Helpers/InsightEngine.cs` | **Değişti** | V2 prompt, parse, merge, kalite kuralları |
| `Models/RemoteDataModels.cs` | **Değişti** | `SmartInsight`: Section, SortKey, IsSectionHeader |
| `MainWindow.xaml.cs` | **Değişti** | TriggerAiAnalysis V2, cache/snapshot, TTL |
| `Models/SnapshotSectionTimestamps.cs` | Mevcut | `AiAnalysis = 6h` (önceden tanımlı) |

**Değişmeyen:** `MainWindow.xaml` (UI tasarımı), snapshot store altyapısı, refresh lifecycle.

---

## 11. Manuel Test Senaryoları

### T1 — Cache-first açılış
1. Daha önce analiz edilmiş sunucuyu seç.
2. **Beklenen:** 7 bölümlü panel anında görünür; "Yenileniyor" yok (TTL geçerliyse).

### T2 — TTL dolmuş arka plan yenileme
1. Snapshot'ta `AiAnalysisUtc` > 6 saat önce olan sunucuyu aç.
2. **Beklenen:** Eski analiz anında; kısa süre sonra kural motoru + AI güncellemesi.

### T3 — Rule engine önceliği
1. Disk > %80 veya kritik servis durdurulmuş sunucu.
2. **Beklenen:** CRITICAL FINDINGS'te kanıtlı madde; AI beklemeden görünür.

### T4 — AI kapalı / Premium yok
1. AI ayarını kapat veya free hesap.
2. **Beklenen:** ⚙️ ikonlu kural tabanlı 7 bölüm; API çağrısı yok.

### T5 — Yetersiz veri
1. WMI bağlantısı olmayan sunucu.
2. **Beklenen:** Mevcut ağ verisiyle kısıtlı rapor; bilinmeyen alanlarda "Yeterli veri bulunamadı" veya bölüm gizleme.

### T6 — Manuel Yenile
1. AI panelinde "Yenile" butonuna bas.
2. **Beklenen:** TTL bypass; kural motoru + AI yeniden çalışır.

### T7 — Sunucu farklılaşması
1. Web sunucu vs SQL sunucu karşılaştır.
2. **Beklenen:** SERVER ROLE ve CRITICAL FINDINGS sunucuya özgü (IIS vs SQL Agent vb.).

### T8 — Snapshot persist
1. Analiz sonrası uygulamayı kapat/aç.
2. **Beklenen:** Aynı V2 panel snapshot'tan geri yüklenir.

### T9 — PREDICTION gizleme
1. Disk trend verisi olmayan sunucu.
2. **Beklenen:** PREDICTION bölümü görünmez.

### T10 — Kalite filtresi
1. AI belirsiz veya eksik JSON döndürürse.
2. **Beklenen:** Kural tabanlı rapor korunur; generic metin gösterilmez.

---

## 12. Build Sonucu

```
dotnet build Netvium.csproj -c Debug   → 0 Error / 0 Warning
dotnet build Netvium.csproj -c Release → 0 Error / 0 Warning
```

---

## 13. Kısıtlara Uyum

| Kural | Durum |
|-------|-------|
| UI tasarımı korundu | ✅ XAML değişmedi |
| Yeni özellik eklenmedi | ✅ Mevcut panel içeriği yeniden yapılandırıldı |
| Performans düşmedi | ✅ Cache-first + arka plan AI |
| Snapshot bozulmadı | ✅ AiInsights genişletildi, geriye uyumlu RuleInsights fallback |
| Kanıtsız yorum yok | ✅ Rule engine + AI kalite filtresi |
| Belirsiz dil yok | ✅ Yasak ifade listesi + kalite kontrolü |

---

*Rapor Netvium Sprint 3 AI Smart Analysis V2 implementasyonu sonrası oluşturulmuştur.*
