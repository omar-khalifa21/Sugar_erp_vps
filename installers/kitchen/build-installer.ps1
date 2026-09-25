[CmdletBinding()] param([string]$Version='0.1.0', [uri]$ApiBaseUrl='https://ascendyz.xyz/api/v1', [switch]$Production, [switch]$AllowUnsignedInternal)
$ErrorActionPreference='Stop'
$root=Resolve-Path "$PSScriptRoot\..\.."
$stage=Join-Path $PSScriptRoot 'staging\win-x64'
$out=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $stage,$out | Out-Null
dotnet publish "$root\apps\kitchen\SugarERP.Kitchen.csproj" -c Release -r win-x64 --self-contained true -o $stage -p:Version=$Version -p:SugarErpApiBaseUrl=$($ApiBaseUrl.AbsoluteUri.TrimEnd('/')) -p:SugarErpEnvironment=Production -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE){throw 'dotnet publish failed'}
$forbidden=Get-ChildItem $stage -Recurse -File | Where-Object {$_.Name -match '\.(db|sqlite|key|pem|pfx|xlsx|csv)$'}
if($forbidden){throw "Forbidden runtime data in installer: $($forbidden.FullName -join ', ')"}
& "$PSScriptRoot\..\shared\Sign-Artifact.ps1" -Path "$stage\SugarERP.Kitchen.Desktop.exe" -Production:$Production -AllowUnsignedInternal:$AllowUnsignedInternal
$compiler=@((Join-Path $env:TEMP 'nsis-3.12-tools\nsis-3.12\Bin\makensis.exe'),(Join-Path $env:TEMP 'nsis-3.11-kitchen\nsis-3.11\Bin\makensis.exe')) | Where-Object {Test-Path $_} | Select-Object -First 1
if(!$compiler){$compiler=(Get-Command makensis.exe -ErrorAction Stop).Source}
& $compiler "/DPUBLISH_DIR=$stage" "/DOUTPUT_DIR=$out" "/DAPP_VERSION=$Version" "$PSScriptRoot\Kitchen.nsi"
if($LASTEXITCODE){throw 'NSIS failed'}
& "$PSScriptRoot\..\shared\Sign-Artifact.ps1" -Path "$out\Sugar-Kitchen-$Version.exe" -Production:$Production -AllowUnsignedInternal:$AllowUnsignedInternal
if($Production){ & "$PSScriptRoot\..\shared\Write-ReleaseManifest.ps1" -Artifact "$out\Sugar-Kitchen-$Version.exe" -Version $Version -Output "$out\current.json" -ReleaseNotes 'Kitchen overhaul: canonical recipes, exact-once ingredient deductions, Cafe orders, reliable automatic sync, and desktop UI improvements' -AllowUnsignedInternal:$AllowUnsignedInternal }
Get-ChildItem $out\*.exe | Select-Object FullName,Length
