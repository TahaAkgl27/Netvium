[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')][string]$Runtime = 'win-x64',
    [string]$OutputRoot,
    [switch]$SelfContained,
    [switch]$SkipTests,
    [switch]$RequireSignature,
    [switch]$WhatIf
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:NugetVulnerabilityStatus = 'not verified'
$restoreMode = 'Solution'
$stageCreated = $false
$staging = $null

function Write-Step([string]$Message) { Write-Host ("[NetviumRelease] {0}" -f $Message) }
function Assert-SemVer([string]$Value) {
    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') { throw "Version gecersiz SemVer: $Value" }
}
function Get-VersionBase([string]$Value) { return (($Value -split '[+-]')[0]) }
function Invoke-Logged([string]$FilePath, [string[]]$Arguments) {
    Write-Step (("RUN {0} {1}") -f $FilePath,($Arguments -join ' '))
    $lines = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($line in $lines) {
        $text = [string]$line
        if ($text -match 'NU1900|NU1801|unable to load the service index|vulnerability') { $script:NugetVulnerabilityStatus = 'vulnerability status not verified' }
        Write-Host $text
    }
    if ($exitCode -ne 0) { throw ("Komut basarisiz ({0}): {1}" -f $exitCode,$FilePath) }
    return $lines
}
function Resolve-OutputRoot([string]$Requested) {
    if ([string]::IsNullOrWhiteSpace($Requested)) { return [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release')) }
    $full = [IO.Path]::GetFullPath($Requested)
    if ([StringComparer]::OrdinalIgnoreCase.Equals($full,$repoRoot)) { throw 'OutputRoot repo kok dizini olamaz.' }
    if ($full -match '(^|[\\/])(bin|obj)([\\/]|$)') { throw 'OutputRoot bin/obj altinda olamaz.' }
    return $full
}
function Get-CommitMetadata {
    $gitDir = Join-Path $repoRoot '.git'
    if (-not (Test-Path -LiteralPath $gitDir)) { return 'NO_GIT_METADATA' }
    try { $value = (& git -C $repoRoot rev-parse --short HEAD 2>$null); if($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($value)){return ([string]$value).Trim()} } catch { }
    return 'NO_GIT_METADATA'
}
function Get-GitState {
    $gitDir = Join-Path $repoRoot '.git'
    if (-not (Test-Path -LiteralPath $gitDir)) { return [pscustomobject]@{ Commit = 'NO_GIT_METADATA'; DirtyWorktree = $false } }
    $commit = Get-CommitMetadata
    $dirty = $false
    try {
        $status = @(& git -C $repoRoot status --porcelain 2>$null)
        $dirty = ($LASTEXITCODE -eq 0 -and $status.Count -gt 0)
    } catch { $dirty = $true }
    return [pscustomobject]@{ Commit = $commit; DirtyWorktree = $dirty }
}
function Write-DependencyInventory([string]$Destination) {
    $rows = New-Object System.Collections.Generic.List[string]
    $rows.Add('Netvium dependency inventory')
    $rows.Add(('GeneratedUtc={0}' -f [DateTime]::UtcNow.ToString('o')))
    $rows.Add(('VulnerabilityStatus={0}' -f $script:NugetVulnerabilityStatus))
    $rows.Add('')
    $assets = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter project.assets.json -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\.*\\(bin|obj)\\' })
    $seen = @{}
    foreach ($asset in $assets) {
        try {
            $json = Get-Content -LiteralPath $asset.FullName -Raw | ConvertFrom-Json
            foreach ($prop in $json.libraries.psobject.Properties) {
                $key = [string]$prop.Name
                if (-not $seen.ContainsKey($key)) { $seen[$key] = $true }
            }
        } catch { }
    }
    foreach ($key in ($seen.Keys | Sort-Object)) { $rows.Add($key) }
    [IO.File]::WriteAllLines($Destination,$rows,[Text.Encoding]::UTF8)
}
function New-VersionedApplicationManifest([string]$Destination, [string]$AssemblyVersion) {
    $template = [IO.File]::ReadAllText((Join-Path $repoRoot 'app.manifest'))
    if (-not $template.Contains('$(AssemblyVersion)')) { throw 'app.manifest version token bulunamadi.' }
    $rendered = $template.Replace('$(AssemblyVersion)', $AssemblyVersion)
    if ($rendered -match '\$\(AssemblyVersion\)' -or $rendered -notmatch 'version="[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+"') { throw 'Versioned embedded manifest numeric degil.' }
    [IO.File]::WriteAllText($Destination, $rendered, (New-Object Text.UTF8Encoding($false)))
}
function Write-Hashes([string]$PackageRoot, [string]$Destination) {
    $root = [IO.Path]::GetFullPath($PackageRoot)
    $rows = New-Object System.Collections.Generic.List[string]
    foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('\','/') -replace '\\','/'
        if ($relative -in @('SHA256SUMS.txt','release-manifest.json')) { continue }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        $rows.Add(("{0}  {1}" -f $hash,$relative))
    }
    [IO.File]::WriteAllLines($Destination,$rows,[Text.Encoding]::ASCII)
}
function Copy-ReleaseDocs([string]$Destination) {
    $docs = @('Netvium-v1.0-Known-Limitations.md','Netvium-v1.0-Controlled-Pilot-Release-Notes.md','Netvium-v1.0-Controlled-Pilot-Checklist.md','Netvium-RC2-Technical-Debt.md')
    $docsRoot = Join-Path $Destination 'Docs'; New-Item -ItemType Directory -Force -Path $docsRoot | Out-Null
    foreach ($doc in $docs) { if(Test-Path (Join-Path $repoRoot $doc)){Copy-Item (Join-Path $repoRoot $doc) (Join-Path $docsRoot $doc) -Force} }
}
function Write-Manifest([string]$PackageRoot,[hashtable]$Data) {
    $manifestPath = Join-Path $PackageRoot 'release-manifest.json'
    $Data['Sha256ManifestHash'] = $Data['Sha256ManifestHash']
    ($Data | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

function New-WixPayloadFragment([string]$StageRoot,[string]$Destination) {
    $doc = New-Object System.Xml.XmlDocument
    $decl = $doc.CreateXmlDeclaration('1.0','utf-8',$null); [void]$doc.AppendChild($decl)
    $wix = $doc.CreateElement('Wix','http://wixtoolset.org/schemas/v4/wxs'); [void]$doc.AppendChild($wix)
    $fragment = $doc.CreateElement('Fragment',$wix.NamespaceURI); [void]$wix.AppendChild($fragment)
    foreach ($spec in @(@{Name='ApplicationFiles';Root='App';Directory='INSTALLFOLDER';Prefix='App'},@{Name='AgentFiles';Root='Agent';Directory='AGENTFOLDER';Prefix='Agent'})) {
        $sourceRoot = Join-Path $StageRoot $spec.Root
        $group = $doc.CreateElement('ComponentGroup',$wix.NamespaceURI); $group.SetAttribute('Id',$spec.Name); [void]$fragment.AppendChild($group)
        $directoryRef = $doc.CreateElement('DirectoryRef',$wix.NamespaceURI); $directoryRef.SetAttribute('Id',$spec.Directory); [void]$fragment.AppendChild($directoryRef)
        $directoryNodes = @{ '' = $directoryRef }
        $files = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Object FullName)
        foreach ($file in $files) {
            $rel = $file.FullName.Substring($sourceRoot.Length).TrimStart('\','/') -replace '\\','/'
            $dirRel = [IO.Path]::GetDirectoryName($rel); if ($null -eq $dirRel) { $dirRel = '' } else { $dirRel = $dirRel -replace '\\','/' }
            $parts = @(); if ($dirRel) { $parts = $dirRel.Split('/') }
            $current = ''; $parent = $directoryRef
            foreach ($part in $parts) {
                $next = if($current){ "$current/$part" } else { $part }
                if (-not $directoryNodes.ContainsKey($next)) {
                    $dirHash = ([Security.Cryptography.SHA1]::Create()).ComputeHash([Text.Encoding]::UTF8.GetBytes("$($spec.Prefix):$next"))
                    $dirId = "$($spec.Prefix)Dir_" + (([BitConverter]::ToString($dirHash)).Replace('-','').Substring(0,12))
                    $node = $doc.CreateElement('Directory',$wix.NamespaceURI); $node.SetAttribute('Id',$dirId); $node.SetAttribute('Name',$part); [void]$parent.AppendChild($node); $directoryNodes[$next]=$node
                }
                $parent = $directoryNodes[$next]; $current = $next
            }
            $componentHash = ([Security.Cryptography.SHA1]::Create()).ComputeHash([Text.Encoding]::UTF8.GetBytes("$($spec.Prefix):$rel"))
            $componentId = "$($spec.Prefix)File_" + (([BitConverter]::ToString($componentHash)).Replace('-','').Substring(0,16))
            $component = $doc.CreateElement('Component',$wix.NamespaceURI); $component.SetAttribute('Id',$componentId); $component.SetAttribute('Directory',$spec.Directory); if($current){$component.SetAttribute('Directory',$directoryNodes[$current].GetAttribute('Id'))}; $component.SetAttribute('Guid','*'); [void]$fragment.AppendChild($component)
            $fileNode = $doc.CreateElement('File',$wix.NamespaceURI); $fileNode.SetAttribute('Id',($componentId+'File')); $fileNode.SetAttribute('Source',$file.FullName); $fileNode.SetAttribute('KeyPath','yes'); [void]$component.AppendChild($fileNode)
            $ref = $doc.CreateElement('ComponentRef',$wix.NamespaceURI); $ref.SetAttribute('Id',$componentId); [void]$group.AppendChild($ref)
        }
    }
    $settings = New-Object System.Xml.XmlWriterSettings; $settings.Indent=$true; $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($Destination,$settings); try { $doc.Save($writer) } finally { $writer.Dispose() }
}
try {
    Assert-SemVer $Version
    $versionBase = Get-VersionBase $Version
    $assemblyVersion = "$versionBase.0"
    $output = Resolve-OutputRoot $OutputRoot
    $final = Join-Path $output $Version
    $productionMode = $SelfContained.IsPresent -or $RequireSignature.IsPresent
    if ($SkipTests -and $productionMode) { throw 'Production Candidate/RequireSignature modunda testler atlanamaz.' }
    if ($WhatIf) {
        Write-Step "WHATIF: Configuration=$Configuration Runtime=$Runtime SelfContained=$($SelfContained.IsPresent) Output=$final"
        Write-Step 'WHATIF: restore -> build -> tests/Rc2Smoke -> publish -> allowlist/manifest/hash -> gate -> atomic move'
        return
    }
    if (Test-Path -LiteralPath $final) { throw "Ayni version zaten mevcut, sessiz overwrite yapilmayacak: $final" }
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $staging = Join-Path $output ('.staging-{0}-{1}' -f $Version,[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $staging | Out-Null; $stageCreated = $true
    $generatedManifest = Join-Path $staging 'Netvium.generated.manifest'
    New-VersionedApplicationManifest $generatedManifest $assemblyVersion
    $manifestArg = '/p:ApplicationManifest={0}' -f $generatedManifest
    $gitState = Get-GitState
    $commit = $gitState.Commit
    $dirtyWorktree = [bool]$gitState.DirtyWorktree
    if ($productionMode -and $dirtyWorktree) { throw "Production candidate dirty worktree den uretilemez. Once baseline commit olusturun." }

    try {
        Invoke-Logged 'dotnet' @('restore',(Join-Path $repoRoot 'Netvium.sln'),'--ignore-failed-sources') | Out-Null
    } catch {
        Write-Step 'Solution restore basarisiz; ayni solution projeleri tek tek restore ediliyor.'
        $restoreMode = 'PerProjectFallback'
        $projects = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter '*.csproj' -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
        foreach ($project in $projects) { Invoke-Logged 'dotnet' @('restore',$project.FullName,'--ignore-failed-sources') | Out-Null }
    }
    $buildArgs = @('build',(Join-Path $repoRoot 'Netvium.sln'),'-c',$Configuration,'--no-restore','/m:1',('/p:Version={0}' -f $Version),('/p:VersionPrefix={0}' -f $versionBase),('/p:AssemblyVersion={0}' -f $assemblyVersion),('/p:FileVersion={0}' -f $assemblyVersion),('/p:InformationalVersion={0}' -f $Version),$manifestArg)
    Invoke-Logged 'dotnet' $buildArgs | Out-Null
    $testResults = [ordered]@{}
    $testsExecuted = $false
    if (-not $SkipTests) {
        $testsExecuted = $true
        $testProjects = @('Netvium.Agent.Tests\Netvium.Agent.Tests.csproj','Netvium.Account.Tests\Netvium.Account.Tests.csproj','Netvium.Identity.Api.Tests\Netvium.Identity.Api.Tests.csproj','Netvium.AIGuide.Tests\Netvium.AIGuide.Tests.csproj')
        foreach ($project in $testProjects) { Invoke-Logged 'dotnet' @('test',(Join-Path $repoRoot $project),'-c','Debug','--no-restore','/m:1') | Out-Null; $testResults[$project] = 'PASS' }
        $smoke = Invoke-Logged 'dotnet' @('run','--project',(Join-Path $repoRoot 'tools\Rc2Smoke\Rc2Smoke.csproj'),'-c','Release','--no-restore')
        if (($smoke -join "`n") -notmatch 'ALL PASS') { throw 'Rc2Smoke ALL PASS sonucu bulunamadi.' }
        $rc2 = 'ALL PASS'
    } else { $rc2 = 'SKIPPED'; $testResults['TestsSkipped'] = 'true' }

    $appRoot = Join-Path $staging 'App'; $agentRoot = Join-Path $staging 'Agent'; New-Item -ItemType Directory -Force -Path $appRoot,$agentRoot | Out-Null
    $publishMode = 'RID-specific'
    $commonPublish = @('-c',$Configuration,('-p:SelfContained={0}' -f $SelfContained.IsPresent),'-p:PublishTrimmed=false','-p:PublishSingleFile=false',('/p:Version={0}' -f $Version),('/p:VersionPrefix={0}' -f $versionBase),('/p:AssemblyVersion={0}' -f $assemblyVersion),('/p:FileVersion={0}' -f $assemblyVersion),('/p:InformationalVersion={0}' -f $Version),$manifestArg,'--no-restore')
    $publishArgs = @('publish',(Join-Path $repoRoot 'Netvium.csproj')) + $commonPublish + @('-r',$Runtime,'-o',$appRoot)
    try { Invoke-Logged 'dotnet' $publishArgs | Out-Null } catch {
        if ($SelfContained) { throw ("Netvium self-contained publish NOT BUILT: {0}" -f $_.Exception.Message) }
        Write-Step 'RID-specific framework-dependent publish kullanilamadi; portable framework-dependent fallback deneniyor.'
        if (Test-Path -LiteralPath $appRoot) { Remove-Item -LiteralPath $appRoot -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $appRoot | Out-Null
        $publishMode = 'FrameworkDependentNoRidFallback'
        Invoke-Logged 'dotnet' (@('publish',(Join-Path $repoRoot 'Netvium.csproj')) + $commonPublish + @('-p:SelfContained=false','-o',$appRoot)) | Out-Null
    }
    $agentPublishArgs = @('publish',(Join-Path $repoRoot 'Netvium.Agent\Netvium.Agent.csproj')) + $commonPublish + @('-r',$Runtime,'-o',$agentRoot)
    try { Invoke-Logged 'dotnet' $agentPublishArgs | Out-Null } catch {
        if ($SelfContained) { throw ("Agent self-contained publish NOT BUILT: {0}" -f $_.Exception.Message) }
        Write-Step 'Agent RID-specific publish kullanilamadi; portable framework-dependent fallback deneniyor.'
        if (Test-Path -LiteralPath $agentRoot) { Remove-Item -LiteralPath $agentRoot -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $agentRoot | Out-Null
        Invoke-Logged 'dotnet' (@('publish',(Join-Path $repoRoot 'Netvium.Agent\Netvium.Agent.csproj')) + $commonPublish + @('-p:SelfContained=false','-o',$agentRoot)) | Out-Null
        $publishMode = 'FrameworkDependentNoRidFallback'
    }
    if (Test-Path -LiteralPath $generatedManifest) { Remove-Item -LiteralPath $generatedManifest -Force }
    Copy-ReleaseDocs $staging

    $installerBuilt = $false
    $installerVerified = $false
    $wix = Get-Command wix -ErrorAction SilentlyContinue
    if ($null -ne $wix) {
        $installerDir = Join-Path $staging 'Installer'; New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
        $msi = Join-Path $installerDir ("Netvium-{0}.msi" -f $Version)
        $payloadWxs = Join-Path $installerDir 'Netvium.Payload.wxs'; New-WixPayloadFragment $staging $payloadWxs
        Invoke-Logged $wix.Source @('build',(Join-Path $repoRoot 'installer\Netvium.wxs'),$payloadWxs,'-arch','x64','-d',("ProductVersion={0}" -f $versionBase),'-o',$msi,'-pdbtype','none') | Out-Null
        if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) { throw 'WiX MSI cikmadi.' }
        & (Join-Path $PSScriptRoot 'Test-NetviumMsi.ps1') -Path $msi -ExpectedVersion $versionBase
        if ($LASTEXITCODE -ne 0) { throw 'MSI static table gate basarisiz.' }
        $installerVerified = $true
        $installerBuilt = $true
    } else { Write-Step 'WiX bulunamadi; InstallerBuilt=false.' }

    $signed = $false
    if ($RequireSignature) {
        throw 'RequireSignature icin once Sign-NetviumRelease.ps1 ile guvenilir sertifikali imza uygulanmalidir.'
    }
    $inventory = Join-Path $staging 'dependency-inventory.txt'; Write-DependencyInventory $inventory
    $manifest = [ordered]@{
        ProductName='Netvium'; RestoreMode=$restoreMode; Version=$Version; VersionBase=$versionBase; BuildUtc=[DateTime]::UtcNow.ToString('o'); Configuration=$Configuration; RuntimeIdentifier=$Runtime; PublishMode=$publishMode; SelfContained=$SelfContained.IsPresent; PackageType=($(if($SelfContained){'ProductionCandidate'}else{'ControlledPilot'})); TestsExecuted=$testsExecuted; TestsSkipped=$SkipTests.IsPresent; TestResults=$testResults; GitCommit=$commit; DirtyWorktree=$dirtyWorktree; Signed=$signed; SignatureVerified=$false; InstallerBuilt=$installerBuilt; InstallerVerified=$installerVerified; Rc2SmokeResult=$rc2; FileCount=0; Sha256ManifestHash=''; VulnerabilityStatus=$script:NugetVulnerabilityStatus; RuntimePrerequisite=($(if($SelfContained){'None'}else{'.NET 8 Desktop Runtime (x64)'})); LiveValidationsPending=@('Temiz Windows 11 kurulum/upgrade/uninstall','Gercek Authenticode ve timestamp','Gercek RDP/SQL/AI endpoint baglantisi'); KnownLimitations=@('ActiveX/COM nedeniyle trimming ve NativeAOT kapali','Self-contained runtime pack ortama baglidir');
    }
    Write-Manifest $staging $manifest
    $hashFile = Join-Path $staging 'SHA256SUMS.txt'; Write-Hashes $staging $hashFile
    $manifest['Sha256ManifestHash'] = (Get-FileHash -LiteralPath $hashFile -Algorithm SHA256).Hash.ToUpperInvariant()
    $manifest['FileCount'] = @(Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object {$_.Name -notin @('SHA256SUMS.txt','release-manifest.json')}).Count
    Write-Manifest $staging $manifest
    & (Join-Path $PSScriptRoot 'Test-NetviumRelease.ps1') -Path $staging -RequireInstaller:$installerBuilt
    if ($LASTEXITCODE -ne 0) { throw 'Local release gate basarisiz.' }
    Move-Item -LiteralPath $staging -Destination $final
    $stageCreated = $false
    Write-Step "Release package ready: $final"
}
catch {
    Write-Error $_
    if ($stageCreated -and $staging -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
    exit 1
}
