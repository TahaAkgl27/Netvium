# Netvium Enterprise v1.0 — Controlled Pilot Manual Checklist

**Version:** Netvium Enterprise v1.0 Controlled Pilot  
**Target environment:** Small/medium IT, **1–50 servers**  
**Tester:** ______________________ **Date:** __________  

Mark each item: **PASS** | **FAIL** | **SKIP** (with reason)

---

## Pre-flight

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 0.1 | Install/build Release: `dotnet build Netvium.sln -c Release` | 0 errors, 0 warnings | | |
| 0.2 | Run automated helper: `dotnet run --project tools/Rc2Smoke/Rc2Smoke.csproj -c Release` | ALL PASS | | |
| 0.3 | Launch `bin/Release/net8.0-windows/Netvium.exe` | App starts without crash | | |
| 0.4 | Confirm .NET 8 Desktop Runtime on pilot machines | Runtime present | | |

---

## 1. Connection persistence

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 1.1 | Add a new server (name, IP, user, folder) | Server appears in tree | | |
| 1.2 | Edit server IP or name | Change visible immediately | | |
| 1.3 | Close app **immediately** after edit (within 1 second) | App exits cleanly | | |
| 1.4 | Reopen app | Edited values persisted in `connections.json` | | |
| 1.5 | Delete a test server, close immediately, reopen | Server removed permanently | | |

---

## 2. Widget layout persistence

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 2.1 | Select a server and open dashboard edit mode | Widgets draggable | | |
| 2.2 | Move or resize at least one widget | Layout changes on screen | | |
| 2.3 | Close app **immediately** after layout change | App exits cleanly | | |
| 2.4 | Reopen app, select same server | Widget positions/sizes match last edit | | |

---

## 3. RDP connect / disconnect / reconnect

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 3.1 | Connect to a pilot server via embedded RDP | Session opens, status shows connected | | |
| 3.2 | Disconnect from session | Returns to dashboard or placeholder | | |
| 3.3 | Reconnect to same server | Session establishes again | | |
| 3.4 | Open second server in new tab (if licensed) | Both tabs manageable | | |
| 3.5 | Close RDP tab, return to dashboard | Dashboard visible for selected server | | |

---

## 4. Dashboard telemetry recovery

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 4.1 | Select server, wait for metrics on dashboard | CPU/RAM/disk or status updates | | |
| 4.2 | Connect RDP to same server (background scans pause) | RDP usable | | |
| 4.3 | Disconnect RDP | Dashboard resumes periodic refresh | | |
| 4.4 | Confirm charts/metrics update within one refresh interval | No permanent "Waiting for telemetry" if data existed | | |
| 4.5 | Switch to another server and back | Metrics reload for each server | | |

---

## 5. Agent telemetry (DPAPI key file)

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 5.1 | On target server: `Netvium.Agent.exe --service --key-file "%ProgramData%\\Netvium\\Agent\\agent.key.dpapi"` | Agent listens on `127.0.0.1:9841` by default | | |
| 5.2 | In Netvium settings, set matching Agent Metrics Key | Key saved (DPAPI when encryption on) | | |
| 5.3 | Remote access is not enabled by default; verify loopback-only operation and document any separately approved transport design | No unintended remote exposure | | |
| 5.4 | Refresh dashboard for that server | Metrics source shows Agent / connected | | |
| 5.5 | Stop agent, refresh | Graceful fallback (see WMI test) | | |

---

## 6. WMI fallback telemetry

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 6.1 | Select server **without** agent running | WMI or cache path attempted | | |
| 6.2 | Valid admin credentials stored | Metrics or partial data appear | | |
| 6.3 | Invalid credentials / firewall block | Clear offline/unavailable state (not fake healthy data) | | |
| 6.4 | Agent unavailable, WMI works | Dashboard still usable via WMI | | |

---

## 7. Notifications

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 7.1 | Settings → Notifications → Test Notification | Toast/desktop notification appears | | |
| 7.2 | Trigger threshold alert (e.g. high CPU on test host) | Alert recorded | | |
| 7.3 | Click header bell (notifications) | Active alerts listed or "No active alerts" | | |
| 7.4 | Confirm no password/secret text in notification content | Safe messages only | | |

---

## 8. Global search

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 8.1 | Press **Ctrl+K** | Global search field focused | | |
| 8.2 | Type partial server name | Connection tree filters | | |
| 8.3 | Type IP fragment | Matching servers shown | | |
| 8.4 | Clear search | Full tree restored | | |

---

## 9. Sidebar search

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 9.1 | Type in sidebar "Ara..." box | Tree filters by name/IP/folder | | |
| 9.2 | Empty folders hidden while filtering | Only matching servers/folders shown | | |
| 9.3 | Clear sidebar search | Full tree restored | | |

---

## 10. Settings — dark-only verification

| # | Step | Expected result | Result | Notes |
|---|------|-----------------|--------|-------|
| 10.1 | Open Settings → Appearance | Theme shows **Dark** only | | |
| 10.2 | No Light or System option available | Cannot select broken light theme | | |
| 10.3 | UI remains consistent dark palette after save/restart | No washed-out mixed theme | | |

---

## Sign-off

| Role | Name | Signature | Date |
|------|------|-----------|------|
| Pilot lead | | | |
| IT operations | | | |

**Pilot batch result:** _____ / _____ tests passed  

**Recommendation:** GO / NO-GO / GO WITH CONDITIONS  

**Conditions (if any):**  

_______________________________________________________________

## FAZ 6E architecture checks

- Confirm Premium/Premium+ comes only from the authenticated account plan; local `license.dat` is not an entitlement source.
- Confirm active RDP uses `Rdp/Clean/*` and legacy RDP classes are not present in the release assembly.
- Plaintext Agent key arguments are unsupported; use the protected key-file workflow.