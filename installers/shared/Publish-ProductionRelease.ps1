[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidateSet('branch-type-1','branch-type-1-touch','branch-type-2','kitchen')][string]$Product,
    [Parameter(Mandatory)][string]$Artifact,
    [Parameter(Mandatory)][string]$Manifest,
    [string]$Server = 'root@198.199.85.22',
    [string]$RemoteRoot = '/opt/sugar-erp/releases',
    [switch]$AllowUnsignedInternal
)
$ErrorActionPreference = 'Stop'
$signature = Get-AuthenticodeSignature -LiteralPath $Artifact
if ($signature.Status -ne 'Valid' -and -not $AllowUnsignedInternal) { throw "Publishing refused: Authenticode signature is $($signature.Status)." }
$data = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$actualHash = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash.ToLowerInvariant()
if ($data.sha256 -ne $actualHash -or $data.filename -ne (Split-Path -Leaf $Artifact)) { throw 'Manifest does not match the signed artifact.' }
$remote = "$($RemoteRoot.TrimEnd('/'))/$Product"
if ($PSCmdlet.ShouldProcess("$Server`:$remote", "Publish signed installer $($data.filename)")) {
    ssh $Server "install -d -m 0755 '$remote'"
    if ($LASTEXITCODE) { throw 'Could not prepare the release directory.' }
    scp -- $Artifact "$Server`:$remote/$($data.filename).uploading"
    if ($LASTEXITCODE) { throw 'Artifact upload failed.' }
    scp -- $Manifest "$Server`:$remote/current.json.uploading"
    if ($LASTEXITCODE) { throw 'Manifest upload failed.' }
    ssh $Server "set -eu; cd '$remote'; echo '$actualHash  $($data.filename).uploading' | sha256sum -c -; mv '$($data.filename).uploading' '$($data.filename)'; mv current.json.uploading current.json; chmod 0644 '$($data.filename)' current.json"
    if ($LASTEXITCODE) { throw 'Remote verification or activation failed.' }
}
