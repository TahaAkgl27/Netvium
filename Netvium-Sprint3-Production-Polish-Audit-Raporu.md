# Netvium Sprint 3 — Production Polish Runtime Audit

**Tarih:** 29 Haziran 2026  
**Kapsam:** Dashboard production polish (Sprint 3)  
**Build:** Debug `0 Error / 0 Warning` · Release `0 Error / 0 Warning`

---

## Özet

Sprint 3 kapsamında dashboard placeholder ve hardcoded alanlar canlı veriye bağlandı; AI V3 formatı, Agent Health, System Status bileşenleri, canlı grafik/zaman aralığı, footer sparkline, status bar ve snapshot trend analizi tamamlandı.

**Production Readiness: 88%**

---

## Panel Denetimi

| Alan | Durum | Veri Kaynağı |
|------|-------|--------------|
| ✔ Dashboard | Tamamlandı | Memory → `.nsnap` → minimum ServerInfo → live refresh |
| ✔ Header | Tamamlandı | `LiveDashboardMetrics` + KPI bar fill (canlı %) |
| ✔ Footer | Tamamlandı | Canlı metrik + `LiveMetricChartSession` sparkline |
| ✔ System Performance | Tamamlandı | Canlı oturum (CPU/RAM/DISK/NETWORK), 2+ sample sonrası grafik |
| ✔ System Status | Tamamlandı | `SystemStatusProbe` + `IntelligenceEngine` |
| ✔ Active Services | Tamamlandı | WMI `Win32_Service` (Dependencies kolonu yok) |
| ✔ Timeline | Tamamlandı | WMI event log + snapshot persist |
| ✔ AI | Tamamlandı | Rule Engine V3 + opsiyonel OpenAI; fallback korumalı |
| ✔ Snapshot | Tamamlandı | `.nsnap` + `SnapshotTrendAnalyzer` |
| ✔ Refresh | Tamamlandı | `DashboardRefreshLifecycle` — Güncel ✓, 3sn fade |
| ✔ Cache | Sadeleştirildi | Deep refresh `.ncache` yazımı kaldırıldı; `.nsnap` birincil |

---

## 1) System Performance

| Özellik | Durum |
|---------|-------|
| İlk açılış placeholder | `Canlı veri bekleniyor...` |
| 1 sample | `Canlı veri toplanıyor...` |
| 2+ sample | Gerçek grafik |
| Snapshot → grafik | **Kullanılmıyor** (yalnızca live session) |
| CPU / RAM / DISK / NETWORK | 4 polyline |
| Zaman aralığı (1s/6s/24s/7g) | `PerfChartTimeRange` filtreleme |
| Legend | Canlı serilerle uyumlu |

**Dosyalar:** `Helpers/LiveMetricChartSession.cs`, `MainWindow.xaml.cs` (`UpdatePerformanceChart`)

---

## 2) Footer Live Metrics

| Alan | Durum |
|------|-------|
| CPU / RAM / Disk metin | Canlı WMI lightweight |
| Network up/down | `NetworkThroughputTracker` |
| İlk sample | `Toplanıyor...` |
| Uptime | WMI `LastBootUpTime` |
| IP | WMI veya server config |
| Sparkline | `LiveCpuSparkline`, `LiveRamSparkline`, `LiveDiskSparkline` — canlı session |

---

## 3) Status Bar

| Alan | Davranış |
|------|----------|
| Latency | Ping ms — veri yoksa panel gizli |
| Protocol | Aktif RDP oturumu + profil — yoksa gizli |
| Encryption | CredSSP/TLS türetilmiş — yoksa gizli |
| Session Time | `RdpSessionViewModel/CleanSession diagnostics` — yoksa gizli |
| Connection | Seçili sunucu |
| AI | Panel durumu |

**Dosyalar:** `ViewModels/RdpSessionViewModel.cs`, `Rdp/Clean/*`, `MainWindow.xaml.cs` (`UpdateStatusBar`)

---

## 4) Agent Status

| Alan | Kaynak |
|------|--------|
| Health endpoint | `GET :9841/health` + `X-Netvium-Key` |
| Version | Agent JSON `version` |
| Last Heartbeat | Agent JSON `time` |
| Latency | HTTP round-trip |
| Status | Online / Warning (>500ms) / Offline |
| WMI fallback | Agent yoksa "WMI" etiketi |

**Dosyalar:** `Helpers/AgentHealthService.cs`, `Netvium.Agent/Program.cs`

---

## 5) System Status Bileşenleri

`SystemStatusProbe` ile üretilen canlı bileşenler:

- Firewall (`mpssvc`)
- Windows Defender (`WinDefend` / `MsMpSvc`)
- BitLocker (`Win32_EncryptableVolume`)
- Windows Update (`wuauserv`)
- Pending Restart (`Win32_ComputerSystem.RebootRequired`)
- Remote Registry
- SMB (`LanmanServer`)
- DNS (`Dnscache` / `DNS`)
- DHCP (`Dhcp`)
- Time Sync (`W32Time`)

**Dosya:** `Helpers/SystemStatusProbe.cs`

---

## 6) Active Services

- Dependencies placeholder (`"Check details..."`) kaldırıldı
- DataGrid'de Dependencies kolonu zaten yok — ek kolon eklenmedi

---

## 7) AI Smart Analysis V3

| Bölüm | Durum |
|-------|-------|
| SERVER ROLE | ✔ |
| HEALTH SCORE | ✔ |
| TOP RISKS | ✔ |
| PERFORMANCE | ✔ |
| SECURITY | ✔ |
| EVENT ANALYSIS | ✔ |
| PREDICTION | ✔ (kanıt varsa) |
| RECOMMENDED ACTIONS | ✔ |

- Rule Engine her zaman çalışır
- AI başarısız/kalitesiz → Rule Engine raporu
- Kalitesiz AI iyi cache'i ezmez (`IsQualityAiReport`, `MergeAiWithRuleReport`)
- "Muhtemelen" / "Kesin değil" AI prompt'ta izinli

**Dosyalar:** `Helpers/AiAnalysisFormatter.cs`, `Helpers/InsightEngine.cs`

---

## 8) Snapshot Trend

`SnapshotTrendAnalyzer` + `PersistentServerSnapshot.Trends`:

- CPU / RAM / Disk trend
- Event trend
- Risk trend
- Health trend

Trend verisi AI payload'a dahil edilir; grafikte kullanılmaz.

---

## 9) Cache Cleanup

| Katman | Durum |
|--------|-------|
| Memory `_serverHistory` | Oturum geçmişi (trend için) |
| WMI memory cache | Dedup (okuma) |
| `.ncache` legacy | Deep refresh yazımı **kaldırıldı** |
| `.nsnap` persistent | Birincil disk yazımı |

---

## 10) Refresh

- Progress %99'da takılmaz (`LiveRefreshIndicatorController`)
- Tamamlanınca `Güncel ✓` → 3 sn → fade out
- Sunucu değişince `Cancel` + generation invalidation

---

## Hardcoded Kalan Alanlar

| Alan | Not |
|------|-----|
| AI Side Panel "Screen Reading" | Bilinçli BETA stub — dashboard dışı |
| Perf buton etiketleri `1s/6s/24s/7g` | UI kısaltması; arka planda saniye pencereleri (60s–7g) |
| Agent event log mining stub | Agent timeline geliştirmesi Sprint 4 |

---

## Placeholder Kalan Alanlar

| Alan | Not |
|------|-----|
| Grafik 0–1 sample | Bilinçli UX metni (boş panel değil) |
| Network ilk sample | `Toplanıyor...` (bilinçli) |
| AI panel Screen Reading | YAKINDA |

---

## Runtime Veri Kaynakları

```
Sunucu seçimi
  → Memory cache / .nsnap / minimum
  → Phase 0: ping + RDP
  → Agent /metrics veya WMI
  → Agent /health (paralel)
  → LiveMetricSampler (5×2sn)
  → Deep: event log + AI + persist .nsnap + trends
```

---

## Eksikler (Sprint 4 Öncesi)

1. **Agent event log** — Agent `/metrics` timeline zenginleştirmesi
2. **7g grafik penceresi** — Yalnızca canlı oturum sample'ları; uzun dönem için ayrı trend grafiği düşünülebilir
3. **BitLocker / Pending Restart** — Bazı WMI ortamlarında `Bilinmiyor` dönebilir
4. **`.ncache` migrasyon** — Okuma hâlâ destekleniyor; tam kaldırma Sprint 4
5. **AI Screen Reading panel** — Ürün kararı bekliyor

---

## Sprint 4 Önerileri

1. Agent'a event log endpoint ekle; timeline tek kaynaktan beslensin
2. Trend grafiği paneli (snapshot history, live chart'tan ayrı)
3. Legacy `.ncache` yazımını tamamen deprecate et + migration UI
4. Dashboard'u UserControl'lere böl (MainWindow 7k satır → bakım)
5. Enterprise health SLA dashboard (multi-server aggregate)

---

## Build Doğrulama

```
dotnet build Netvium.sln -c Debug   → 0 Error, 0 Warning
dotnet build Netvium.sln -c Release → 0 Error, 0 Warning
```

---

## Yeni / Güncellenen Dosyalar

| Dosya | Açıklama |
|-------|----------|
| `Models/AgentHealthInfo.cs` | Agent health modeli |
| `Models/SnapshotTrendData.cs` | Trend modeli |
| `Helpers/AgentHealthService.cs` | `/health` client |
| `Helpers/SystemStatusProbe.cs` | WMI sistem durumu |
| `Helpers/SnapshotTrendAnalyzer.cs` | Trend hesaplama |
| `Helpers/LiveMetricChartSession.cs` | Zaman aralığı + network serisi |
| `Helpers/AiAnalysisFormatter.cs` | AI V3 bölümleri |
| `Helpers/InsightEngine.cs` | V3 prompt + parser |
| `Helpers/IntelligenceEngine.cs` | System status entegrasyonu |
| `Netvium.Agent/Program.cs` | Gelişmiş `/health` |
| `ViewModels/RdpSessionViewModel.cs` / `Rdp/Clean/*` | Session time + profil |
| `MainWindow.xaml` / `.xaml.cs` | UI wiring |
