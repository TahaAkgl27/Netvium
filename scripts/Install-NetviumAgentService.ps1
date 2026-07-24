[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory=$true)]
    [string]$ExecutablePath,
    [string]$SecretPath = (Join-Path $env:ProgramData 'Netvium\Agent\agent.key.dpapi'),
    [switch]$Start
)

$ErrorActionPreference = 'Stop'
$serviceName = 'Netvium Agent'
$current = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $current.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Yönetici yetkisi gerekir.' }

$exe = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or [IO.Path]::GetFileName($exe) -ne 'Netvium.Agent.exe') { throw 'Beklenen Netvium.Agent.exe bulunamadı.' }
$secret = [IO.Path]::GetFullPath($SecretPath)
if (-not (Test-Path -LiteralPath $secret -PathType Leaf)) { throw 'DPAPI servis anahtar dosyası bulunamadı.' }
$expectedDataRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'Netvium\Agent'))
if (-not $secret.StartsWith($expectedDataRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Servis anahtarı ProgramData Netvium Agent altında olmalıdır.' }
$acl = Get-Acl -LiteralPath $secret
foreach ($rule in $acl.Access) { if ($rule.IdentityReference -match '(^|\\)(Everyone|Users|Authenticated Users)$') { throw 'Servis anahtarı ACL kapsamı güvenli değil.' } }

if (-not $PSCmdlet.ShouldProcess($serviceName, 'Install or update Netvium Agent service')) { return }
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$created = $false
try {
    $binaryPath = '"' + $exe + '" --service --key-file "' + $secret + '"'
    if ($null -eq $existing) {
        New-Service -Name $serviceName -DisplayName $serviceName -Description 'Netvium read-only telemetry agent.' -BinaryPathName $binaryPath -StartupType Automatic
        $created = $true
    } else {
        & sc.exe config $serviceName binPath= $binaryPath start= delayed-auto | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Mevcut servis yapılandırılamadı.' }
    }

    & sc.exe description $serviceName 'Netvium read-only telemetry agent.' | Out-Null
    & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Servis recovery politikası uygulanamadı.' }
    & sc.exe config $serviceName start= delayed-auto | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Servis başlangıç modu uygulanamadı.' }

    if ($Start) {
        Start-Service -Name $serviceName
        $svc = Get-Service -Name $serviceName
        $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(20))
    }
    Write-Output "Netvium Agent servisi hazır: $serviceName"
}
catch {
    if ($created) {
        try { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue } catch { }
        try { & sc.exe delete $serviceName | Out-Null } catch { }
    }
    throw
}