[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][string]$ExpectedVersion
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$msi = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path)
if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) { throw 'MSI bulunamadi.' }
if ($ExpectedVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'ExpectedVersion MSI numeric version olmali.' }
$wix = Get-Command wix -ErrorAction Stop
$temp = Join-Path ([IO.Path]::GetTempPath()) ('netvium-msi-gate-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $out = Join-Path $temp 'decompiled.wxs'
    & $wix.Source msi decompile $msi -o $out 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $out -PathType Leaf)) { throw 'MSI decompile/table okunamadi.' }
    [xml]$xml = Get-Content -LiteralPath $out -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable); $ns.AddNamespace('w','http://wixtoolset.org/schemas/v4/wxs')
    $pkg = $xml.SelectSingleNode('/w:Wix/w:Package',$ns)
    if ($null -eq $pkg) { throw 'MSI Package tablosu okunamadi.' }
    if ([string]$pkg.GetAttribute('Version') -ne $ExpectedVersion) { throw "MSI ProductVersion uyumsuz: $($pkg.GetAttribute('Version'))" }
    if ([string]::IsNullOrWhiteSpace($pkg.GetAttribute('ProductCode'))) { throw 'MSI ProductCode eksik.' }
    if ([string]::IsNullOrWhiteSpace($pkg.GetAttribute('UpgradeCode'))) { throw 'MSI UpgradeCode eksik.' }
    if ($pkg.GetAttribute('UpgradeCode').Trim('{}') -ne '6D7C8B65-5B5A-4C2D-9A1C-2A6C0F0E6F6E') { throw 'UpgradeCode beklenen sabit degerle uyusmuyor.' }
    if ([string]$pkg.GetAttribute('Manufacturer') -ne 'Netvium') { throw 'ARP Publisher/Manufacturer uyumsuz.' }
    if ($null -eq $xml.SelectSingleNode('/w:Wix/w:Package/w:StandardDirectory[@Id="ProgramFiles64Folder"]',$ns)) { throw 'ProgramFiles64Folder x64 hedefi yok.' }
    $major = $xml.SelectSingleNode('/w:Wix/w:Package/w:MajorUpgrade',$ns)
    if ($null -eq $major -or $major.GetAttribute('AllowSameVersionUpgrades') -ne 'yes' -or [string]::IsNullOrWhiteSpace($major.GetAttribute('DowngradeErrorMessage'))) { throw 'MajorUpgrade/downgrade policy eksik.' }
    $launch = @($xml.SelectNodes('/w:Wix/w:Package/w:Launch',$ns))
    $runtimeLaunch = @($launch | Where-Object { $_.GetAttribute('Condition') -match 'NETVIUM_DESKTOP_RUNTIME_8_X64' })
    if ($runtimeLaunch.Count -ne 1) { throw 'Desktop Runtime launch condition eksik.' }
    if ($runtimeLaunch[0].GetAttribute('Message') -notmatch '(?i)\.NET 8.*Desktop Runtime.*x64') { throw 'Runtime launch mesaji guvenli/anlasilir degil.' }
    $search = $xml.SelectSingleNode('//w:RegistrySearch[@Id="NetviumDesktopRuntime8X64"]',$ns)
    if ($null -eq $search -or $search.GetAttribute('Bitness') -ne 'always64' -or $search.GetAttribute('Key') -notmatch 'InstalledVersions\\x64\\sharedfx\\Microsoft\.WindowsDesktop\.App') { throw 'x64 WindowsDesktop sharedfx registry aramasi eksik.' }
    $files = @($xml.SelectNodes('//w:File',$ns)); $components = @($xml.SelectNodes('//w:Component',$ns))
    if ($files.Count -eq 0 -or $components.Count -eq 0) { throw 'MSI component/file tablolari bos.' }
    $features = @($xml.SelectNodes('/w:Wix/w:Package/w:Feature',$ns))
    $agent = $features | Where-Object { $_.GetAttribute('Id') -eq 'AgentFeature' }
    if ($null -eq $agent -or $agent.GetAttribute('Level') -ne '100') { throw 'Agent feature varsayilan davranisi beklenen optional seviyede degil.' }
    if ($null -ne $xml.SelectSingleNode('//w:CustomAction',$ns)) { throw 'Beklenmeyen custom action bulundu.' }
    $serialized = $xml.OuterXml
    if ($serialized -match '(?i)firewall|api[_-]?key|agent\.key|password\s*=|connectionstring\s*=') { throw 'MSI table/source secret veya firewall izi tasiyor.' }
    Write-Host ("MSI STATIC TABLE GATE PASS: Version={0}; ProductCode={1}; Components={2}; Files={3}" -f $pkg.GetAttribute('Version'),$pkg.GetAttribute('ProductCode'),$components.Count,$files.Count)
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
}