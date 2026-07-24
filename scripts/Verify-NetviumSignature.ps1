[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$ExpectedPublisher,
    [switch]$WhatIf
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    $root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Release klasoru bulunamadi." }
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Extension -in '.exe','.dll','.msi' })
    if ($files.Count -eq 0) { throw "Dogrulanacak imzali dosya bulunamadi." }
    foreach ($file in $files) {
        if ($WhatIf) { Write-Host "VERIFY $($file.FullName)"; continue }
        $sig = Get-AuthenticodeSignature -FilePath $file.FullName
        if ($sig.Status -ne 'Valid') { throw ("Gecersiz/eksik imza: {0} ({1})" -f $file.Name,$sig.Status) }
        if ($null -eq $sig.SignerCertificate) { throw "Imzalayan sertifika yok: $($file.Name)" }
        if ($ExpectedPublisher -and $sig.SignerCertificate.Subject -notmatch [Regex]::Escape($ExpectedPublisher)) { throw "Publisher beklenen degerle eslesmedi: $($file.Name)" }
        if ($null -eq $sig.TimeStamperCertificate) { throw "Timestamp yok: $($file.Name)" }
        if ($sig.TimeStamperCertificate.NotAfter -lt [DateTime]::UtcNow) { throw "Timestamp sertifikasi suresi dolmus: $($file.Name)" }
    }
    Write-Host ("Signature verification passed. Files={0}" -f $files.Count)
}
catch {
    Write-Error $_
    exit 1
}