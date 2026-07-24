[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-DesktopRuntime8X64 {
    param([object[]]$Runtimes)
    foreach ($runtime in $Runtimes) {
        if ([string]$runtime.Framework -eq 'Microsoft.WindowsDesktop.App' -and
            [string]$runtime.Version -match '^8\.' -and
            [string]$runtime.Architecture -eq 'x64') { return $true }
    }
    return $false
}

$cases = @(
    [pscustomobject]@{ Name='Yalnız Microsoft.NETCore.App'; Input=@([pscustomobject]@{Framework='Microsoft.NETCore.App'; Version='8.0.28'; Architecture='x64'}); Expected=$false },
    [pscustomobject]@{ Name='Yalnız Microsoft.AspNetCore.App'; Input=@([pscustomobject]@{Framework='Microsoft.AspNetCore.App'; Version='8.0.28'; Architecture='x64'}); Expected=$false },
    [pscustomobject]@{ Name='WindowsDesktop 8 x64'; Input=@([pscustomobject]@{Framework='Microsoft.WindowsDesktop.App'; Version='8.0.28'; Architecture='x64'}); Expected=$true },
    [pscustomobject]@{ Name='Runtime yok'; Input=@(); Expected=$false },
    [pscustomobject]@{ Name='Yalnız WindowsDesktop 8 x86'; Input=@([pscustomobject]@{Framework='Microsoft.WindowsDesktop.App'; Version='8.0.28'; Architecture='x86'}); Expected=$false },
    [pscustomobject]@{ Name='Yalnız WindowsDesktop 9 x64'; Input=@([pscustomobject]@{Framework='Microsoft.WindowsDesktop.App'; Version='9.0.0'; Architecture='x64'}); Expected=$false }
)
$failed = 0
foreach ($case in $cases) {
    $actual = Test-DesktopRuntime8X64 -Runtimes $case.Input
    if ($actual -ne $case.Expected) { Write-Host ("FAIL: {0}" -f $case.Name); $failed++ } else { Write-Host ("PASS: {0}" -f $case.Name) }
}
if ($failed -gt 0) { throw ("Runtime prerequisite fixture gate failed: {0}" -f $failed) }
Write-Host 'RUNTIME PREREQUISITE FIXTURE PASS'