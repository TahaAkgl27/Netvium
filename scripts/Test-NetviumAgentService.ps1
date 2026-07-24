[CmdletBinding()]
param([string]$BaseUri = 'http://127.0.0.1:9841')
$ErrorActionPreference = 'Stop'
$health = Invoke-WebRequest -Uri ($BaseUri.TrimEnd('/') + '/health') -Method Get -TimeoutSec 5 -SkipHttpErrorCheck
if ($health.StatusCode -notin @(200,401)) { throw "Agent health beklenmeyen HTTP durumu: $($health.StatusCode)" }
Write-Output "Agent health endpoint erişilebilir (HTTP $($health.StatusCode)); secret bu scriptte taşınmıyor."