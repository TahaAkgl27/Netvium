# Netvium Runtime Data Source Audit

**Tarih:** 26 Haziran 2026  
**Kapsam:** Dashboard (`SelectedServerPanel`) — tüm alanlar, veri kaynağı, kod referansı, hardcoded denetimi, runtime akışı, kod kalitesi  
**Build:** Debug `0 Error / 0 Warning` · Release `0 Error / 0 Warning`

---

## 1. Özet

Netvium dashboard verisi **4 katmanlı** bir pipeline üzerinden akar:

1. **Anında (Phase 0):** Bellek cache → `.nsnap` persistent snapshot → minimum `ServerInfo`
2. **Hızlı (Phase 1):** ICMP ping + TCP 3389 + Agent HTTP (`:9841/metrics`) → WMI fallback
3. **Canlı örnekleme:** `LiveMetricSampler` (5×2sn) → `GetLightweightMetricsAsync`
4. **Derin (Phase 2, +5sn):** Event log enrichment + AI analiz + health score persist

**Kaynak türleri:**

| Etiket | Anlam |
|--------|-------|
| **Live WMI** | `WmiCollector.GetSnapshotAsync` / `GetLightweightMetricsAsync` |
| **Live Ping** | `TryPingMsAsync` (ICMP) |
| **Live TCP** | `TryTcpOpenAsync` (RDP 3389) |
| **Live Agent** | `GET http://{ip}:9841/metrics` |
| **Snapshot** | `%LocalAppData%\Netvium\snapshots\*.nsnap` (`PersistentSnapshotStore`) |
| **Disk cache** | `%LocalAppData%\Netvium\cache\*.ncache` (`CacheManager`) |
| **Memory cache** | `_serverHistory`, `_lastSnapshot`, `_cachedAiInsightsByServer` |
| **Computed** | `ServerHealthEvaluator`, `InsightEngine`, `IntelligenceEngine`, `ServerRuleEngine` |
| **Hardcoded** | XAML varsayılanı veya kodda hiç güncellenmeyen alan |

---

## 2. Runtime Akışı

### 2.1 Sunucu seçimi → Dashboard açılışı

```
Tree / Favorite tıklama
    ↓
ShowSelectedServerOverview()                    [MainWindow.xaml.cs ~2880]
    ↓
ApplyDashboardFromCachedMetrics()               [~3088]
    ├─ Bellek: _serverHistory[localKey]
    ├─ PersistentSnapshotStore.LoadAsync()      → .nsnap
    └─ Yoksa: ApplyMinimumDashboardFromServer() [~3054]
    ↓
StartInsightsRefresh() → RefreshInsightsAsync() [~3960]
```

### 2.2 Canlı yenileme (RefreshInsightsAsync)

```
RefreshInsightsAsync()                          [~3960]
    ↓
Paralel: TryPingMsAsync + TryTcpOpenAsync + TryFetchSnapshotViaAgentAsync
    ↓
Phase 0: UpdateUiWithSnapshot(Placeholder)      [~4012] — yalnızca ping/RDP strip
    ↓
Agent başarısız → WmiCollector.GetSnapshotAsync(includeEventLogs:false) [~4023]
    ↓
Phase 1: UpdateUiWithSnapshot(Basic)            [~4046] — CPU/RAM/Disk/servisler
    ├─ UpdateLiveMetrics() → ApplyLiveDashboardMetricsUi()
    └─ StartLiveMetricSampling() → LiveMetricSampler (5× WMI lightweight)
    ↓
Phase 2: RunDeferredDeepPhaseAsync()            [~4054]
    ├─ TryDeferredEventLogEnrichmentAsync()     [~4077] — ikinci WMI bağlantısı
    ├─ UpdateUiWithSnapshot(Deep)               — health score, persist
    └─ TriggerAiAnalysis()                      [~5216]
```

### 2.3 Metrik → UI binding

```
WmiSnapshot
    ↓
LiveDashboardMetricsBuilder.FromSnapshot()      [Helpers/LiveDashboardMetricsBuilder.cs ~21]
    ↓
ApplyLiveDashboardMetricsUi()                   [MainWindow.xaml.cs ~3147]
    ├─ Header KPI (HeaderCpu/Ram/Disk)
    ├─ System Performance (IntelligenceCpu/Ram/Disk/Ping)
    ├─ Footer (LiveCpu/Ram/Disk/Network/Uptime/Ip)
    └─ LiveMetricChartSession → UpdatePerformanceChart()
```

### 2.4 AI Smart Analysis V2

```
WmiSnapshot + ping + RDP
    ↓
ServerAnalysisPipeline.Build()                  [Helpers/ServerAnalysisPipeline.cs ~11]
    ├─ IntelligenceEngine.GenerateIntelligence()  — rol, bileşenler
    └─ ServerRuleEngine.Evaluate()              — eşik kuralları
    ↓
InsightEngine.BuildRuleOnlyInsights()           [InsightEngine.cs ~256]
    → AiAnalysisFormatter.BuildRuleBasedReport()
    ↓
(Opsiyonel) InsightEngine.GetAiReportAsync()    — OpenAI/Azure API
    ↓
InsightEngine.MergeAiWithRuleReport()
    ↓
ApplySmartAnalysisInsights() → SmartInsightList
    ↓
Persist: snapshot.AiInsights + _cachedAiInsightsByServer (TTL 6h)
```

---

## 3. HEADER

### 3.1 Toolbar

| Alan | Kaynak | Kod | Durum | Eksik |
|------|--------|-----|-------|-------|
| Live Refresh Status | Computed (refresh lifecycle) | `LiveRefreshIndicatorController` ← `DashboardRefreshLifecycle` | Gerçek | Hayır |
| Live Refresh Progress | Computed | `LiveRefreshProgressFill` width binding | Gerçek | Hayır |
| Console / Quick Scan / Refresh / Connect / Disconnect | Hardcoded (XAML label) | `MainWindow.xaml` ~1288–1305 | UI sabit | Hayır |

### 3.2 Sunucu başlık kartı

| Alan | Kaynak | Kod | Durum | Eksik |
|------|--------|-----|-------|-------|
| **Server Name** | Server config (`connections.json`) | `ShowSelectedServerOverview` → `SetTextIfChanged(SelectedTitleText)` ~4266 | Gerçek | Hayır |
| **Status** | Computed: ping + RDP + WMI (`ServerHealthEvaluator`) | `UpdateUiWithSnapshot` ~4251; Phase-0: `"KAYITLI"` ~3011; minimum: `"BİLİNMİYOR"` ~3074 | Gerçek (canlı) / Geçici (cache) | Hayır |
| **Health Badge** (renk) | Computed `HealthBadge` enum | `GetBadgeBrush(badge)` ~4248 | Gerçek | Hayır |
| **OS** | Live WMI `Win32_OperatingSystem.Caption` | `WmiCollector.CollectOsAndRamInfo` ~355 → `UpdateIntelligenceUi` ~4815 | Gerçek | Evet — Agent path OS göndermez |
| **IP** | Server config + WMI `IPAddresses` | `LiveDashboardMetricsBuilder.ResolveIpAddress` ~87 → `ApplyLiveDashboardMetricsUi` ~3276 | Gerçek | Hayır |
| **Last Login** | Live WMI `Win32_NetworkLoginProfile.LastLogon` | `WmiCollector.CollectLastUserInfo` ~572 → `UpdateUiWithSnapshot` ~4272 | Gerçek | Evet — WMI izin / profil yoksa `"-"` |
| **Active User** | Live WMI sessions (`Win32_LogonSession` + `Win32_LoggedOnUser`) | `ActiveSessionCollector` / `UpdateUiWithSnapshot` ~4275–4280 | Gerçek | Evet — oturum yoksa `"Yok"` |
| **Uptime** | Live WMI `Win32_OperatingSystem.LastBootUpTime` | `CollectOsAndRamInfo` → `FormatUptimePair` ~100 | Gerçek | Evet — WMI yoksa `"—"` |
| **Agent Status** | Derived: `snap.Connected` (WMI veya Agent başarısı) | `UpdateDashboardAgentStatus` ~4819 | Kısmen yanıltıcı — gerçek Netvium Agent durumu değil, veri toplama başarısı | Evet — Agent-specific health yok |
| Agent yeşil nokta | Hardcoded XAML | `MainWindow.xaml` ~1338 (`#22C55E`) | Sabit renk | Evet — Offline'da da yeşil kalır |
| **Health Score** | Computed `InsightEngine.CalculateRiskScore` | `CalculateHealthScore` ~5521 → `HealthScoreValueText` ~4351 | Gerçek (Deep tier) | Evet — Basic/Phase-0'da gösterilmez |
| **Critical Summary** (`HealthSummaryText`) | Computed badge + ping/RDP + timestamp | `UpdateUiWithSnapshot` ~4264; stale banner ~3038 | Gerçek | Hayır |
| **Health Breakdown** | Computed `CalculateHealthScore().Breakdown` | `HealthBreakdownText` ~4352 (Collapsed) | Gerçek | Evet — UI'da gizli |
| **Active Users Badge** | WMI `snap.ActiveUsers` | `UpdateServicesList` ~4720 | Gerçek | Evet — Collapsed varsayılan |
| **CPU (header)** | Live WMI `Win32_Processor.LoadPercentage` veya Agent `cpuPct` | `ApplyLiveDashboardMetricsUi` ~3155–3160 | Gerçek | Evet — `MonitorCpu=false` ise güncellenmez |
| **RAM (header)** | Live WMI OS memory veya Agent `ramUsedPct` | `ApplyLiveDashboardMetricsUi` ~3174–3179 | Gerçek | Evet — `MonitorRam=false` |
| **DISK (header)** | Live WMI `Win32_LogicalDisk` veya Agent `diskUsedPct` | `ApplyLiveDashboardMetricsUi` ~3193–3206 | Gerçek | Evet — `MonitorDisk=false` |
| Header KPI mini-bar (iç Border) | Hardcoded | `MainWindow.xaml` ~1356–1375 `Width="40"` | Dekoratif placeholder | Evet — metriğe bağlı değil |

---

## 4. System Performance

| Alan | Kaynak | Kod | Durum | Eksik |
|------|--------|-----|-------|-------|
| Bölüm başlığı / alt başlık | Hardcoded XAML | `MainWindow.xaml` ~1395–1398 | Sabit metin | Hayır |
| **CPU %** | Live WMI / Agent / Snapshot | `IntelligenceCpuVal` ← `ApplyLiveDashboardMetricsUi` ~3158 | Gerçek | Evet — offline `"—"` |
| **CPU Cores hint** | Live WMI processor count | `SelectedCpuCountText` ← `UpdateUiWithSnapshot` ~4268 | Gerçek | Evet — yoksa `"-"` |
| **RAM %** | Live WMI / Agent | `IntelligenceRamVal` ~3177 | Gerçek | Evet |
| **RAM size hint** | Live WMI `TotalVisibleMemorySize` | `SelectedRamSizeText` ~4269 | Gerçek | Evet — yoksa `"-"` |
| **DISK %** | Live WMI / Agent | `IntelligenceDiskVal` ~3196 | Gerçek | Evet |
| **Latency** | Live ICMP ping | `IntelligencePingVal` ~3220 | Gerçek | Evet — ping kapalıysa `"N/A"` |
| **Latency status** | Computed eşik (<50 Ultra Stable, <150 Good) | `IntelligencePingStatus` ~3221 | Gerçek | Evet |
| **Chart (CPU/RAM line)** | Live lightweight WMI poll (5 sample) | `LiveMetricSampler` → `UpdatePerformanceChart` ~5352 | Gerçek (kısa pencere) | Evet — 10sn veri, tarihsel değil |
| Chart placeholder | UI state metni | `UpdatePerformanceChartPlaceholder` ~4605 | Placeholder metin | Hayır (bilinçli) |
| Perf range butonları (1s/6s/24s/7g) | Hardcoded — yalnızca aktif stil | `OnPerfRange*Click` ~4862 | **Implement edilmedi** — chart filtresi yok | **EVET** |
| Chart legend (CPU/RAM/Disk/Network) | Hardcoded XAML | `MainWindow.xaml` ~1455 | Sabit | Evet — Disk/Network çizgisi yok |
| Legacy progress bars | Aynı metrik (gizli) | `IntelligenceCpuBar/RamBar/DiskBar` ~3161 | Gerçek ama Collapsed | Hayır |

---

## 5. System Status (`IntelligenceComponentsList`)

Kaynak: `IntelligenceEngine.GenerateIntelligence()` → `UpdateIntelligenceUi` ~4792  
WMI girdileri: `snap.Services` (`Win32_Service`), `snap.ListenPorts`, `snap.Sessions`, `snap.SharedFolders`, `snap.DiskTotalGb`, `snap.TopProcesses`

| Satır / Bileşen | Algılama mantığı | WMI / Servis | Snapshot cache | Durum | Eksik |
|-----------------|------------------|--------------|----------------|-------|-------|
| **Windows OS Core** | `snap.Connected == true` | Bağlantı başarısı | `.nsnap` + memory | Gerçek (bağlantı proxy) | Evet — her zaman "Aktif" yazılır |
| **TCP/IP Stack** | `snap.IPAddresses.Any()` | `Win32_NetworkAdapterConfiguration` ~399 | Cached | Gerçek | Evet — IP yoksa satır yok |
| **WinRM Management** | `WinRM` servisi Running | `Win32_Service` Name=WinRM | Cached services | Gerçek | Evet — servis kapalıysa satır yok |
| **RDP Access** | `TermService` Running | `Win32_Service` + session count | Cached | Gerçek | Evet — TermService her Windows'ta var |
| **IIS Web Server** | `W3SVC` Running | `Win32_Service` | Cached | Gerçek | Evet — IIS yoksa satır yok |
| **SQL Server** | MSSQLSERVER / MSSQL$ / port 1433 / sqlservr process | Service + `ListenPorts` + `Win32_PerfFormattedData` | Cached | Gerçek | Evet |
| **Active Directory** | `NTDS` Running veya port 389/3268 | `Win32_Service` + ports | Cached | Gerçek | Evet |
| **RDS Farm Node** | TermService + (UmRdpService+SessionEnv veya >1 session) | `Win32_Service` + sessions | Cached | Gerçek (heuristic) | Evet — false positive riski |
| **Dosya Paylaşımı** | `LanmanServer` + shared folders | `Win32_Share` ~174 + `LanmanServer` | Cached | Gerçek | Evet |
| **Yüksek Kapasiteli Depolama** | `DiskTotalGb > 800` | `Win32_LogicalDisk` aggregate | Cached | Gerçek (eşik tabanlı) | Evet — 800GB sabit eşik |
| **Web Uygulaması** | IIS var, RDS/AD/SQL yok | Heuristic | Cached | Computed | Evet |
| **Firewall** | — | — | — | **System Status'ta YOK** | **EVET** — yalnızca AI SECURITY bölümünde |
| **Antivirus** | — | — | — | **System Status'ta YOK** | **EVET** — AI SECURITY'de `WinDefend`/`MsMpSvc` |
| **Remote Registry** | — | — | — | **System Status'ta YOK** | **EVET** — AI SECURITY'de |

Offline fallback: `IntelligenceEngine.cs` ~12–22 — `"Bilinmiyor / Çevrimdışı"` + boş components.

Cache-first: `ApplyPersistentSnapshotToUi` → `snapshot.SystemStatusComponents` (önceden persist edilmişse regenerate atlanabilir).

---

## 6. Active Services

| Alan | Kaynak | Kod | Durum | Eksik |
|------|--------|-----|-------|-------|
| **Liste (Display)** | Live WMI `Win32_Service.DisplayName` | `WmiCollector.CollectServicesInfo` ~658 → `UpdateServicesList` ~4716 | **Gerçek servis** — örnek veri değil | Evet — WMI fail → boş |
| **Status (State)** | Live WMI `Win32_Service.State` | Aynı | Gerçek | Evet |
| **Startup (StartMode)** | Live WMI `Win32_Service.StartMode` | Aynı | Gerçek | Evet |
| **View All** | UI-only: grid `MaxHeight` toggle 176↔360 | `OnViewAllServicesClick` ~4838 | UI davranışı — veri değişmez | Hayır |
| Agent path servisleri | Agent JSON `services[]` | `TryFetchSnapshotViaAgentAsync` | Gerçek (agent kuruluysa) | Evet — StartMode agent'tan gelmez |
| `MonitorServices=false` | Hardcoded tek satır | `UpdateServicesList` ~4718 `"Disabled"` | Ayar kapalı placeholder | Bilinçli |
| Filtre mantığı | Kritik + rol + durmuş (non-Disabled) | `WmiCollector` ~662–720, max 300 servis | Gerçek filtre | Hayır |
| Dependencies kolonu | Hardcoded string | `ServiceRow.Dependencies = "Check details..."` ~707 | **Placeholder** | **EVET** |

**Kritik servis listesi (WMI):** `wuauserv`, `mpssvc`, `TermService`, `RemoteRegistry`, `WinRM`, `LanmanServer`, `Dnscache`, `Dhcp`, `EventLog`, `SamSs`, `W3SVC`, `IISADMIN`, `MSSQLSERVER`, `SQLBrowser`, `SQLWriter`, `NTDS`, `IsmServ`, `Kdc`, `Netlogon`, `DNS`, `UmRdpService`, `SessionEnv`, `SmtpSvc`

---

## 7. Recent Events

| Alan | Kaynak | Kod | Durum | Eksik |
|------|--------|-----|-------|-------|
| **Event listesi** | Live WMI `Win32_NTLogEvent` (System 7036/1074/6005 + Security 4624 + RDP) | `WmiCollector.CollectTimelineEvents` ~1319 | **Gerçek Event Viewer** | Evet — izin/security log kapalıysa boş |
| Deferred enrichment | İkinci WMI bağlantısı | `TryEnrichEventLogsAsync` ~4091 | Gerçek | Evet — +5sn gecikme |
| **Snapshot cache** | `snap.Timeline` / `.nsnap` | `ApplyDashboardPhase0Ui` ~3020 | Cached gerçek veri | Hayır |
| Phase-0 placeholder | Hardcoded bilgi mesajı | ~3024 `"Detaylı analiz 5 sn sonra..."` | Placeholder | Bilinçli |
| Minimum dashboard placeholder | Hardcoded | ~3081 `"Canlı analiz başlatıldı."` | Placeholder | Bilinçli |
| Boş event fallback | Hardcoded stabil mesaj | `UpdateTimeline` ~4750 | Placeholder (WMI başarılı ama event yok) | Evet |
| Agent path | Stub mesaj | Agent: event log WMI-only | Placeholder | Evet |
| `MonitorEventLogs=false` | Panel gizlenir | `UpdateIntelligenceUi` ~4812 | Ayar | Hayır |
| Pagination | Client-side | `ShowTimelinePage` | Gerçek | Hayır |

---

## 8. AI Smart Analysis

### 8.1 V2 Bölümleri (`SmartInsightList`)

| Bölüm | Kaynak | Motor | AI mı? | Placeholder? | Kod |
|-------|--------|-------|--------|--------------|-----|
| **SERVER ROLE** | WMI servis/port/process heuristic | `IntelligenceEngine` + `ServerAnalysisPipeline` | Hayır (rule) | Hayır | `AiAnalysisFormatter` ~51–57 |
| **CRITICAL FINDINGS** | Eşik kuralları | `ServerRuleEngine.Evaluate` | Hayır | Kısmen — bulgu yoksa bölüm yok | `ServerRuleEngine.cs` ~10–57 |
| **PERFORMANCE** | CPU/RAM/Disk/Latency metrikleri | `AiAnalysisFormatter.BuildPerformance` | Hayır | Hayır | `AiAnalysisFormatter.cs` ~119 |
| **SECURITY** | Servis durumları + RDP TCP | `BuildSecurity` (mpssvc, WinRM, RDP, SMB, ports) | Hayır | Hayır | ~133–145 |
| **PREDICTION** | Disk trend (history ≥2 sample) | `BuildDiskTrendPrediction` | Hayır | Evet — `"Tahmin üretilemedi."` varsayılan | `ServerAnalysisPipeline` ~80 |
| **RECOMMENDED ACTIONS** | Rule findings → action | `BuildActionsFromFindings` | Hayır | Evet — boşsa bölüm yok | `AiAnalysisFormatter` ~148 |
| **EVIDENCE** | Metrik + skor + event özeti | `BuildEvidence` | Hayır | Hayır | ~163 |
| AI overlay (tüm bölümler) | OpenAI/Azure JSON | `InsightEngine.GetAiReportAsync` | **Evet** | API fail → rule fallback | `InsightEngine.cs` ~262 |
| Merge | AI + rule enrichment | `MergeAiWithRuleReport` | Hybrid | Hayır | ~330 |
| Cache TTL | 6 saat | `SnapshotTtl.AiAnalysis` | — | — | `SnapshotSectionTimestamps` |
| Empty state | Hardcoded | `SmartAnalysisNormalText` | — | Placeholder metin | XAML ~1702 |

### 8.2 Eski IntelligenceEngine bölümleri (cache-first / merge)

`UpdateIntelligenceUi` ~4796–4805, `IntelligenceEngine.SmartInsights`:
- SERVER ROLE, AI INSIGHTS, INFRASTRUCTURE FINDINGS, ACTIONABLE RECOMMENDATIONS

Bu bölümler `TriggerAiAnalysis` çalışınca V2 formatıyla **değiştirilir**. Cache'ten yüklenirken hangi format olduğu persist zamanına bağlıdır.

### 8.3 Risk / Health input

| Alan | Kaynak | Kod |
|------|--------|-----|
| Health Score (AI context) | `InsightEngine.CalculateRiskScore` | `CalculateHealthScore` ~5523 |
| Risk Score | `ServerRiskEngine.ComputeRiskScore` | `ServerRuleEngine.cs` ~73 |
| Rule Engine | Eşik: disk>80, ram>90, cpu>85, latency>50, firewall off, stopped critical | `ServerRuleEngine.cs` ~19–55 |

---

## 9. General Information

| Alan | Binding / Control | Kaynak | Kod | Durum | Eksik |
|------|-------------------|--------|-----|-------|-------|
| **OS** | `ElementName=SelectedOsText` | WMI `OSName` | `UpdateIntelligenceUi` ~4815 | Gerçek | Evet — `"Unknown OS"` fallback |
| **Memory** | `ElementName=SelectedRamSizeText` | WMI `TotalRamGb` | `UpdateUiWithSnapshot` ~4269 | Gerçek | Evet |
| **CPU Cores** | `ElementName=SelectedCpuCountText` | WMI `CPUCoreCount` | ~4268 | Gerçek | Evet |
| **Uptime** | `ElementName=DashUptimeText` | WMI `LastBootUpTime` | `ApplyLiveDashboardMetricsUi` ~3272 | Gerçek | Evet |
| **IP** | `ElementName=SelectedDnsNameText` | Server + WMI IP | ~3278 | Gerçek | Hayır |
| **Last User** | `SelectedLastUserText` | WMI `Win32_ComputerSystem.UserName` / explorer / sessions | `CollectLastUserInfo` ~524 | Gerçek | Evet |

Gizli alanlar: `SelectedCompatibilityText` (WMI `OSArchitecture`), `SelectedLastSessionText`, `IpAddressesList` — güncellenir ama UI'da Collapsed.

---

## 10. Footer

### 10.1 Alt metrik kartları (`LiveMetricsRow`)

| Alan | Kaynak | Kod | Durum | Eksik |
|------|--------|-----|-------|-------|
| **CPU** | WMI / Agent / Snapshot | `LiveCpuText` ~3160 | Gerçek | Evet |
| **RAM** | WMI / Agent | `LiveRamText` ~3179 | Gerçek | Evet |
| **DISK** | WMI / Agent | `LiveDiskText` ~3198 | Gerçek | Evet |
| **NETWORK ↑↓** | Live WMI perf counter delta | `NetworkThroughputTracker` + `Win32_PerfRawData_Tcpip_NetworkInterface` | Gerçek (2. sample sonrası) | Evet — ilk sample `"Toplanıyor..."` |
| **UPTIME** | WMI boot time | `LiveUptimeText` / `LiveUptimeDetailText` ~3272 | Gerçek | Evet |
| **IP ADDRESS** | WMI / server config | `LiveIpText` ~3277 | Gerçek | Evet |
| Footer sparkline polylines | Hardcoded `Points` | `MainWindow.xaml` ~1950–2020 | **Dekoratif placeholder** | **EVET** |
| Quick Actions (Restart/Shutdown/Ping) | WMI remote (`Win32_OperatingSystem`, ping) | Click handlers | Gerçek aksiyon | Evet — izin gerekir |

### 10.2 App status bar (`MainStatusBarBorder`)

| Alan | Kaynak | Kod | Durum | Eksik |
|------|--------|-----|-------|-------|
| **Connection** | `_selectedServer` | `ApplyLiveDashboardMetricsUi` ~3280 | Gerçek | Hayır |
| **Latency** | — | `StatusLatencyText` XAML ~2155 | **Hardcoded `"Latency: —"` — hiç güncellenmiyor** | **EVET** |
| **Protocol** | Hardcoded | `"TLS 1.3"` ~2159 | Placeholder | **EVET** |
| **Encryption** | Hardcoded | `"AES-256 Passwordleme"` ~2163 | Placeholder | **EVET** |
| **Session** | Hardcoded | `"Oturum: 00:00:00"` ~2167 | **Hiç güncellenmiyor** | **EVET** |
| **AI indicator** | UI toggle `_isAiPanelOpen` | ~6822 | UI state | Hayır |

---

## 11. Hardcoded Denetimi (Proje Taraması)

### 11.1 Anahtar kelime araması

| Pattern | Sonuç |
|---------|-------|
| TODO / FIXME | **0** eşleşme (`.cs` kaynak) |
| Mock / Fake / Dummy / Demo / Coming Soon | **0** eşleşme (`.cs`) |
| TEMP | Atomic write `.tmp` dosyaları; `CONNECT_TEMPORARY` Win32 sabiti — placeholder değil |
| Sample | `MetricSample`, `LiveMetricSampler`, debug log — canlı örnekleme, mock değil |
| Test | `SettingsViewModel` Test Notification; `SnapshotInspectRunner` CLI — bilinçli |
| Placeholder | `DashboardUpdateTier.Placeholder`, `RdpPlaceholder`, `PerformanceChartPlaceholder` — UI tier/kontrol adları |

### 11.2 Fallback string envanteri (yüksek sinyal)

| Değer | Dosya(lar) | Kullanım |
|-------|------------|----------|
| `"—"` / `"-"` | `MainWindow.xaml.cs`, `LiveDashboardMetricsBuilder.cs:103`, `WmiCollector.cs` | Metrik yok fallback |
| `"Unknown"` / `"Unknown OS"` | `WmiCollector.cs:675`, `MainWindow.xaml.cs:4815`, `ServerAnalysisPipeline.cs:56` | Servis/OS bilinmiyor |
| `"N/A"` | `MainWindow.xaml.cs:3229`, `LiveDashboardMetricsBuilder.cs:120`, `ServerModels.cs:68` | Ping/throughput yok |
| `"Henüz bilinmiyor"` | `MainWindow.xaml.cs:2996,3067` | OS cache yok |
| `"KAYITLI"` / `"BİLİNMİYOR"` | `ApplyDashboardPhase0Ui`, `ApplyMinimumDashboardFromServer` | Geçici durum metinleri |
| `"Check details..."` | `WmiCollector.cs:707` | Service Dependencies placeholder |
| `"TLS 1.3"` / `"AES-256"` | `MainWindow.xaml:2159–2163` | Status bar sabit |
| `"Active"` (agent dot) | XAML sabit yeşil + `DashAgentStatusText` | Yanıltıcı agent göstergesi |
| `new List<>()` boş | Timeline/session minimum dashboard | Bilinçli boş state |
| `Enumerable.Empty` | Minimal kullanım | — |

### 11.3 Bilinçli stub

| Dosya | Satır | Not |
|-------|-------|-----|
| `Rdp/ExternalMstscFallback.cs` | 12–15 | `_logger` placeholder yorumu |
| `MainWindow.xaml` | 2101 | `AiScreenStatusText`: `"Bu özellik henüz geliştiriliyor."` |

---

## 12. Eksik Veri Denetimi (Panel Bazlı)

| Panel | Boş kalabilecek alanlar | Sebep |
|-------|-------------------------|-------|
| **Header** | OS, Last Login, Active User, Uptime, Health Score | WMI izin; Agent OS göndermez; Deep tier bekler |
| **Header** | Agent Status doğruluğu | `Connected` ≠ Netvium Agent health |
| **System Performance** | Tüm metrikler | WMI offline; monitor ayarları kapalı |
| **System Performance** | Chart | Lightweight poll 10sn; sunucu değişince sıfırlanır |
| **System Performance** | Perf range | **Kod eksikliği** — filtre implement edilmedi |
| **System Status** | Firewall, AV, Remote Registry | **Kod eksikliği** — `IntelligenceEngine`'de yok |
| **Active Services** | Tüm liste | WMI fail; `MonitorServices=false` |
| **Active Services** | Dependencies | Hardcoded placeholder |
| **Recent Events** | Gerçek eventler | Security log izni; `MonitorEventLogs=false`; Agent stub |
| **AI Smart Analysis** | PREDICTION | Yetersiz history (<2 disk sample) |
| **AI Smart Analysis** | AI bölümleri | API key yok; premium değil; cooldown |
| **General Info** | RAM, cores, last user | İlk WMI turu öncesi |
| **Footer** | Network throughput | İlk WMI sample; perf counter yok |
| **Footer status bar** | Latency, Session | **Kod eksikliği** — hiç bağlanmamış |

---

## 13. Kod Kalitesi

### 13.1 Tekrarlayan veri çekimleri

| Sorun | Konum | Etki |
|-------|-------|------|
| Full WMI + 5× Lightweight WMI | `RefreshInsightsAsync` ~4023 + `LiveMetricSampler` ~36 | Aynı Win32 sınıfları 6+ kez sorgulanır |
| Event log ikinci bağlantı | `GetSnapshotAsync(includeEventLogs:false)` + `TryEnrichEventLogsAsync` ~4091 | Gereksiz çift WMI scope |
| `GetLightweightMetricsAsync` cache bypass | `WmiCollector.cs` ~481 | Her poll yeni WMI bağlantısı |
| `LoadLegacyWmiSnapshotAsync` çift okuma | `PersistentSnapshotStore` ~273 + `WmiCollector.LoadFromDiskAsync` | CacheManager 2× |
| Startup preload + refresh overlap | `StartupSnapshotPreloadAsync` + `RefreshInsightsAsync` | Paralel WMI riski |

### 13.2 Duplicate cache

| Katman | Path | Format |
|--------|------|--------|
| Memory | `WmiCollector._cache` | `WmiSnapshot` |
| Disk cache | `%LocalAppData%\Netvium\cache\` | `.ncache` |
| Persistent | `%LocalAppData%\Netvium\snapshots\` | `.nsnap` |

Aynı `WmiSnapshot` verisi üç yerde tutulabilir. Key şemaları farklı: `{ip}|{username}` vs SHA256(`ServerSnapshotKey`).

### 13.3 Duplicate model / engine

| Overlap | Dosyalar |
|---------|----------|
| `WmiSnapshot` vs `LiveDashboardMetrics` | `RemoteDataModels.cs` vs `LiveDashboardMetrics.cs` |
| `IntelligenceInsight` vs `SmartInsight` vs `AnalysisFinding` vs `AiFindingItem` | 4 paralel insight modeli |
| `IntelligenceEngine` vs `InsightEngine` vs `ServerRuleEngine` | Rol/heuristic vs AI vs eşik kuralları |
| `IntelligenceEngine.SmartInsights` vs `AiAnalysisFormatter` V2 | Farklı bölüm isimleri, merge karmaşıklığı |

### 13.4 Gereksiz / eksik Registry

Registry okuması dashboard metriklerinde **kullanılmıyor**. Tüm veri WMI + ping + TCP + Agent HTTP.

---

## 14. Sonuç

### 14.1 Gerçek çalışan alanlar

- **Ağ erişimi:** Ping latency, RDP TCP 3389, health badge (`ServerHealthEvaluator`)
- **WMI metrikleri:** CPU, RAM, Disk, uptime, OS, RAM size, CPU cores, IP listesi
- **WMI servisleri:** Active Services listesi (filtrelenmiş gerçek `Win32_Service`)
- **WMI oturumlar:** Active User, Last User, session tabanlı RDP göstergesi
- **WMI event log:** Recent Events (System + Security, deferred enrichment)
- **Canlı chart:** CPU/RAM kısa pencere (lightweight poll)
- **Network throughput:** Perf counter delta (2. sample sonrası)
- **System Status:** Rol tabanlı bileşenler (IIS, SQL, AD, RDS, WinRM, RDP, File)
- **AI Smart Analysis V2:** Rule engine bölümleri (SERVER ROLE, CRITICAL, PERFORMANCE, SECURITY, EVIDENCE)
- **AI overlay:** OpenAI/Azure API (ayar + premium + API key ile)
- **Snapshot persist:** `.nsnap` cache-first dashboard yükleme
- **Health Score:** Deep tier'da risk skoru + breakdown

### 14.2 Placeholder / Hardcoded alanlar

| Alan | Tür |
|------|-----|
| Header KPI mini-bar genişlikleri | Dekoratif sabit |
| Agent yeşil nokta (XAML) | Her zaman yeşil |
| `DashAgentStatusText` etiketi | WMI success proxy (gerçek agent health değil) |
| Perf range butonları (1s/6s/24s/7g) | UI-only, veri filtresi yok |
| Chart Disk/Network legend | Çizgi yok |
| Footer sparkline polylines | Sabit Points |
| `StatusLatencyText` | Hiç güncellenmiyor |
| `StatusProtocolText` / `StatusEncryptionText` | Sabit TLS/AES metni |
| `StatusSessionText` | Sabit `00:00:00` |
| Service `Dependencies` kolonu | `"Check details..."` |
| Timeline placeholder mesajları | Phase-0 / minimum / boş fallback |
| `SmartAnalysisNormalText` | Empty state metni |
| `AiScreenStatusText` | "henüz geliştiriliyor" |
| Phase-0 `"KAYITLI"` status | Geçici cache göstergesi |

### 14.3 Sprint 3 öncesi tamamlanması gereken eksikler

1. **Status bar canlı veri:** `StatusLatencyText` ← ping; `StatusSessionText` ← oturum süresi; protokol/şifreleme gerçek RDP durumundan veya kaldırılmalı
2. **Agent Status doğruluğu:** Netvium Agent `:9841/health` ayrı kontrol; yeşil nokta `Connected`'a bağlanmalı
3. **System Status genişletme:** Firewall (`mpssvc`), Antivirus (`WinDefend`), Remote Registry — `IntelligenceEngine` components'e ekle
4. **Perf range implementasyonu:** Chart'a zaman filtresi veya butonları kaldır
5. **WMI çift-fetch optimizasyonu:** Lightweight poll'u full snapshot sonrası throttle; event log tek bağlantıda
6. **Cache birleştirme:** `cache\` vs `snapshots\` tek kaynak stratejisi; `LoadLegacyWmiSnapshotAsync` çift okuma kaldır
7. **AI bölüm tutarlılığı:** `IntelligenceEngine.SmartInsights` vs V2 `AiAnalysisFormatter` tek format
8. **Chart tamamlama:** Disk/Network serileri veya legend'dan kaldır
9. **Service Dependencies:** WMI `Win32_Service` dependency query veya kolonu gizle
10. **PREDICTION:** Daha uzun history persist (disk trend için ≥2 günlük sample)

---

## 15. Build Sonucu

```
dotnet build -c Debug
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s)
```

**Not:** Debug build, `Netvium.exe` çalışırken dosya kilidi nedeniyle copy adımında başarısız olabilir; derleme (`Netvium.dll`) başarılıdır.

---

## 16. Dosya Referans Özeti

| Dosya | Rol |
|-------|-----|
| `MainWindow.xaml` | Dashboard UI tanımları, x:Name kontroller |
| `MainWindow.xaml.cs` | Orchestration: `ShowSelectedServerOverview`, `RefreshInsightsAsync`, `UpdateUiWithSnapshot`, `ApplyLiveDashboardMetricsUi`, `TriggerAiAnalysis` |
| `Helpers/WmiCollector.cs` | WMI toplama: OS, CPU, RAM, disk, services, sessions, events, network |
| `Helpers/LiveDashboardMetricsBuilder.cs` | Snapshot → UI metrics model |
| `Helpers/LiveMetricSampler.cs` | 5×2sn lightweight WMI poll |
| `Helpers/IntelligenceEngine.cs` | Rol tespiti + System Status components |
| `Helpers/ServerRuleEngine.cs` | Eşik kuralları (disk, ram, cpu, firewall, latency) |
| `Helpers/ServerAnalysisPipeline.cs` | AI context builder |
| `Helpers/InsightEngine.cs` | AI API + risk score + merge |
| `Helpers/AiAnalysisFormatter.cs` | V2 bölüm formatı |
| `Helpers/ServerHealthEvaluator.cs` | Health badge hesabı |
| `Services/PersistentSnapshotStore.cs` | `.nsnap` persist/load |
| `Helpers/CacheManager.cs` | Legacy `.ncache` |
| `Models/RemoteDataModels.cs` | `WmiSnapshot`, `ServiceRow`, `TimelineEvent`, `SmartInsight` |

---

*Rapor üretim yöntemi: kaynak kod statik analizi, `MainWindow.xaml` / `.xaml.cs` orchestration izleme, `WmiCollector` / `IntelligenceEngine` / `AiAnalysisFormatter` satır bazlı inceleme, proje geneli pattern grep, Debug + Release build doğrulaması.*
