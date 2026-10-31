# Harmonia

**Open-source Windows audio management (early prototype)**

Harmonia aims to unify audio routing, mixing, EQ, device synchronization and eventually custom surround speaker layouts. The first milestone mirrors the left channel of the default Windows stereo mix to one DisplayPort monitor, and the right channel to another monitor. Each selected channel is duplicated to both speakers inside its monitor.

> Experimental, source-only prototype. Not yet tested on target hardware. Expect latency and possible clock drift between monitors.

## Core principle

**Do not change Windows audio configuration.** This prototype does not change default playback devices, system volume, per-app routing or the registry. Stopping Harmonia ends its own additional audio playback.

**Limitation:** the existing default Windows output remains audible if its speakers are active; this prototype mirrors sound and does not replace the default output.

## Run

Prerequisites: Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

From the folder containing `Harmonia.csproj`:

```cmd
dotnet restore Harmonia.csproj
dotnet run --project Harmonia.csproj
```

Select two distinct playback endpoints that are **not** the existing Windows default playback endpoint. Press Enter to stop.

```cmd
dotnet run --project Harmonia.csproj -- --list
dotnet run --project Harmonia.csproj -- --reset
```

The app stores its own selected endpoint IDs at `%LOCALAPPDATA%\Harmonia\settings.json`; it never changes Windows sound settings.

Smart App Control may block unsigned executables. Do not disable Windows security just to run this prototype.

## Design

C# / .NET 8 / NAudio 2.2.1, with WASAPI loopback capture and two independent shared-mode WASAPI playback devices.

## Limitations

- No GUI or output-device clock synchronization yet.
- Independent monitor clocks can drift; buffering can cause audible latency.
- Only stereo capture formats PCM 16/24/32 and 32-bit float have initial handling.
- A source output with audible speakers remains audible.
- No hot-plug recovery or channel-test controls.
- The prototype has not been compiled or validated against actual hardware here.

See [ROADMAP.md](ROADMAP.md) for development priorities and [CONTRIBUTING.md](CONTRIBUTING.md) for contributions.