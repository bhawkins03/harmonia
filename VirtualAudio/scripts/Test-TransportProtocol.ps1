[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Native MSVC x64 tools are required.' }
$output=Join-Path $root ".work/tests/transport-$Configuration"
New-Item -ItemType Directory -Force $output | Out-Null
$developer=Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
$source=Join-Path $root 'tests/TransportProtocolTests.cpp'
$smoke=Join-Path $root 'tests/TransportKernelCompile.cpp'
$batch=Join-Path $output 'compile.cmd'
$optimization=if($Configuration -eq 'Debug') {'/Od /Zi'} else {'/O2'}
@"
@echo off
call "$developer" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /permissive- /MT $optimization /Fo"$output/native.obj" /Fd"$output/native.pdb" /Fe"$output/native.exe" "$source" /link /INCREMENTAL:NO
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /kernel /c /W4 /WX /Fo"$output/kernel.obj" "$smoke"
exit /b %errorlevel%
"@ | Set-Content $batch
& $env:ComSpec /d /c $batch
if ($LASTEXITCODE -ne 0) { throw 'Native transport test compilation failed.' }
$fixtures=Join-Path $root 'tests/vectors/transport-v1.txt'
& (Join-Path $output 'native.exe') $fixtures $output
if ($LASTEXITCODE -ne 0) { throw 'C++ transport tests failed.' }
& dotnet run --project (Join-Path $root 'tests/TransportProtocol.Tests/TransportProtocol.Tests.csproj') --configuration $Configuration -- $fixtures $output
if ($LASTEXITCODE -ne 0) { throw 'C# transport tests failed.' }
foreach($suffix in @('encoded','fuzz')) {
    $native=[IO.File]::ReadAllLines((Join-Path $output "native-$suffix.txt"))
    $managed=[IO.File]::ReadAllLines((Join-Path $output "managed-$suffix.txt"))
    if (Compare-Object $native $managed -SyncWindow 0) { throw "C++/C# $suffix results differ." }
}
Write-Output "Transport v1 $Configuration passed: C++/C# golden bytes and 10000 mutation results agree; /kernel codec smoke passed."
