# D0 validation: 2026-10-31

Status: D0 passed after toolchain installation; no driver installed or deployed.
Earlier blocked attempts below are retained as historical evidence.

## Script changes

- Prepare-SysVad.ps1 now passes each complete argument array using the named
  GitArgs parameter. Positional array splatting into this single array parameter
  does not reliably preserve the complete command.
- Build-SysVad.ps1 retains native argument-array splatting (including paths with
  spaces) and now requests diagnostic file logging, alongside its binary log
  and build metadata. No shell command strings are constructed.

## Validation

The corrected preparation script completed against the existing clean reference
at `S:\AI Coding tests\sysvad-reference`. HEAD remained
`f4c1ff842b6a0001152f44cb61788b216a1c7627`, WIL remained pinned to
`3c00e7f1d8cf9930bbb8e5be3ef0df65c84e8928`, and the checkout remained clean.

PowerShell's default execution policy prevents direct script-file invocation.
Validation executed the reviewed script text as an in-memory ScriptBlock with
PSScriptRoot substituted by its script directory. No execution policy was changed.

Build wrapper preflight produced:

```text
Native MSBuild/C++ tools missing. Install the documented VS + SDK + WDK prerequisites. dotnet build cannot build SysVAD.
```

No compiler ran, so there is no native build log or compiler error to report.
Future builds produce diagnostic .log, .binlog, and metadata .json files under
VirtualAudio/.work/logs by default.

## Toolchain inspection

No Visual Studio Installer/vswhere, native C++ compiler, or native MSBuild was
found in the standard Visual Studio installation locations. Recursive cl.exe
searches covered both Program Files directories and S:\AI Coding tests.
The existing Program Files MSBuild folders did not contain MSBuild.exe.
No Visual Studio package-instance registration, installed-app SDK/WDK/Visual
Studio entry, or Windows Kits Installed Roots registration was found.
The standard Windows Kits locations were absent. Other visible drive roots
showed no apparent custom toolchain installation. Arbitrary custom locations
have not been exhaustively searched.

.NET SDK 8.0.427 is present; its MSBuild is not a native WDK build environment.

## Required installation before retry

Use Microsoft's supported Visual Studio 2026 Community/Professional/Enterprise
setup for WDK 28000. Select Desktop development with C++, including MSVC x64/x86
tools and MSBuild. Add Windows Driver Kit integration, x64/x86 Spectre-mitigated
MSVC libraries, ATL and ATL with Spectre mitigations. Microsoft's full setup
also lists MFC with Spectre mitigations and ARM64 equivalents; ARM64 is outside
this x64 D0 build.

Install the Windows SDK and WDK from the matching 28000 download links:
https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk

The current WDK release is 28000.2526. The manifest's 10.0.28000.0 is the kit
directory/target version; SDK/WDK build numbers must match, but QFE revisions
need not. Verify Include/10.0.28000.0/um/Windows.h and km/ntddk.h, the corresponding
x64 libraries, and WDK MSBuild toolset integration before retrying.

Do not substitute a 26100 kit without reviewing and changing the pin. Do not
install drivers, certificates, provision a target, or change audio/security
settings for D0. No changes were pushed during this validation.

## Retry after Build Tools installation

Build preflight was retried on 2026-10-31. Visual Studio Build Tools 2026
18.11.12328.437 is now installed, with native MSBuild and MSVC 14.51.36231.
The installed Windows SDK is 10.0.26100.0. The configured build still targets
10.0.28000.0 and fails preflight with:

```text
Missing matching SDK/WDK header: um/Windows.h (10.0.28000.0).
```

The installed 26100 SDK also lacks `km/ntddk.h`, kernel libraries, and the
Windows Kits `build` directory. No WDK is present in the registered kit root.
Build Tools alone therefore does not yet provide the driver toolchain.
Install the matching 28000 SDK/WDK and Visual Studio WDK integration before
retrying the pinned build. No compiler ran and no driver was produced.

## Successful toolchain installation and D0 builds

Installed from Microsoft's official, signature-verified installers:
- Windows SDK 10.0.28000.2957.
- Windows WDK 10.0.28000.2526.
- Visual Studio `Component.Microsoft.Windows.DriverKit.BuildTools`, including
  its Spectre/ATL/MFC dependencies, plus the ATL component.

All installer steps returned 0; no restart was requested. Installer logs are in
`VirtualAudio/.work/installers/` and Visual Studio's user Temp directory.

Prepared the pinned clean SysVAD/WIL sources in
`VirtualAudio/.work/Windows-driver-samples`. Default sandbox compilation hit
MSBuild FileTracker access restrictions. An unrestricted build compiled the
binary but the 32-bit MSBuild host failed WDK INF/API validation. Selecting
`MSBuild/Current/Bin/amd64/MSBuild.exe` resolved those validation failures;
Build-SysVad.ps1 now discovers that host by default.

Both complete solution builds passed with **0 warnings and 0 errors**, signing
off and deployment disabled:
- Debug: `.work/logs/sysvad-Debug-x64-20261031-192305.{log,binlog,json}`.
- Release: `.work/logs/sysvad-Release-x64-20261031-192331.{log,binlog,json}`.

Packaged upstream driver binaries and INFs are under
`.work/Windows-driver-samples/audio/sysvad/x64/{Debug,Release}/package/`.
The driver binary is `TabletAudioSample.sys`. These are reference sample
artifacts, not a Harmonia-specific endpoint. D1 adaptation and D2 transport
remain unimplemented. No driver, certificate, or boot-setting changes were made.
