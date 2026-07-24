# Netvium Enterprise v1.0 — Known Limitations

**Applies to:** Netvium Enterprise v1.0 Controlled Pilot  
**Scope:** 1–50 servers, Windows desktop admin workstations  

This document defines intentional scope boundaries and accepted risks for the controlled pilot. Items listed here are not necessarily bugs—they are known constraints for v1.0.

---

## Scale and performance

| Limitation | Detail |
|------------|--------|
| **Not validated for 100+ servers** | Full-fleet scan, sidebar ping sweep, and monolithic `connections.json` rewrites scale O(N). Acceptable for 1–50 servers; degraded experience expected beyond that. |
| **Scan-all is sequential** | Manual "scan all" runs full refresh per server without batching. Can run for extended periods on large lists. |
| **No fleet-wide parallelism cap** | Sidebar status uses limited concurrency but still touches every server each interval. |

---

## Features disabled or incomplete

| Limitation | Detail |
|------------|--------|
| **AI free-form chat disabled** | AI panel chat input is read-only. No conversational backend in v1.0. |
| **Screen reading disabled** | AI "Understand Screen" and related beta features are not available. |
| **Light theme disabled** | Settings expose Dark theme only. Light/system theme was incomplete and removed from v1.0 to prevent broken UI. |
| **Premium gating is local** | `IsPremiumEnabled` is a settings flag, not external entitlement verification. |

---

## Security and credentials

| Limitation | Detail |
|------------|--------|
| **Agent TLS requires operator setup** | Client supports `AgentScheme=https` in settings, but the agent does not auto-provision certificates. TLS must be configured manually for encrypted transport. |
| **Agent default localhost-only** | Remote metrics require explicit `--bind`. This is intentional hardening; pilot operators must document bind policy. |
| **Agent secret provisioning** | Plaintext command-line and environment secrets are rejected. Service uses LocalMachine DPAPI key file; console uses CurrentUser DPAPI key file or bounded redirected stdin. |
| **PowerShell credentials in memory** | Remote console and shutdown paths may pass credentials via encoded PowerShell command lines or in-process strings. No plaintext temp `.ps1` files in v1.0, but memory/process visibility remains. |
| **HTTP default for agent** | Unencrypted HTTP is default. Warning logged once per session when HTTP is used. |
| **Settings secrets when encryption off** | If `EnableEncryption` is disabled, AI/agent keys may be stored plaintext in `settings.json`. Keep encryption enabled for pilot. |

---

## Monitoring and data

| Limitation | Detail |
|------------|--------|
| **WMI environment variance** | Defender, BitLocker, and other WMI namespaces may be unavailable on hardened or core installs. Partial dashboards are expected. |
| **Agent session detail** | Agent session idle/logon fields may show placeholders. |
| **Snapshot history in connections.json** | Snapshot history unencrypted unless `EncryptSnapshots` enabled (off by default). |
| **Cache size cap** | Legacy cache size enforcement may not cover all `.ncache` / `.nsnap` growth during long sessions without restart. |

---

## UI and accessibility

| Limitation | Detail |
|------------|--------|
| **Accessibility improvements planned post-v1.0** | No `AutomationProperties` on icon buttons; connection tree selection is mouse-primary; keyboard navigation incomplete. |
| **Runtime placeholder glyphs** | Some KPI tiles show `—` or `-` until first telemetry refresh. |
| **Close-to-tray debounced save** | Hiding to tray uses debounced saves; full flush occurs on actual exit. |
| **MainWindow monolith** | ~8700 lines code-behind; maintenance and regression cost remain high (see technical debt note). |

---

## Operational

| Limitation | Detail |
|------------|--------|
| **No automated test suite in solution** | Validation relies on Rc2Smoke helper + manual pilot checklist. |
| **Update check uses public GitHub** | May not match private deployment pipelines. |
| **Manual pilot tests required** | RDP, WMI, agent, and persistence on real hosts are not fully automated. |

---

## Out of scope for v1.0 (post-release)

- MainWindow decomposition into services  
- Fleet-scale scan batching and connection sharding  
- Full light theme  
- AI chat and screen reading  
- WCAG 2.x accessibility compliance  
- Agent automatic TLS provisioning  

See `Netvium-RC2-Technical-Debt.md` for maintainability roadmap.

## FAZ 6E architecture note (2026-07-23)

- Active entitlement is sourced from the authenticated account/plan returned by the direct SQL account service. A local `license.dat` file is not read for entitlement and cannot unlock Premium/Premium+.
- The active embedded RDP path is `Rdp/Clean/*` through `RdpSessionHostControl` and `RdpCleanSession`; legacy RDP sources are retained but excluded from production compilation.
- Agent remains loopback/local-only by default. Remote bind/TLS design and real Windows Service/WMI validation remain pilot work.
- RDP pixel parity on real targets remains a live-validation item.