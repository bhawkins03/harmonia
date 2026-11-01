[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [switch]$Analyze)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    & dotnet build Harmonia.csproj --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Desktop application build failed.' }
    & ./VirtualAudio/scripts/Test-EndpointProfile.ps1
    & ./VirtualAudio/scripts/Test-TransportProtocol.ps1 -Configuration $Configuration
    & ./VirtualAudio/scripts/Test-TransportSecurity.ps1
    & ./VirtualAudio/scripts/Build-Harmonia.ps1 -Configuration $Configuration -Analyze:$Analyze
    Write-Output "Official Harmonia development build passed: $Configuration x64. Driver is unsigned and not installed."
} finally {
    Pop-Location
}
