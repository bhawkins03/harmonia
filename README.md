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

## Speaker layouts (experimental feature branch)

The `feature/speaker-layouts` branch introduces a graphical speaker-layout selector and a top-down listener-centered display, rendered with native Windows drawing primitives.

| Layout | Playback endpoints | Routing |
| --- | --- | --- |
| Dual monitor (stereo) | Front left + front right | Left channel on left monitor's two speakers; right channel on right monitor's two speakers |
| Triple monitor (stereo + center) | Front left + front right + center | Left and right as above, center gets (L+R)/2 |
| (Beta) Surround 5.1 | Front left, front right, center, one stereo rear device; optional separate LFE | Center=(L+R)/2; rear stereo=(L-R)/2 and (R-L)/2; LFE is a simple low-pass mono approximation |

**Important:** This is a stereo-to-surround experiment, NOT native/discrete 5.1 decoding, and is not calibrated. It may sound odd with mono audio, which cancels in the difference-derived rear speakers. The rear device gets two distinct channels on a single stereo playback endpoint. The subwoofer endpoint is optional and requires its own dedicated Windows playback device. If absent, this is effectively a 5.0 output layout, despite the beta selector label.

Each selected playback endpoint must be distinct, and none may be the current Windows default playback device; the default source continues playing normally.

To test before merging the branch:

```cmd
git fetch origin
git switch feature/speaker-layouts
dotnet restore
dotnet run
```

The UI includes a simplified, schematic speaker-position map (not a distance-calibrated room model). It uses custom GDI+ vector shapes requiring no third-party asset licenses. Real device synchronization, safe multichannel DSP, speaker calibration, and polished iconography remain future work.

The code is experimental and has not been compiled on a Windows machine in this environment.
