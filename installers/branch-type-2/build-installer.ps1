[CmdletBinding()] param([string]$Version='0.3.0', [uri]$ApiBaseUrl='https://ascendyz.xyz/api/v1', [switch]$Production)
$ErrorActionPreference='Stop'
$root=Resolve-Path "$PSScriptRoot\..\.."
$stage=Join-Path $PSScriptRoot 'staging\win-x64'
$out=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $stage,$out | Out-Null
dotnet publish "$root\apps\branch-type-2\SugarERP.Branch2.csproj" -c Release -r win-x64 --self-contained true -o $stage -p:Version=$Version -p:SugarErpApiBaseUrl=$($ApiBaseUrl.AbsoluteUri.TrimEnd('/')) -p:SugarErpEnvironment=Production -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE){throw 'dotnet publish failed'}
$forbidden=Get-ChildItem $stage -Recurse -File | Where-Object {$_.Name -match '\.(db|sqlite|key|pem|pfx|xlsx|csv)$'}
if($forbidden){throw "Forbidden runtime data in installer: $($forbidden.FullName -join ', ')"}
& "$PSScriptRoot\..\shared\Sign-Artifact.ps1" -Path "$stage\SugarERP.Branch2.Desktop.exe" -Production:$Production
$compiler=@((Join-Path $env:TEMP 'nsis-3.12-tools\nsis-3.12\Bin\makensis.exe'),(Join-Path $env:TEMP 'nsis-3.11-kitchen\nsis-3.11\Bin\makensis.exe')) | Where-Object {Test-Path $_} | Select-Object -First 1
if(!$compiler){$compiler=(Get-Command makensis.exe -ErrorAction Stop).Source}
& $compiler "/DPUBLISH_DIR=$stage" "/DOUTPUT_DIR=$out" "/DAPP_VERSION=$Version" "$PSScriptRoot\BranchType2.nsi"
if($LASTEXITCODE){throw 'NSIS failed'}
& "$PSScriptRoot\..\shared\Sign-Artifact.ps1" -Path "$out\Sugar-Branch2-$Version.exe" -Production:$Production
if($Production){ & "$PSScriptRoot\..\shared\Write-ReleaseManifest.ps1" -Artifact "$out\Sugar-Branch2-$Version.exe" -Version $Version -Output "$out\current.json" -ReleaseNotes 'Branch Type 2 production release with touch workflow and immutable day close' }
Get-ChildItem $out\*.exe | Select-Object FullName,Length
