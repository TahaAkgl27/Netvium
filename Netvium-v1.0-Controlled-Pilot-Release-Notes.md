# Netvium Enterprise v1.0 Controlled Pilot — Release Notes

**Version:** Netvium Enterprise v1.0 Controlled Pilot  
**Build:** .NET 8 WPF (`net8.0-windows`) + Netvium.Agent (`net8.0`)  
**Date:** July 2026  
**Status:** Ready for controlled pilot  

---

## Summary

Netvium Enterprise v1.0 Controlled Pilot is a Windows desktop RDP management and monitoring application for **small and medium IT environments** managing approximately **1–50 servers**. This release focuses on stability, trustworthy UI, secure defaults, and reliable persistence—not fleet-scale optimization or experimental AI chat.

---

## Target audience

- Internal IT teams and MSPs with modest server counts  
- Windows Server environments using RDP for daily administration  
- Pilots requiring WMI and/or optional Netvium.Agent metrics  

**Not intended for:** unmanaged deployments above ~50 servers, internet-exposed agent endpoints without TLS, or environments requiring full WCAG accessibility compliance in v1.0.

---

## What's included

### Core capabilities

- Multi-session embedded RDP with tab management  
- Connection tree with folders, favorites, and quick connect  
- Tiered dashboard telemetry (Agent → WMI → cache → persistent snapshot)  
- Widget-based customizable dashboard per server  
- Alert engine with in-app notification panel  
- Global search (`Ctrl+K`) and sidebar connection search  
- DPAPI-protected credentials and snapshot storage  
- Audit logging and structured application logs  

### Security hardening (RC2 / RC3)

- Immediate flush save on application close (connections, layout, settings)  
- Quick Ping command injection blocked via host validation  
- Agent defaults to **localhost bind** (`127.0.0.1`); remote bind requires explicit `--bind`  
- Configurable agent scheme/port (`http` / `https`) in settings  
- Agent shared secret never written to application logs  
- Remote session logoff validates `rwinsta` exit code  
- PowerShell remote console uses in-memory `-EncodedCommand` (no password temp scripts)  

### Pilot UX stabilization (RC3)

- Dead UI controls wired or clearly disabled  
- Dashboard telemetry recovery after RDP sessions  
- Turkish/English label encoding fixes  
- Dark theme only (production default)  
- Header shows Windows username instead of hardcoded identity  

---

## Known disabled in v1.0

| Item | Status |
|------|--------|
| AI free-form chat | **Disabled** — input read-only; use Risk Analysis (WMI-based) where Premium/API configured |
| Screen reading (AI panel) | **Not available** — marked BETA / YAKINDA |
| Light theme | **Disabled** — Dark only in settings |
| Large fleet (100+ servers) | **Not supported** — scan-all and sidebar ping scale linearly |

---

## Security notes for operators

1. **Agent default bind:** `Netvium.Agent` listens on `127.0.0.1:9841` (loopback/local-only). Remote bind and firewall/TLS operation are not part of the validated pilot path and require separate live validation.  
2. **Agent authentication:** Uses a DPAPI-protected key file (`--key-file`); plaintext command-line and environment secrets are not supported.  
3. **Transport:** Default client-agent communication is **HTTP**. Set `AgentScheme=https` in settings when TLS is configured on the agent side.  
4. **Secrets in logs:** Application logs do not include agent keys or connection passwords.  
5. **Remote admin:** PowerShell-based operations may still expose credentials in process memory or encoded command lines during execution—operate from trusted admin hosts only.  

### Starting the agent (pilot)

```text
Netvium.Agent.exe --service --key-file "%ProgramData%\\Netvium\\Agent\\agent.key.dpapi"
```

---

## System requirements

- Windows 10/11 or Windows Server 2019+ (admin workstation)  
- .NET 8 Desktop Runtime  
- Outbound RDP (3389), ICMP (optional), WMI/RPC for monitoring  
- Agent: .NET 8 Runtime on monitored servers (optional)  

---

## Installation (pilot)

1. Build or deploy Release output from `Netvium.sln`  
2. Run `Netvium.exe` from `bin/Release/net8.0-windows/`  
3. Complete settings (agent key, refresh interval, notifications)  
4. Deploy agent on pilot servers as needed  
5. Execute manual checklist: `Netvium-v1.0-Controlled-Pilot-Checklist.md`  

---

## Automated validation

```powershell
dotnet build Netvium.sln -c Release
dotnet run --project tools/Rc2Smoke/Rc2Smoke.csproj -c Release
```

Expected: build 0 errors / 0 warnings; Rc2Smoke **ALL PASS**.

---

## Support scope for pilot

- Bug reports with steps, logs from `%ProgramData%\Netvium\Logs\`  
- Pilot feedback on WMI coverage per server OS/version  
- Agent deployment questions (bind address, firewall rules)  

---

## Related documents

- `Netvium-v1.0-Controlled-Pilot-Checklist.md` — manual pilot tests  
- `Netvium-v1.0-Known-Limitations.md` — scope boundaries  
- `Netvium-RC2-Technical-Debt.md` — post-v1.0 refactor notes  

## FAZ 6E runtime surface

- Premium and Premium+ entitlement is account/plan based through the direct SQL account service. Offline `license.dat` is ignored and is not an activation source.
- The production RDP path is `Rdp/Clean/RdpCleanSession` + `RdpSessionHostControl`; legacy RDP classes are excluded from the release compilation while their source files remain retained.
- Identity API is not wired into the production WPF account flow yet.
- Current validation totals: Agent 9/9, Account 71/71, AIGuide 92/92 after legacy-only tests were excluded, Identity API 11/11. Real Windows Service, remote WMI, RDP pixel parity, and external mstsc validation remain required.