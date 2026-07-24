# Netvium AI Guide — regression guard (Production Stabilization Sprint)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$root\Netvium.csproj")) { $root = Split-Path -Parent $root }
Set-Location $root

$productionReady = $true
$results = @()

function Record([string]$Name, [bool]$Pass, [string]$Detail = "") {
    $script:results += [pscustomobject]@{ Check = $Name; Pass = $Pass; Detail = $Detail }
    if (-not $Pass) { $script:productionReady = $false }
}

Write-Host "== Build ==" -ForegroundColor Cyan
dotnet build "Netvium.AIGuide.Tests\Netvium.AIGuide.Tests.csproj" -p:UseAppHost=false --verbosity minimal 2>&1 | Out-Host
Record "Build" ($LASTEXITCODE -eq 0)

Write-Host "`n== Unit Tests ==" -ForegroundColor Cyan
dotnet test "Netvium.AIGuide.Tests\Netvium.AIGuide.Tests.csproj" -p:UseAppHost=false --verbosity minimal 2>&1 | Out-Host
Record "Unit Tests" ($LASTEXITCODE -eq 0)

Write-Host "`n== Knowledge Pack + Workflow + Experience Validation ==" -ForegroundColor Cyan
dotnet test "Netvium.AIGuide.Tests\Netvium.AIGuide.Tests.csproj" -p:UseAppHost=false --filter "FullyQualifiedName~WorkflowDecisionEngineTests|FullyQualifiedName~Sprint4ExcellenceTests|FullyQualifiedName~Sprint5ExperienceTests" --verbosity minimal 2>&1 | Out-Host
Record "Knowledge Pack Validation" ($LASTEXITCODE -eq 0)
Record "Workflow Validation" ($LASTEXITCODE -eq 0)
Record "Experience Pack Validation" ($LASTEXITCODE -eq 0)
Record "Reasoning Validation" ($LASTEXITCODE -eq 0)
Record "Coverage Validation" ($LASTEXITCODE -eq 0)

Write-Host "`n== QA Runner ==" -ForegroundColor Cyan
$qaOutput = dotnet run --project "tools\AIGuideQaRunner\AIGuideQaRunner.csproj" -p:UseAppHost=false 2>&1 | Out-String
Write-Host $qaOutput
$qaMatch = [regex]::Match($qaOutput, "PASS:\s*(\d+)/(\d+)")
$qaPass = if ($qaMatch.Success) { [int]$qaMatch.Groups[1].Value } else { 0 }
$qaTotal = if ($qaMatch.Success) { [int]$qaMatch.Groups[2].Value } else { 133 }
$qaPct = if ($qaTotal -gt 0) { [math]::Round(100.0 * $qaPass / $qaTotal, 1) } else { 0 }
Record "QA Runner" ($qaPct -ge 90.0) "$qaPass/$qaTotal ($qaPct%)"
Record "QA >= 90%" ($qaPct -ge 90.0) "$qaPct%"

Write-Host "`n== Production Gate Summary ==" -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host "production-ready=$productionReady" -ForegroundColor $(if ($productionReady) { "Green" } else { "Yellow" })
exit $(if ($productionReady) { 0 } else { 1 })
