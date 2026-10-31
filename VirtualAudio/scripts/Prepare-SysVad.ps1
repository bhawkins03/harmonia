[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference = 'Stop'
if (!$Destination) { $Destination = Join-Path $PSScriptRoot '../.work/Windows-driver-samples' }
$pin = Get-Content (Join-Path $PSScriptRoot '../upstream.json') -Raw | ConvertFrom-Json
function Invoke-Git([string[]]$GitArgs) {
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "git failed: $GitArgs" }
}
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path $Destination) {
    if (!(Test-Path (Join-Path $Destination '.git'))) { throw 'Destination exists and is not a Git checkout.' }
    $origin = & git -C $Destination remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $origin -ne $pin.repository) { throw 'Unexpected origin; refusing to change checkout.' }
    $dirty = & git -C $Destination status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Reference checkout must be clean.' }
} else {
    Invoke-Git @('clone', '--filter=blob:none', '--no-checkout', $pin.repository, $Destination)
}
Invoke-Git @('-C', $Destination, 'sparse-checkout', 'set', 'audio/sysvad')
Invoke-Git @('-C', $Destination, 'checkout', '--detach', $pin.commit)
Invoke-Git @('-C', $Destination, 'submodule', 'update', '--init', '--recursive', '--', 'wil')
$wil = & git -C (Join-Path $Destination 'wil') rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $wil -ne $pin.wilCommit) { throw 'WIL pin mismatch.' }
Write-Output "Prepared unmodified SysVAD at $Destination ($($pin.commit)). Nothing installed."
