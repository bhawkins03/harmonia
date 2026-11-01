# Harmonia

## Download Harmonia Lite

**[Download Harmonia Lite v0.2.1](https://github.com/bhawkins03/harmonia/releases/tag/v0.2.1-lite)** · Windows x64 · Portable application

Lite focuses on the original goal: **send stereo left and right channels
to two separate Windows playback devices** (for example, two monitor speakers).
Choose one device for LEFT and another for RIGHT, adjust each volume
independently, then click **Start routing**. Your selections are remembered.

Download the ZIP, extract, and double-click `Harmonia.exe`. No developer
toolchain, Git, PowerShell, .NET runtime installation or kernel driver needed.
The Windows default output is the *source* for WASAPI loopback; it cannot also
be one of the two selected outputs. Lite does not offer 5.1, center, subwoofer,
rear surround, synthetic upmix, speaker-map editing, or driver installation.
Separate physical device clocks may drift. This unsigned release may prompt
a Windows reputation warning.

**Update checks (v0.2.1):** Lite checks the project's GitHub Releases at startup, asks before downloading a newer Lite version, and lets you disable the automatic check or run it manually inside the app. Settings stay in your Windows user profile. This is a portable unsigned updater, not a signed installer.\n\n## Harmonia Suite

Suite is the ongoing full project for a selectable Windows virtual audio
endpoint, native multichannel audio and advanced multi-device routing.
**It is not yet a tested end-user download.** Driver and service development
continues independently from Lite.

See [edition architecture](docs/EDITIONS.md).

Harmonia is an open-source Windows audio-routing project. The **audio-driver
architecture on `main` is the official development baseline** going forward.
It combines a native virtual playback driver with a C# Windows desktop app.

The official **Suite main development build** includes the VM-validated D2.2
driver, bounded kernel PCM queue, secured v1 control interface, and C++/C#
protocol tests. On November 1, 2026, signed VM tests passed six-channel PCM
export through SYSTEM and restricted LocalService, unauthorized-access denial,
and simulated CopyProtect denial. The normal Release driver was restored and
retested. Export requires positively verified rights and fresh packet commits.
The desktop app does not consume this transport yet; service/client and routing
integration remain the next milestone. Source builds produce unsigned packages;
VM development signing is separate from production signing.
See [D2.2 audit and limitations](VirtualAudio/D2_2_AUDIT.md),
[validation status](VirtualAudio/D2_2_STATUS.md), and
[the approved security policy](VirtualAudio/D2_SECURITY.md).

All further development and driver/runtime testing will take place in a VM.
See the [VM handoff](VirtualAudio/VM_HANDOFF.md) for setup, build commands and
remaining validation. These driver tests do not establish end-to-end Suite or
production readiness. Lite's branches, release workflow and driverless backend
are independent of this Suite milestone.

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

Next: Suite service/client and routing integration, Driver Verifier and lifecycle
stress, latency/timestamp validation, and production signing/installation packaging.

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
