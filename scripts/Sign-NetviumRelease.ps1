[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][string]$Thumbprint,
    [Parameter(Mandatory=$true)][uri]$TimestampUrl,
    [switch]$WhatIf
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-SafeReleasePath([string]$InputPath) {
    $full = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $InputPath -ErrorAction Stop).Path)
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Release klasoru bulunamadi." }
    return $full
}

try {
    $root = Resolve-SafeReleasePath $Path
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Extension -in '.exe','.dll','.msi' -and $_.Name -notlike '*.deps.json' })
    if ($files.Count -eq 0) { throw 'Imzalanacak PE/installer dosyasi bulunamadi.' }
    $files = @($files | Sort-Object @{Expression={ if($_.Extension -eq '.dll'){0}elseif($_.Extension -eq '.exe'){1}else{2} }}, FullName)
    if ($WhatIf) { foreach($file in $files){ Write-Host ('WHATIF SIGN {0}' -f $file.FullName) }; Write-Host ('Signing dry-run completed. Files={0}' -f $files.Count); return }
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My\$Thumbprint,Cert:\LocalMachine\My\$Thumbprint -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $cert) { throw "Belirtilen thumbprint ile sertifika bulunamadi." }
    if ($cert.NotBefore -gt [DateTime]::UtcNow -or $cert.NotAfter -lt [DateTime]::UtcNow) { throw "Sertifika gecerlilik araliginda degil." }
    if ($cert.Subject -eq $cert.Issuer) { throw "Self-signed sertifika production imzasi icin kabul edilmez." }
    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    $eku = @($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | ForEach-Object { $_.Format($false) }) -join ';'
    if ($eku -notmatch 'Code Signing|1\.3\.6\.1\.5\.5\.7\.3\.3') { throw "Sertifikada Code Signing EKU bulunamadi." }

    foreach ($file in $files) {
        Write-Host ("SIGN {0}" -f $file.FullName)
        if ($WhatIf) { continue }
        $result = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert -TimestampServer $TimestampUrl
        if ($result.Status -ne 'Valid') { throw ("Imza basarisiz: {0} ({1})" -f $file.Name,$result.Status) }
    }
    Write-Host ("Signing completed. Files={0}; TimestampUrl={1}" -f $files.Count,$TimestampUrl.AbsoluteUri)
}
catch {
    Write-Error $_
    exit 1
}
