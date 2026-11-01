[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Debug', [string]$MSBuildPath, [switch]$Analyze, [switch]$SimulateProtected)
$ErrorActionPreference='Stop'
if ($SimulateProtected -and $Configuration -ne 'Debug') { throw 'Protection simulation requires Debug; never use it for Release.' }
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pin=Get-Content (Join-Path $root 'upstream.json') -Raw | ConvertFrom-Json
if (!$MSBuildPath) {
    $vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $MSBuildPath=& $vswhere -latest -products '*' -requires Component.Microsoft.Windows.DriverKit.BuildTools -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
}
if (!$MSBuildPath -or !(Test-Path $MSBuildPath)) { throw '64-bit native MSBuild with WDK Build Tools is required.' }
$logs=Join-Path $root '.work/logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$log=Join-Path $logs ("harmonia-$Configuration-x64-"+(Get-Date -Format yyyyMMdd-HHmmss))
$target=if($Analyze -or $SimulateProtected){'/t:Rebuild'}else{'/t:Build'}
$buildArgs=@((Join-Path $root 'driver/Package/HarmoniaPackage.vcxproj'), $target, '/m', '/nr:false',
    "/p:Configuration=$Configuration", '/p:Platform=x64', "/p:WindowsTargetPlatformVersion=$($pin.kitVersion)",
    '/p:SignMode=Off', '/p:EnableDeployment=false', '/v:minimal', "/bl:$log.binlog", '/fl', "/flp:logfile=$log.log;verbosity=normal")
if($Analyze){$buildArgs+='/p:RunCodeAnalysis=true'}
$buildArgs+="/p:HarmoniaSimulateProtection=$([bool]$SimulateProtected)"
if($SimulateProtected){Write-Warning 'DEBUG VM TEST BUILD: CopyProtect simulation denies all PCM export.'}
@{upstream=$pin;msbuild=$MSBuildPath;arguments=$buildArgs;startedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 5 | Set-Content "$log.json"
& $MSBuildPath @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Harmonia build failed. See $log.log" }
Write-Output "Harmonia $Configuration x64 build passed. Unsigned; not deployed. Logs: $log"
