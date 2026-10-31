[CmdletBinding()]
param(
    [string]$Source,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$MSBuildPath,
    [string]$KitRoot = "${env:ProgramFiles(x86)}/Windows Kits/10",
    [string]$LogDirectory
)
$ErrorActionPreference = 'Stop'
if (!$Source) { $Source = Join-Path $PSScriptRoot '../.work/Windows-driver-samples' }
if (!$LogDirectory) { $LogDirectory = Join-Path $PSScriptRoot '../.work/logs' }
$pin = Get-Content (Join-Path $PSScriptRoot '../upstream.json') -Raw | ConvertFrom-Json
if (!$MSBuildPath) {
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    if (Test-Path $vswhere) {
        $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }
}
if (!$MSBuildPath -or !(Test-Path $MSBuildPath)) {
    throw 'Native MSBuild/C++ tools missing. Install the documented VS + SDK + WDK prerequisites. dotnet build cannot build SysVAD.'
}
foreach ($header in @('um/Windows.h', 'km/ntddk.h')) {
    if (!(Test-Path (Join-Path $KitRoot "Include/$($pin.kitVersion)/$header"))) {
        throw "Missing matching SDK/WDK header: $header ($($pin.kitVersion))."
    }
}
$head = & git -C $Source rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $pin.commit) { throw 'Run Prepare-SysVad.ps1: reference commit mismatch.' }
$dirty = & git -C $Source status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Tracked reference files must be unmodified, including WIL.' }
$wil = & git -C (Join-Path $Source 'wil') rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $wil -ne $pin.wilCommit) { throw 'WIL missing or revision mismatch.' }
New-Item -ItemType Directory -Force $LogDirectory | Out-Null
$LogDirectory = [IO.Path]::GetFullPath($LogDirectory)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $LogDirectory "sysvad-$Configuration-x64-$stamp"
$buildArgs = @((Join-Path $Source $pin.solution), '/t:Build', '/m', '/nr:false',
    "/p:Configuration=$Configuration", '/p:Platform=x64',
    "/p:WindowsTargetPlatformVersion=$($pin.kitVersion)",
    '/p:SignMode=Off', '/p:EnableDeployment=false', "/bl:$log.binlog", '/fl', "/flp:logfile=$log.log;verbosity=normal")
@{ upstream = $pin; msbuild = $MSBuildPath; msbuildVersion = (& $MSBuildPath -version -nologo | Out-String).Trim();
   configuration = $Configuration; platform = 'x64'; arguments = $buildArgs; startedUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 5 | Set-Content "$log.json"
& $MSBuildPath @buildArgs
if ($LASTEXITCODE -ne 0) { throw "SysVAD build failed. See $log.log" }
Write-Output "Build completed, unsigned and not deployed. Evidence: $log"
