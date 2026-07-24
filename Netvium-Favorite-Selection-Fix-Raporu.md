# Netvium Favorite Selection Fix Raporu

**Tarih:** 26 Haziran 2026

---

## Build

| Konfigürasyon | Sonuç |
|---------------|-------|
| **Release** | 0 Error / 0 Warning |
| **Debug** | Derleme başarılı (Netvium.exe çalışırken kopya uyarısı — kod hatası yok) |

---

## Kök Neden

Toolbar yıldız butonu `GetQuickActionServer()` üzerinden **`_selectedNode` (tree seçimi)** veya **RDP oturumu** sunucusunu hedef alıyordu. Analiz dashboard'u ise `ShowSelectedServerOverview()` + `_insightsKey` ile farklı bir sunucuyu gösterebiliyordu.

Özellikle:
- `_activeDashboardServer` tanımlı değildi; dashboard hangi sunucuyu gösterdiği tek kaynakta tutulmuyordu
- `ShowSelectedServerOverview()` `_selectedServer` güncellemiyordu
- Favoriler listesine tıklanınca `ConnectToServer()` çağrılıyordu (RDP), dashboard + tree senkronu yoktu
- Tree focus ile dashboard sunucusu ayrışabiliyordu

Sonuç: Kullanıcı DC analiz ekranına bakarken yıldıza basınca tree'de seçili kalan 3CX favoriye eklenebiliyordu.

---

## Yapılan Fixler

### 1. Active Dashboard Server (`_activeDashboardServer`)

Tek kaynak: analiz panelinde gösterilen sunucu.

- `ShowSelectedServerOverview()` → set edilir
- `HideSelectedServerOverview()` / klasör seçimi → temizlenir
- `_selectedServer` ile senkron tutulur

### 2. Toolbar Favorite Target

```csharp
GetToolbarFavoriteTarget() → IsDashboardVisible() && _activeDashboardServer
OnToolbarToggleFavorite() → ToggleFavorite(GetToolbarFavoriteTarget())
```

- Analiz ekranı yoksa → yıldız **disabled**
- Klasör seçiliyse → disabled
- Ephemeral sunucu → disabled + tooltip: *"Geçici bağlantılar favorilere eklenemez."*

### 3. Tooltip & Toast (Türkçe)

- Tooltip: `"DC favorilere ekle"` / `"DC favorilerden çıkar"`
- Toast: `"DC favorilere eklendi"` / `"DC favorilerden çıkarıldı"`
- Context menu favorite → sağ tıklanan node (değişmedi)

### 4. Tree / Favorites Senkronizasyonu

- `SyncTreeSelectionToServer()` — dashboard değişince tree'de ilgili node seçilir (`TreeViewItem.IsSelected`)
- `OnFavoriteItemClick` → `ShowSelectedServerOverview()` (eskiden `ConnectToServer`)
- `ConnectionTreeNode.IsActiveDashboard` — aktif dashboard sunucusuna mavi sol border + arka plan

### 5. State Güncelleme Noktaları

`UpdateRdpToolbarState()` şu akışlarda çağrılır:
- Tree seçimi
- Dashboard aç/kapa
- Favorite toggle
- Favorites UI refresh
- Tab/session değişimi (`SwitchToTab`)
- Sunucu silme (`RefreshFavoritesUi`)

### 6. GetQuickActionServer İyileştirmesi

Dashboard görünürken quick action'lar (ping, restart vb.) de `_activeDashboardServer` kullanır — toolbar ile tutarlılık.

---

## Değişen Dosyalar

| Dosya | Değişiklik |
|-------|------------|
| `MainWindow.xaml.cs` | `_activeDashboardServer`, sync, toolbar target, favorites click |
| `MainWindow.xaml` | `IsActiveDashboard` görsel vurgusu |
| `Models/ConnectionTreeNode.cs` | `IsActiveDashboard` property |
| `ViewModels/RdpSessionToolbarViewModel.cs` | `IsFavoritesEnabled`, Türkçe tooltip |

---

## Test Senaryoları

| Senaryo | Beklenen | Durum |
|---------|----------|-------|
| DC seçili, yıldız bas | DC favoriye eklenir | Kod review ✓ |
| Tree 3CX focus, dashboard DC | Yıldız → DC | `GetToolbarFavoriteTarget` ✓ |
| Favorilerden 3CX tıkla | Dashboard 3CX, tree 3CX seçili | `ShowSelectedServerOverview` ✓ |
| Klasöre tıkla | Yıldız disabled | `ClearActiveDashboardServer` ✓ |
| Quick Connect ephemeral | Yıldız disabled | `IsEphemeral` guard ✓ |
| Sunucu sil | Favoriden kalkar | Mevcut `RemoveNodeInternal` ✓ |
| IP/port değiş | `MigrateFavoriteKey` | Mevcut ✓ |

*Manuel UI testi: uygulama yeniden başlatıldıktan sonra doğrulanmalıdır.*

---

## Kullanıcıya Not

Değişiklikleri görmek için Netvium'u kapatıp yeniden derleyin/başlatın.
