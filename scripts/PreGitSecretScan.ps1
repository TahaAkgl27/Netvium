[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [switch]$StagedOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Root).Path)
$excludedDirs = @('\bin\','\obj\','\artifacts\','\.git\','\.vs\','\TestResults\','\packages\')
$legacyPaths = @('Services\Licensing\','Services\LicensingUiState.cs','Models\LicenseRecord.cs','tools\SnapshotInspectRunner\','tools\RdpPixelCompareRunner\')
$textExtensions = @('.cs','.csproj','.props','.targets','.xaml','.xml','.json','.config','.yml','.yaml','.ps1','.psm1','.md','.txt','.wxs','.manifest','.pubxml')
$testPath = '(?i)(Tests?\\|\\.Tests\\|test-?only|fixture)'
$filePaths = @(& rg --files -uu -g '!bin/**' -g '!obj/**' -g '!artifacts/**' -g '!.git/**' -g '!.vs/**' -g '!TestResults/**' -g '!packages/**' $root 2>$null)
$files = @($filePaths | ForEach-Object { Get-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue })
function Is-Ignored([string]$relative) {
    if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { return $false }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $false }
    try { & git -C $root check-ignore --no-index -q -- $relative 2>$null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}
$findings = New-Object System.Collections.Generic.List[string]
$legacy = New-Object System.Collections.Generic.List[string]
$stagedFiles = @(); if ($StagedOnly) { $stagedFiles = @(git -C $root diff --cached --name-only) }
foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length).TrimStart('\','/')
    if (@($excludedDirs | Where-Object { $relative -like "*$_*" }).Count -gt 0) { continue }
    if (-not $StagedOnly -and (Is-Ignored $relative)) { continue }
    if ($StagedOnly -and $stagedFiles -notcontains ($relative -replace '\\','/') -and $stagedFiles -notcontains $relative) { continue }
    if ($textExtensions -notcontains $file.Extension.ToLowerInvariant()) { continue }
    if ($textExtensions -notcontains $file.Extension.ToLowerInvariant()) { continue }
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($file.FullName))
    $isTest = $relative -match $testPath
    $patterns = @(
        '(?i)-----BEGIN (?:RSA|EC|OPENSSH|PRIVATE) KEY-----',
        '(?i)(?:password|passwd|pwd)\s*[:=]\s*["''][^"'']{3,}["'']',
        '(?i)(?:api[_-]?key|access[_-]?token|refresh[_-]?token|secret|token)\s*[:=]\s*["''][^"'']{8,}["'']',
        '(?i)(?:Server|Data Source)\s*=\s*[^;\r\n]+;[^\r\n]*(?:Password|Pwd)\s*=\s*[^;\r\n]+',
        '(?i)Bearer\s+[A-Za-z0-9._~+/=-]{20,}',
        '\b(?:10\.40\.|37\.221\.)[0-9.]+\b',
        '(?i)\b[A-Za-z0-9._%+-]+@(?!example\.test\b|localhost\b)[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b'
    )
    foreach ($pattern in $patterns) {
        if ($text -match $pattern) {
            $allowed = $false
            if ($file.Extension -eq '.xaml') { $allowed = $true }
            if ($isTest -and $pattern -notmatch 'PRIVATE KEY') { $allowed = $true }
            if ($relative -eq 'Services\ConnectionTransferService.cs' -and $text -match 'MRemoteDefaultMasterPassword\s*=\s*"mR3m"') { $allowed = $true }
            if ($relative -like 'tools\RdpCleanSmokeRunner\*' -or $relative -like 'tools\RdpPixelCompareRunner\*') { $allowed = $true }
            if (-not $allowed) { $findings.Add("$relative :: $pattern") }
        }
    }
}
if ($legacy.Count -gt 0) {
    Write-Host ('LOCAL-ONLY legacy files excluded from baseline: ' + ($legacy -join ', '))
}
if ($findings.Count -gt 0) {
    Write-Error ('Pre-Git secret scan failed. Findings: ' + ($findings -join ' | '))
    exit 1
}
Write-Host ('PRE-GIT SECRET SCAN PASS. Scanned files=' + $files.Count)
