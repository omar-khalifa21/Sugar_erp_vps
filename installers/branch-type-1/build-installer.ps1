[CmdletBinding()]
param(
    [string]$Version = '0.2.0',
    [string]$Runtime = 'win-x64',
    [uri]$ApiBaseUrl = 'https://ascendyz.xyz/api/v1',
    [switch]$Production
)

$ErrorActionPreference = 'Stop'
$installerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Resolve-Path (Join-Path $installerRoot '..\..')
$projectPath = Join-Path $repositoryRoot 'apps\branch-type-1\SugarERP.Branch1.csproj'
$publishRoot = Join-Path $installerRoot "staging\$Runtime"
$artifactRoot = Join-Path $installerRoot 'artifacts'
$scriptPath = Join-Path $installerRoot 'BranchType1.nsi'

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw 'Version must be a semantic version such as 1.2.3.'
}

New-Item -ItemType Directory -Force -Path $publishRoot, $artifactRoot | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $publishRoot `
    -p:Version=$Version `
    -p:SugarErpApiBaseUrl=$($ApiBaseUrl.AbsoluteUri.TrimEnd('/')) `
    -p:SugarErpEnvironment=Production `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$forbidden = Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Where-Object {
    $_.Name -match '\.(db|sqlite|sqlite3|wal|shm|bak|key|pem|pfx|xlsx|csv)$' -or
    $_.FullName -match '[\\/](Data|exports|backups|keys|secrets)[\\/]'
}
if ($forbidden) {
    $names = ($forbidden.FullName -join [Environment]::NewLine)
    throw "Packaging stopped because runtime data, exports, backups, keys, or credentials were found:`n$names"
}

$compiler = Get-Command makensis.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if (-not $compiler) {
    foreach ($candidate in @((Join-Path $env:ProgramFiles 'NSIS\makensis.exe'), (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'), (Join-Path $env:TEMP 'nsis-3.12-tools\nsis-3.12\Bin\makensis.exe'))) {
        if (Test-Path -LiteralPath $candidate) { $compiler = $candidate; break }
    }
}
if (-not $compiler) { throw 'NSIS 3.12 is required to compile the installers.' }

& "$PSScriptRoot\..\shared\Sign-Artifact.ps1" -Path "$publishRoot\SugarERP.Branch1.exe" -Production:$Production
foreach ($variant in @('Desktop', 'Touch')) {
    $product = if ($variant -eq 'Touch') { 'branch-type-1-touch' } else { 'branch-type-1' }
    $variantRoot = Join-Path $artifactRoot $product
    New-Item -ItemType Directory -Force -Path $variantRoot | Out-Null
    & $compiler "/DPUBLISH_DIR=$publishRoot" "/DOUTPUT_DIR=$variantRoot" "/DAPP_VERSION=$Version" "/DVARIANT=$variant" $scriptPath
    if ($LASTEXITCODE -ne 0) { throw "NSIS compilation failed for $variant." }
    $artifactName = if ($variant -eq 'Touch') { "Sugar-Branch-Type-1-Touch-$Version.exe" } else { "Sugar-Branch-Type-1-$Version.exe" }
    $artifact = Join-Path $variantRoot $artifactName
    & "$PSScriptRoot\..\shared\Sign-Artifact.ps1" -Path $artifact -Production:$Production
    if ($Production) {
        & "$PSScriptRoot\..\shared\Write-ReleaseManifest.ps1" -Artifact $artifact -Version $Version -Output (Join-Path $variantRoot 'current.json') -ReleaseNotes "Branch Type 1 $variant production release"
    }
}

Get-ChildItem -LiteralPath $artifactRoot -Filter '*.exe' -Recurse | Select-Object FullName, Length, LastWriteTime
