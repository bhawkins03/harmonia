[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [switch]$Analyze,[switch]$TestsInVm,[switch]$SkipDriver)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    & dotnet build Harmonia.csproj --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Desktop application build failed.' }
    foreach($project in 'Suite/Service/Harmonia.Suite.Service.csproj','Suite/Installer/Harmonia.Suite.Installer.csproj','Suite/Tests/Harmonia.Suite.Tests.csproj') {
        & dotnet build $project -c $Configuration
        if($LASTEXITCODE){throw "Suite component build failed: $project"}
    }
    if(!$TestsInVm) {
        & dotnet run --project Suite/Tests/Harmonia.Suite.Tests.csproj -c $Configuration --no-build
        if($LASTEXITCODE){throw 'Suite routing/update tests failed.'}
    }
    & ./VirtualAudio/scripts/Test-EndpointProfile.ps1
    & ./VirtualAudio/scripts/Test-TransportProtocol.ps1 -Configuration $Configuration -CompileOnly:$TestsInVm
    & ./VirtualAudio/scripts/Test-TransportSecurity.ps1
    if(!$SkipDriver){ & ./VirtualAudio/scripts/Build-Harmonia.ps1 -Configuration $Configuration -Analyze:$Analyze }
    if($TestsInVm){Write-Output 'Test execution explicitly deferred to Test-SuiteService.ps1 in Harmonia Test Bench; this build does not report runtime passes.'}
    Write-Output "Official Harmonia Suite source build passed: $Configuration x64. No driver or service was installed."
} finally {
    Pop-Location
}
