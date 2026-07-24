[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [switch]$RequireSignature,
    [switch]$RequireInstaller,
    [switch]$ProbeStartup
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Fail([string]$Message) { throw $Message }
function Get-Text([string]$File) { try { return [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($File)) } catch { return '' } }
try {
    $root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { Fail 'Release klasoru yok.' }
    $manifestPath = Join-Path $root 'release-manifest.json'; $hashPath = Join-Path $root 'SHA256SUMS.txt'
    if (-not (Test-Path $manifestPath) -or -not (Test-Path $hashPath)) { Fail 'Manifest veya SHA256SUMS.txt eksik.' }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $required = @('ProductName','Version','Configuration','RuntimeIdentifier','SelfContained','TestsExecuted','TestsSkipped','GitCommit','DirtyWorktree','Signed','SignatureVerified','InstallerBuilt','InstallerVerified','Rc2SmokeResult','FileCount','Sha256ManifestHash','VulnerabilityStatus','RuntimePrerequisite','LiveValidationsPending','KnownLimitations')
    foreach($name in $required){if($null -eq $manifest.PSObject.Properties[$name]){Fail "Manifest alani eksik: $name"}}
    if ([string]$manifest.ProductName -ne 'Netvium') { Fail 'Manifest ProductName gecersiz.' }
    if ([string]$manifest.Configuration -ne 'Release') { Fail 'Production paketi Release olmalidir.' }
    if ([string]$manifest.Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$') { Fail 'Manifest version SemVer degil.' }
    if ([string]$manifest.GitCommit -eq '') { Fail 'GitCommit bos olamaz.' }
    if ([string]$manifest.PackageType -eq 'ProductionCandidate' -and [bool]$manifest.DirtyWorktree) { Fail 'Production candidate dirty worktree ile uretilmis.' }
    if (-not [bool]$manifest.TestsExecuted -and -not [bool]$manifest.TestsSkipped) { Fail 'TestsExecuted/TestsSkipped durumu belirsiz.' }
    if (-not [bool]$manifest.TestsSkipped -and [string]$manifest.Rc2SmokeResult -ne 'ALL PASS') { Fail 'Rc2Smoke ALL PASS degil.' }
    if ($RequireInstaller -and -not [bool]$manifest.InstallerBuilt) { Fail 'Installer gerekli fakat build edilmemis.' }
    if (($RequireInstaller -or [bool]$manifest.InstallerBuilt) -and @(Get-ChildItem -LiteralPath (Join-Path $root 'Installer') -Filter '*.msi' -File -ErrorAction SilentlyContinue).Count -eq 0) { Fail 'InstallerBuilt bildirildi fakat MSI dosyasi yok.' }
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File)
    if ($files.Count -lt 3) { Fail 'Release paketi bos veya eksik.' }
    $forbiddenNamePatterns = @('(^|[\\/])obj([\\/]|$)','(^|[\\/])bin([\\/]|$)','\.cs$','\.user$','\.suo$','\.pfx$','\.snk$','\.rdp$','license\.dat$','agent\.key\.dpapi$','testhost','coverlet','\.Test(s)?\.dll$')
    foreach($file in $files){$relative=$file.FullName.Substring($root.Length).TrimStart('\','/'); foreach($pattern in $forbiddenNamePatterns){if($relative -match $pattern){Fail "Allowlist engeli: $($relative -replace '[A-Za-z]:','<drive>')"}}}
    $contentPatterns = @(
        'Netvium\.Enterprise\.OfflineLicense\.v1\.2026','FAZ6D_SECRET','LicenseActivationService','LicenseValidator','RdpConnectionManager','EmbeddedRdpClient','RdpConnectDeadline',
        '(?i)password\s*=\s*(?!<|\[|$)[^\s;<>]+','(?i)pwd\s*=\s*(?!<|\[|$)[^\s;<>]+','(?i)api[_-]?key\s*[:=]\s*[A-Za-z0-9]','(?i)Bearer\s+[A-Za-z0-9._~-]{12,}',
        '(?i)(Server|Data Source)\s*=\s*[^;\r\n]+;[^\r\n]*(Password|Pwd)\s*=', '\b(10\.40\.[0-9]{1,3}\.[0-9]{1,3}|37\.221\.[0-9]{1,3}\.[0-9]{1,3})\b'
    )
    $textExtensions = @('.txt','.json','.md','.xml','.config','.wxs','.ps1','.psm1','.yml','.yaml','.manifest','.pubxml','.props','.targets'); $textFiles = @($files | Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() }); foreach($file in $textFiles){$text=Get-Text $file.FullName; foreach($pattern in $contentPatterns){if($text -match $pattern){Fail "Secret/legacy tarama engeli: $($file.Name) pattern=$pattern"}}}
    $hashLines = @(Get-Content -LiteralPath $hashPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $hashNames = @{}
    foreach($line in $hashLines){if($line -notmatch '^([0-9A-Fa-f]{64})\s{2}(.+)$'){Fail 'SHA256SUMS satir formati gecersiz.'}; $expected=$Matches[1].ToUpperInvariant(); $rel=$Matches[2].Trim(); $candidate=Join-Path $root ($rel -replace '/','\'); if(-not(Test-Path -LiteralPath $candidate -PathType Leaf)){Fail "Hash dosyasi eksik: $rel"}; $actual=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToUpperInvariant(); if($actual -ne $expected){Fail "Hash mismatch: $rel"}; $hashNames[$rel]=$true }
    foreach($file in $files){$rel=$file.FullName.Substring($root.Length).TrimStart('\','/') -replace '\\','/'; if($rel -notin @('SHA256SUMS.txt','release-manifest.json') -and -not $hashNames.ContainsKey($rel)){Fail "Hash manifestinde olmayan dosya: $rel"}}
    if ([int]$manifest.FileCount -ne ($files.Count - 2)) { Fail 'Manifest FileCount uyumsuz.' }
    if([string]$manifest.Sha256ManifestHash -ne (Get-FileHash -LiteralPath $hashPath -Algorithm SHA256).Hash.ToUpperInvariant()){Fail 'Manifest SHA256SUMS hash alani uyusmuyor.'}
    $appDll=Join-Path $root 'App\Netvium.dll'; $appExe=Join-Path $root 'App\Netvium.exe'; if(-not(Test-Path $appDll) -or -not(Test-Path $appExe)){Fail 'App allowlist ana dosyalari eksik.'}
    $base=(([string]$manifest.Version) -split '[+-]')[0]; $fileVersion=(Get-Item $appDll).VersionInfo.FileVersion; if($fileVersion -and -not $fileVersion.StartsWith($base + '.')){Fail "Assembly/FileVersion uyumsuz: $fileVersion vs $base"}
    if(-not [bool]$manifest.SelfContained){$desktop=@(dotnet --list-runtimes 2>$null | Where-Object {$_ -match '^Microsoft\.WindowsDesktop\.App 8\.'}); if($desktop.Count -eq 0){Write-Host 'PREREQUISITE MISSING: Microsoft.WindowsDesktop.App 8.x (x64)'}else{Write-Host 'PREREQUISITE PASS: Microsoft.WindowsDesktop.App 8.x'}}
    if($RequireSignature -or [bool]$manifest.Signed){& (Join-Path $PSScriptRoot 'Verify-NetviumSignature.ps1') -Path $root; if($LASTEXITCODE -ne 0){Fail 'Signature verification basarisiz.'}}
    if($ProbeStartup){$proc=Start-Process -FilePath $appExe -PassThru; Start-Sleep -Seconds 5; if($proc.HasExited -and $proc.ExitCode -ne 0){Fail "Netvium startup probe crash exit=$($proc.ExitCode)"}; if(-not $proc.HasExited){Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue}; Write-Host 'Startup probe PASS'}else{Write-Host 'Startup probe PENDING (GUI/COM canli masaustu dogrulamasi)'}
    Write-Host ("LOCAL RELEASE GATE PASS: {0}" -f $root)
}
catch { Write-Error $_; exit 1 }
