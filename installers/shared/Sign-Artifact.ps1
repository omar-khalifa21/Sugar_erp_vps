[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path, [switch]$Production)
$ErrorActionPreference = 'Stop'
if (-not $env:SUGAR_SIGN_CERT_THUMBPRINT) {
    if ($Production) { throw 'Production release requires SUGAR_SIGN_CERT_THUMBPRINT for a legitimate installed code-signing certificate.' }
    Write-Warning 'Unsigned development artifact; production promotion is prohibited.'
    return
}
$signTool = if ($env:SUGAR_SIGNTOOL_PATH) { $env:SUGAR_SIGNTOOL_PATH } else { (Get-Command signtool.exe -ErrorAction Stop).Source }
$timestamp = if ($env:SUGAR_SIGN_TIMESTAMP_URL) { $env:SUGAR_SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
& $signTool sign /sha1 $env:SUGAR_SIGN_CERT_THUMBPRINT /fd SHA256 /tr $timestamp /td SHA256 $Path
if ($LASTEXITCODE) { throw 'Authenticode signing failed.' }
& $signTool verify /pa /all $Path
if ($LASTEXITCODE) { throw 'Authenticode verification failed.' }
