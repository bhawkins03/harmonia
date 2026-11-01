[CmdletBinding()]
param(
    [ValidateSet('Lite', 'Suite')][string]$Edition = 'Lite',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$ExperimentalSuite
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "$Edition-$Runtime"
$zip = Join-Path $dist "Harmonia-$Edition-$Runtime.zip"
if ($Edition -eq 'Suite') {
    if (!$ExperimentalSuite) {
        throw 'Suite is not a functional release. Pass -ExperimentalSuite only for an explicitly marked unsigned, non-installable developer archive.'
    }
    if ($Runtime -ne 'win-x64') { throw 'The current native driver supports x64 only.' }
}
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Push-Location $root
try {
    if ($Edition -eq 'Suite') {
        & ./Build.ps1 -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Suite development build failed.' }
    }
    $publishArgs = @('publish', 'Harmonia.csproj', '--configuration', $Configuration,
        '--runtime', $Runtime, '--self-contained', "$($SelfContained.IsPresent)".ToLowerInvariant(),
        '--output', (Join-Path $stage 'App'))
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    if ($Edition -eq 'Suite') {
        $package = Join-Path $root "VirtualAudio/driver/Package/x64/$Configuration/HarmoniaPackage"
        if (!(Test-Path $package)) { throw "Missing driver build output: $package" }
        $driverFiles = @('HarmoniaVirtualAudio.sys','HarmoniaVirtualAudio.inf','harmoniavirtualaudio.cat')
        $driverTarget = Join-Path $stage 'ExperimentalUnsignedDriver'
        New-Item -ItemType Directory -Force $driverTarget | Out-Null
        foreach ($name in $driverFiles) {
            $source = Join-Path $package $name
            if (!(Test-Path $source)) { throw "Missing driver artifact: $source" }
            Copy-Item -LiteralPath $source -Destination $driverTarget
        }
    }
    $notice = if ($Edition -eq 'Lite') {
        @'
HARMONIA LITE - LOOPBACK EDITION
No kernel audio driver is included or required.
Uses the experimental WASAPI default-render-endpoint loopback backend.
Requires a compatible Windows runtime and available audio devices.
Does not provide a virtual 5.1 playback device or guaranteed discrete 5.1 input.
Requires .NET 8 Desktop Runtime when not published self-contained.
'@
    } else {
        @'
HARMONIA SUITE - EXPERIMENTAL DEVELOPMENT ARCHIVE ONLY
NOT A FUNCTIONAL AUDIO PRODUCT OR INSTALLER.
Contains the existing loopback desktop app and an unsigned experimental driver
for authorized VM-only development. This legacy archive omits the Suite transport
service; use Package-SuiteDevelopment.ps1 for current VM integration.
The driver must NOT be installed on a daily-use PC. It currently cannot
deliver sound from the virtual endpoint into the desktop app.
Nothing in this archive installs a driver, certificate or service.
'@
    }
    Set-Content -LiteralPath (Join-Path $stage 'READ-ME-FIRST.txt') -Value $notice -Encoding UTF8
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Output "Created $Edition archive: $zip"
} finally {
    Pop-Location
}
