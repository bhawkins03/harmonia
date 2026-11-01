# Harmonia

## Choose an edition

| Edition | For whom | Download / status |
| --- | --- | --- |
| **Harmonia Lite** | Friends and Windows users wanting driverless, experimental multi-device audio routing using WASAPI loopback | [Download the latest Lite preview](https://github.com/bhawkins03/harmonia/releases/tag/v0.1.0-lite-preview) (Windows x64 portable ZIP) |
| **Harmonia Suite** | Users who want a selectable virtual Windows 5.1 render device and native discrete-channel routing | **In development — no functional public installer yet.** |

**Lite branding:** The portable EXE, window title bar, system tray and app header now use the white Harmonia logo, with transparent artwork generated into an ICO during the Windows build.\n\n**Using Lite:** Download `Harmonia-Lite-win-x64.zip`, extract it, and double-click
`Harmonia.exe`. No Git, PowerShell, .NET SDK, or separate .NET runtime is needed
for the self-contained build. No driver installation is required. The preview
is unsigned, so Windows SmartScreen may warn; don't bypass warnings unless you
trust the code and downloaded file.

Lite captures the Windows default playback endpoint using WASAPI loopback,
so that same endpoint cannot simultaneously be a routed output. It does not
create a virtual audio device or guarantee native 5.1 input; independent output
clocks can drift. **Suite is not yet ready for end-user use.**

See [edition architecture and packaging](docs/EDITIONS.md) for what is included
in each edition, known limitations, and release requirements.

Harmonia is an open-source Windows audio-routing project. The **audio-driver
architecture on `main` is the official development baseline** going forward.
It combines a native virtual playback driver with a C# Windows desktop app.

The main development build includes the D2.2 bounded kernel PCM queue and secured v1 control interface,
plus C++/C# protocol tests. The driver is unsigned, uninstalled, and has not been
runtime-tested. Export fails closed without positively verified rights and fresh
packet commits. The desktop app does not consume this transport yet.
See [D2.2 audit and limitations](VirtualAudio/D2_2_AUDIT.md),
[validation status](VirtualAudio/D2_2_STATUS.md), and
[the approved security policy](VirtualAudio/D2_SECURITY.md).

All further development and driver/runtime testing will take place in a VM.
See the [VM handoff](VirtualAudio/VM_HANDOFF.md) for setup, build commands and
remaining validation. Promotion to main does not establish working audio export.

## Official build

Requires Windows, .NET 8 SDK, Visual Studio 2026 C++ Build Tools with WDK Build
Tools and Spectre/ATL libraries, and matching Windows SDK/WDK build 28000.
See [driver setup](VirtualAudio/IMPLEMENTATION.md) for details.

From the repository root:

```powershell
./Build.ps1
./Build.ps1 -Configuration Debug
./Build.ps1 -Configuration Release -Analyze
```

The default Release build compiles the desktop app, compiles the native format
policy and C++/C# transport tests, and builds/validates the unsigned x64 driver package. It does not
install the driver, add certificates, or change Windows audio settings.

App output: `bin/Release/net8.0-windows/`.
Driver package: `VirtualAudio/driver/Package/x64/Release/HarmoniaPackage/`.
The package contains `HarmoniaVirtualAudio.sys`, its INF and unsigned catalog.
Build logs and metadata are under ignored `VirtualAudio/.work/logs/`.

## Driver baseline

- Device name: **Harmonia Virtual Audio**; ID: `ROOT\HarmoniaVirtualAudio`.
- Stereo and discrete 5.1 layouts at 48 kHz Float32.
- One render stream; no capture endpoints, offload, or sample effects.
- Debug/Release compilation and WDK package checks pass locally.

See [D1 validation and limitations](VirtualAudio/D1_STATUS.md) and the
[driver implementation plan](VirtualAudio/IMPLEMENTATION.md).

Next: signed isolated-environment transport validation, service/client and routing integration,
Windows audio/runtime validation, and signing/installation packaging.

## Desktop app

The existing WinForms app remains available for experimental WASAPI loopback
routing, speaker layouts, meters, channel tests, and recovery controls:

```powershell
dotnet run --project Harmonia.csproj
```

This backend still captures the existing Windows default output. It does not
consume the new virtual driver's audio yet. Independent output clocks can
drift; stereo upmix is distinct from native 5.1 input. No real-hardware audio
validation is claimed. See [native surround notes](docs/NATIVE_SURROUND.md).

Driver source derives from Microsoft's MIT-licensed SysVAD sample; its license
and notices are retained in `VirtualAudio/driver/`. See [LICENSE](LICENSE),
[ROADMAP.md](ROADMAP.md), and [CONTRIBUTING.md](CONTRIBUTING.md).
