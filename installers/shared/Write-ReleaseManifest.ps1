[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Artifact,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Output,
    [string]$ReleaseNotes = 'Production release',
    [string]$MinimumVersion = '0.0.0',
    [switch]$Required,
    [switch]$AllowUnsignedInternal
)
$ErrorActionPreference = 'Stop'
$signature = Get-AuthenticodeSignature -LiteralPath $Artifact
if ($signature.Status -ne 'Valid' -and -not $AllowUnsignedInternal) { throw "Production manifest refused: Authenticode signature is $($signature.Status)." }
$manifest = [ordered]@{
    channel = 'production'
    version = $Version
    filename = (Split-Path -Leaf $Artifact)
    publishedAt = [DateTimeOffset]::UtcNow.ToString('O')
    sha256 = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    releaseNotes = $ReleaseNotes
    minimumVersion = $MinimumVersion
    required = [bool]$Required
    signed = ($signature.Status -eq 'Valid')
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
Get-Content -LiteralPath $Output
