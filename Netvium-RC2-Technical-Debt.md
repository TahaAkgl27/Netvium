# Netvium RC2 — Post-Release Technical Debt

## MainWindow code-behind decomposition

`MainWindow.xaml.cs` (~8700 lines) mixes UI event wiring with business logic across RDP sessions, dashboard layout, storage, settings, notifications, and WMI monitoring.

**Recommended post-RC2 refactor (not a release blocker):**

Split responsibilities into focused services:

| Service | Responsibility |
|---------|----------------|
| `RdpSessionService` | Tab lifecycle, connect/disconnect, remote shell commands |
| `DashboardService` | Widget layout, workspace switching, snapshot binding |
| `ConnectionStorageService` | Load/save connections, favorites, folders |
| `SettingsService` (extend) | Apply runtime settings, tray/minimize behavior |
| `NotificationService` | Toasts, status strip, tray integration |
| `MonitoringService` | WMI/agent polling, health checks, insights refresh |

**Approach:** Extract one vertical slice at a time behind interfaces; keep XAML code-behind as thin event forwarders. Do not big-bang rewrite.

**RC3 note:** Search, notifications, telemetry recovery, encoding, and dark-only theme were stabilized without splitting MainWindow.

## FAZ 6E boundary (2026-07-23)

The production surface now uses account-plan entitlement and the Clean RDP engine only. Dormant offline licensing and legacy RDP sources remain on disk for rollback but are excluded from production compilation. MainWindow remains a large code-behind surface (approximately 9k lines) and is intentionally not refactored in this phase. Live Windows Service/WMI, RDP pixel parity, and transport validation remain open debt.