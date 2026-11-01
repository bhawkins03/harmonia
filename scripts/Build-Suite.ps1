[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [switch]$SkipDriver,[switch]$UiInVm)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    & "$root/Build.ps1" -Configuration $Configuration -TestsInVm:$UiInVm -SkipDriver:$SkipDriver
    if($UiInVm) {
        & dotnet build Suite/UiTests/Harmonia.Suite.UiTests.csproj -c $Configuration
        if($LASTEXITCODE){throw 'Suite UI test build failed.'}
        'UI execution deferred explicitly to Test-SuiteService.ps1 in Harmonia Test Bench.'
    } else {
        & dotnet run --project Suite/UiTests/Harmonia.Suite.UiTests.csproj -c $Configuration
        if($LASTEXITCODE){throw 'Suite interface/persistence tests failed.'}
    }
    & dotnet build Harmonia.csproj -c $Configuration -p:LiteEdition=true -o (Join-Path $root "Suite/.work/LiteRegression/$Configuration")
    if($LASTEXITCODE){throw 'Independent Lite build regressed.'}
    $lite=Join-Path $root "Suite/.work/LiteRegression/$Configuration"
    if((Get-Content (Join-Path $lite 'Harmonia.deps.json') -Raw) -match 'Harmonia\.(Suite|Transport)'){throw 'Suite dependencies leaked into Lite.'}
    # The unchanged Lite workflow reads the single literal <Version> tag.
    $plainVersions=[regex]::Matches((Get-Content Harmonia.csproj -Raw),'(?<=<Version>)[^<]+')
    if($plainVersions.Count -ne 1 -or $plainVersions[0].Value -ne '0.2.1'){throw 'Shared Suite metadata changed Lite release version detection.'}
    if(Get-ChildItem $lite -Recurse -File | Where-Object Extension -in '.sys','.inf','.cat'){throw 'Kernel artifacts leaked into Lite.'}
    & "$root/scripts/VM/Test-Workflow.ps1"
    if($UiInVm){'PASS: Suite/driver/test compilation, offline ACL checks, workflow guards and independent Lite compilation. Runtime results must come from the VM logs.'}
    else {'PASS: Suite service, installer, deterministic tests, UI, and independent Lite compilation.'}
} finally { Pop-Location }
