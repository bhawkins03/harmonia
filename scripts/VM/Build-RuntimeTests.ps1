[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'MSVC x64 tools required.' }
$output=Join-Path $root 'VirtualAudio/.work/tests/runtime'
New-Item -ItemType Directory -Force $output | Out-Null
$developer=Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
$source=Join-Path $root 'VirtualAudio/tests/RuntimeTransportTests.cpp'
$audioSource=Join-Path $root 'VirtualAudio/tests/RuntimeAudioTests.cpp'
$ksSource=Join-Path $root 'VirtualAudio/tests/RuntimeKsTests.cpp'
$rightsSource=Join-Path $root 'VirtualAudio/tests/ContentRightsPolicyTests.cpp'
$batch=Join-Path $output 'compile.cmd'
@"
@echo off
call "$developer" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /permissive- /MT /O2 /Fo"$output/runtime.obj" /Fe"$output/RuntimeTransportTests.exe" "$source" /link /INCREMENTAL:NO advapi32.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /permissive- /MT /O2 /Fo"$output/audio.obj" /Fe"$output/RuntimeAudioTests.exe" "$audioSource" /link /INCREMENTAL:NO ole32.lib uuid.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /permissive- /MT /O2 /Fo"$output/ks.obj" /Fe"$output/RuntimeKsTests.exe" "$ksSource" /link /INCREMENTAL:NO setupapi.lib ole32.lib uuid.lib ksuser.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /permissive- /MT /O2 /Fo"$output/rights.obj" /Fe"$output/ContentRightsPolicyTests.exe" "$rightsSource" /link /INCREMENTAL:NO
exit /b %errorlevel%
"@ | Set-Content $batch
& $env:ComSpec /d /c $batch
if ($LASTEXITCODE) { throw 'Runtime probe compilation failed.' }
Write-Output (Join-Path $output 'RuntimeTransportTests.exe')
