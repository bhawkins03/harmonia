# Harmonia

Harmonia is an open-source Windows audio-routing project. The **audio-driver
architecture on `main` is the official development baseline** going forward.
It combines a native virtual playback driver with a C# Windows desktop app.

The minimal driver builds, but the driver-to-app audio transport is not yet
implemented. The driver currently discards playback audio. It is unsigned,
uninstalled, and has not been runtime-tested; this is a development build.
The D2.1 v1 protocol codecs and cross-language tests are implemented; see
[the wire specification](VirtualAudio/PROTOCOL.md) and
[the security approval gate for D2.2](VirtualAudio/D2_SECURITY.md).

## Official build

Requires Windows, .NET 8 SDK, Visual Studio 2026 C++ Build Tools with WDK Build
Tools and Spectre/ATL libraries, and matching Windows SDK/WDK build 28000.
See [driver setup](VirtualAudio/IMPLEMENTATION.md) for details.

From the repository root:

```powershell
./Build.ps1
./Build.ps1 -Configuration Debug
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

Next: secure, bounded PCM transport to the app, routing-engine integration,
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
