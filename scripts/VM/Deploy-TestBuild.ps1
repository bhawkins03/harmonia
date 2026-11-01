[CmdletBinding()]
param(
    [Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$PassThru
)
. "$PSScriptRoot/Common.ps1"
$vm = Assert-BenchSession $Session
$checkpoint = Get-BenchCheckpoint $vm
$source = (Resolve-Path (Join-Path $PSScriptRoot "../../VirtualAudio/driver/Package/x64/$Configuration/HarmoniaPackage")).Path
$files = foreach ($name in 'HarmoniaVirtualAudio.inf','HarmoniaVirtualAudio.sys','HarmoniaVirtualAudio.cat') {
    $item = Get-Item -LiteralPath (Join-Path $source $name)
    if (!$item.Length) { throw "Empty artifact: $name" }
    $item
}
$manifest = @($files | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Length=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$destination = 'C:\HarmoniaTest\Builds\' + $Configuration + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
Invoke-Command -Session $Session -ArgumentList $destination -ScriptBlock {
    param($destination)
    $ErrorActionPreference = 'Stop'
    if (!(Test-Path -LiteralPath 'C:\HarmoniaTest' -PathType Container)) { throw 'Guest test directory is missing.' }
    New-Item -ItemType Directory -Path $destination -ErrorAction Stop | Out-Null
}
foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination $destination -ToSession $Session -ErrorAction Stop }
Invoke-Command -Session $Session -ArgumentList $destination,$manifest,([string]$checkpoint.Id) -ScriptBlock {
    param($destination,$manifest,$checkpointId)
    $ErrorActionPreference = 'Stop'
    foreach ($entry in $manifest) {
        $path = Join-Path $destination $entry.Name
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256 -or (Get-Item -LiteralPath $path).Length -ne $entry.Length) { throw "Transfer verification failed: $path" }
    }
    @{ CheckpointId=$checkpointId; CopiedUtc=[DateTime]::UtcNow.ToString('o'); Files=$manifest } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding UTF8
}
if ($PassThru) { [pscustomobject]@{ Path=$destination; Manifest=$manifest } } else { Write-Output "Verified package copied to $destination. Nothing installed." }
