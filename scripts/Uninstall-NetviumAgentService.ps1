[CmdletBinding(SupportsShouldProcess)]
param([switch]$RemoveData)

$ErrorActionPreference = 'Stop'
$serviceName = 'Netvium Agent'
$current = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $current.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Yönetici yetkisi gerekir.' }

if (-not $PSCmdlet.ShouldProcess($serviceName, 'Uninstall Netvium Agent service')) { return }
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Netvium Agent servisi silinemedi.' }
}

if ($RemoveData) {
    $dataRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'Netvium\Agent'))
    $expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'Netvium\Agent'))
    if ($dataRoot -ne $expectedRoot -or $dataRoot -eq [IO.Path]::GetFullPath($env:ProgramData)) { throw 'Güvenli veri yolu doğrulanamadı.' }
    if (Test-Path -LiteralPath $dataRoot -PathType Container) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
}
Write-Output "Netvium Agent servisi kaldırıldı: $serviceName"