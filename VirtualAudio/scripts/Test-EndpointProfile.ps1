[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Native MSVC x64 tools are required for format policy tests.' }
$compiler=Get-ChildItem (Join-Path $installation 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'bin/Hostx64/x64/cl.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$compiler) { throw 'Cannot find the x64 MSVC compiler.' }
$output=Join-Path $root '.work/tests'
New-Item -ItemType Directory -Force $output | Out-Null
& $compiler /nologo /c /std:c++17 /W4 /WX ("/Fo"+(Join-Path $output 'EndpointProfileTests.obj')) (Join-Path $root 'tests/EndpointProfileTests.cpp')
if ($LASTEXITCODE -ne 0) { throw 'Native format policy tests failed.' }
Write-Output 'Native format policy checks passed.'
