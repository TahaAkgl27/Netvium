# Netvium Dashboard V1.0 - Sprint 3 Runtime Validation & Hardening Report

## Scope

Validated and hardened the completed dashboard widgets against common Windows runtime failures:

- WMI permission errors
- Missing WMI namespaces/classes
- RPC unavailable / firewall restrictions
- Provider load failures
- Empty WMI results
- Slow or hanging queries
- Non-fatal widget data failures

No new widgets were added. The dashboard layout engine and UI design were not changed.

## Hardening Applied

- Added per-collector timeout isolation in `WmiCollector` so one slow or unavailable WMI provider cannot block the full dashboard snapshot.
- Added timeout wrapping for `ActiveSessionCollector.GetSessionsAsync`.
- Added timeout wrapping for deferred Event Log enrichment.
- Added `EnumerationOptions.Timeout` through `CreateTimedSearcher` for the Sprint 2 provider-heavy queries.
- Classified and logged WMI failures with useful diagnostics:
  - Access denied
  - RPC unavailable
  - Invalid namespace
  - Provider load failure
  - Invalid class
  - Generic `ManagementException`
- Made `Win32_Share` collection non-fatal. A share query failure no longer marks the whole snapshot disconnected.
- Reduced duplicate WMI probing in `SystemStatusProbe` by reusing shared snapshot fields for BitLocker and pending restart.
- Confirmed infrastructure/security widgets have non-blank unavailable or empty states.

## Verification Summary

Runtime Verified with live remote customer server: NO

Reason: no remote Windows test target/credentials were provided in this session. Validation was performed by source audit, failure-path hardening, linter diagnostics, and a clean build.

Build verification:

- Command: `dotnet build "Netvium.csproj" -o "_build_check37"`
- Result: succeeded
- Warnings: 0
- Errors: 0

## Widget Runtime Matrix

### Performance

Widget: Performance  
Status: Hardened through shared snapshot collection and collector timeout isolation.  
Runtime Verified: NO  
Data Source: WMI CPU/RAM/Disk/Network snapshot and live chart session.  
Errors Found: A slow WMI collector could previously delay the grouped snapshot.  
Fixes Applied: Per-collector timeout isolation.  
Remaining Risk: Actual remote WMI counter availability still depends on target permissions/providers.  
Production Ready: NO until tested against a real target.

### System Status

Widget: System Status  
Status: Hardened.  
Runtime Verified: NO  
Data Source: shared snapshot services, BitLocker fields, Windows Update reboot state.  
Errors Found: Duplicate direct WMI probes could fail or slow down independently.  
Fixes Applied: Reused shared snapshot for BitLocker and pending restart before fallback probing.  
Remaining Risk: Service data depends on WMI service query access.  
Production Ready: NO until tested against a real target.

### Server Information

Widget: Server Information  
Status: Hardened through shared snapshot timeout isolation.  
Runtime Verified: NO  
Data Source: WMI OS, computer system, network adapter/configuration, disk, uptime.  
Errors Found: Slow WMI sub-query could delay all grouped metrics.  
Fixes Applied: Per-collector timeout isolation.  
Remaining Risk: Some fields may show `Not Available` if providers or permissions are missing.  
Production Ready: NO until tested against a real target.

### Recent Events

Widget: Recent Events  
Status: Hardened.  
Runtime Verified: NO  
Data Source: Windows Event Log WMI enrichment.  
Errors Found: Deferred Event Log enrichment lacked its own timeout wrapper.  
Fixes Applied: Added timeout wrapper and non-fatal fallback.  
Remaining Risk: Event Log access commonly requires permissions; widget will show empty/unavailable state.  
Production Ready: NO until tested against a real target.

### AI Smart Analysis

Widget: AI Smart Analysis  
Status: Hardened indirectly.  
Runtime Verified: NO  
Data Source: shared snapshot, health components, insight engine.  
Errors Found: Dependent snapshot failures could reduce available input.  
Fixes Applied: Snapshot collection is now more resilient to partial provider failures.  
Remaining Risk: AI quality depends on live snapshot completeness and optional external engine availability.  
Production Ready: NO until runtime verified.

### CPU / RAM / Disk / Network / Latency / IP Address / Uptime

Widget: Metric Widgets  
Status: Hardened through shared WMI collector.  
Runtime Verified: NO  
Data Source: shared WMI snapshot and live metric session.  
Errors Found: Individual provider hangs could delay snapshot completion.  
Fixes Applied: Per-collector timeout isolation.  
Remaining Risk: Some counters may be unavailable on restricted or older Windows targets.  
Production Ready: NO until tested against real targets.

### GPU

Widget: GPU  
Status: Hardened through collector timeout isolation.  
Runtime Verified: NO  
Data Source: `Win32_VideoController`.  
Errors Found: Provider/query failures were previously only local to GPU but could still consume collector time.  
Fixes Applied: Per-collector timeout isolation.  
Remaining Risk: Remote GPU data is often unavailable or incomplete on servers.  
Production Ready: NO until runtime verified.

### Processes

Widget: Processes  
Status: Hardened through shared snapshot timeout isolation.  
Runtime Verified: NO  
Data Source: WMI process collection.  
Errors Found: Slow process query could delay grouped snapshot.  
Fixes Applied: Per-collector timeout isolation.  
Remaining Risk: Process owner/CPU details may require elevated rights.  
Production Ready: NO until runtime verified.

### Servicesb
Remaining Risk: Session logoff requires privileges and target support.  
Production Ready: NO until runtime verified.

### Windows Updates

Widget: Windows Updates  
Status: Hardened.  
Runtime Verified: NO  
Data Source: `Win32_Service` and `StdRegProv` registry reads.  
Errors Found: Provider-specific errors needed better logging and timeout handling.  
Fixes Applied: Timed WMI searcher and classified provider failure logs.  
Remaining Risk: Pending update count remains `Not Available` unless a safe supported source is added.  
Production Ready: NO until runtime verified.

### Firewall

Widget: Firewall  
Status: Hardened.  
Runtime Verified: NO  
Data Source: `StdRegProv` firewall policy registry and `mpssvc` service status.  
Errors Found: Provider-specific errors needed better logging and timeout handling.  
Fixes Applied: Timed WMI searcher and classified provider failure logs.  
Remaining Risk: Registry provider can be blocked by policy/firewall.  
Production Ready: NO until runtime verified.

### BitLocker

Widget: BitLocker  
Status: Hardened.  
Runtime Verified: NO  
Data Source: `root\CIMV2\Security\MicrosoftVolumeEncryption`, `Win32_EncryptableVolume`.  
Errors Found: Missing namespace/provider could occur on unsupported editions.  
Fixes Applied: Timeout isolation, classified invalid namespace/provider logs, explicit empty state.  
Remaining Risk: Namespace may be absent or inaccessible remotely.  
Production Ready: NO until runtime verified.

### Windows Defender

Widget: Windows Defender  
Status: Hardened.  
Runtime Verified: NO  
Data Source: `root\Microsoft\Windows\Defender`, `MSFT_MpComputerStatus`, `MSFT_MpThreatDetection`.  
Errors Found: Defender namespace/provider may be absent, disabled, or permission-blocked.  
Fixes Applied: Timeout isolation, classified provider logs, explicit unavailable state.  
Remaining Risk: Third-party AV or server policies may disable Defender provider.  
Production Ready: NO until runtime verified.

### Security Score

Widget: Security Score  
Status: Hardened.  
Runtime Verified: NO  
Data Source: calculated from Firewall, BitLocker, Defender, Windows Update, critical services and health components.  
Errors Found: Score could be degraded by missing inputs.  
Fixes Applied: Missing data is handled as unavailable/penalty input, not fake healthy data.  
Remaining Risk: Score accuracy depends on runtime availability of upstream security providers.  
Production Ready: NO until runtime verified.

### Open Ports

Widget: Open Ports  
Status: Hardened.  
Runtime Verified: NO  
Data Source: `root\StandardCimv2`, `MSFT_NetTCPConnection`, `MSFT_NetUDPEndpoint`, process PID map.  
Errors Found: StandardCimv2 provider can be missing or blocked; query could be slow.  
Fixes Applied: Timed searchers, provider error classification, explicit empty/unavailable state.  
Remaining Risk: Older Windows versions or restricted remoting may not expose these classes.  
Production Ready: NO until runtime verified.

## Remaining Runtime Test Checklist

To mark widgets Production Ready, test against at least these targets:

- Healthy Windows Server with full WMI/DCOM access.
- Server with WMI access denied.
- Server with firewall/RPC blocked.
- Server without BitLocker namespace.
- Server with Defender disabled or third-party AV.
- Server with StandardCimv2 unavailable.
- Slow/high-latency server to confirm timeout behavior.

## Final Result

The dashboard is now safer under partial WMI failures: widgets should not crash, freeze the UI, or render blank panels when individual providers are missing or slow. Final production readiness still requires live runtime validation against real Windows servers.
