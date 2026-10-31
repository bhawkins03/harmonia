<div align="center">

# HARMONIA

### Make every speaker part of your soundstage.

**An open-source Windows audio routing experiment for unconventional speaker setups.**

Route stereo audio across independent playback devices, arrange them on a visual speaker map, and explore multi-device surround sound.

[Get started](#get-started) · [Features](#features) · [Project status](#project-status) · [Roadmap](#roadmap) · [Contributing](CONTRIBUTING.md)

![Windows](https://img.shields.io/badge/platform-Windows_10%2F11-2389DA?style=flat-square&logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Stage](https://img.shields.io/badge/stage-early_prototype-F0AD4E?style=flat-square)
![Virtual driver](https://img.shields.io/badge/virtual_driver-in_development-505C6B?style=flat-square)

</div>

---

> [!IMPORTANT]
> **Harmonia is experimental software, not a finished virtual audio driver.**
> The current application uses WASAPI loopback capture from the Windows default playback device. It cannot recover discrete surround channels once Windows or another app has downmixed them to stereo. The planned selectable *Harmonia Virtual Audio* device is **not yet implemented or installable**. Expect latency, possible audio drift, and rough edges.

## Why Harmonia?

Your speakers don't always come in a matched set. Maybe you have two monitors with built-in speakers, an extra center speaker, and a pair of headphones you want to use as rears. Windows normally treats these as separate playback devices.

**Harmonia explores treating them as one configurable listening space.**

The long-term goal is simple: select **Harmonia Virtual Audio** as your Windows output, then let Harmonia distribute each channel to whichever devices you put around you. Switch back to a physical Windows playback device to bypass Harmonia.

Right now, we're building and validating the audio routing engine and its interface before connecting that future driver.

## Features

| Capability | Current state |
| --- | --- |
| Stereo output to two independent playback endpoints | **Implemented / prototype** |
| Dual-monitor stereo layout | **Implemented / prototype** |
| Triple-monitor layout with synthesized center | **Experimental** |
| 5.1-style routing and optional separate subwoofer endpoint | **Experimental** |
| Drag-and-drop device assignment on speaker map | **Implemented on development branch** |
| Live four-bar signal meters above speaker positions | **Implemented on development branch** |
| Rear left/right mapped to one stereo device | **Implemented; source upmix is limited** |
| System tray and saved layout assignments | **Implemented on development branch** |
| Independent native 5.1/7.1 virtual Windows playback endpoint | **Planned / driver design underway** |
| Device clock drift correction, robust hot-plug handling, production installer | **Not yet implemented** |

**What the meters show:** software PCM levels sent toward each output buffer, not confirmation that the physical speaker produced sound.

### Arrange your own soundstage

```text
                       FRONT
         [ Monitor / FL ]   [ Monitor / FR ]
                    [ Center / FC ]

                       YOU

          [ Rear L ]       [ Rear R ]
                \           /
                 [ Stereo headphones ]

                 [ Optional LFE ]
```

Drag available playback-device tiles onto map positions. Assignments are saved for each layout. In the current 5.1-style layout, both rear positions refer to **one stereo headphone endpoint**, with separate left and right signals.

### What is different about native surround?

**Stereo upmix** takes an already mixed left/right signal and synthesizes additional outputs. It cannot reliably separate sounds that originated only in the rear channels.

**Native multichannel audio** preserves discrete front, center, LFE, and surround channels *before* they're downmixed. That's the reason we're designing the virtual playback driver.

## Get started

**Requirements:** Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and Git for the development-branch instructions.

For the current graphical prototype, use the development branch:

```powershell
git clone https://github.com/bhawkins03/harmonia.git
cd harmonia
git switch --track origin/feature/dragdrop-virtual-input
dotnet restore
dotnet run
```

If you already have the repository cloned, use `git fetch origin` and `git switch feature/dragdrop-virtual-input` instead.

**Basic workflow**

1. Select a layout, then drag available playback devices onto the map.
2. Keep each physical output endpoint assigned to only one role. The two rear positions share a single stereo output.
3. Click **Start routing** and monitor the signal bars.
4. Click **Stop routing**, or exit from the system tray when finished.

> [!NOTE]
> In the existing loopback architecture, an output device cannot also be the Windows default capture source because it risks recapture/feedback. The original default audio device may remain independently audible. Harmonia does **not** automatically change Windows default playback, system volume, per-app routing, or speaker configuration.

### Working on the virtual driver?

The driver design is on `design/virtual-audio-driver-v1`:

- [Driver architecture](VirtualAudio/DESIGN.md)
- [Draft PCM transport protocol](VirtualAudio/PROTOCOL.md)
- [Development environment](VirtualAudio/DEV_SETUP.md)

Driver implementation uses Microsoft's [SysVAD virtual audio device sample](https://github.com/microsoft/Windows-driver-samples/tree/main/audio/sysvad) as a reference and requires C++ Windows driver tooling. **No functional driver installer or signed `.sys` is currently provided by this project.**

## Architecture

```text
CURRENT PROTOTYPE                         TARGET ARCHITECTURE

Windows default endpoint                 Apps / games / media players
          │                                         │
          ▼                                         ▼
    WASAPI loopback                     Harmonia Virtual Audio
    (2ch or 6ch mix)                     (planned 5.1/7.1)
          │                                         │
          └─────────────┐              PCM transport / service
                        │                          │
                        ▼                          ▼
                    Harmonia user-mode audio engine
                              │
                   channel mapping + DSP
                              │
             ┌────────────────┼───────────────────┐
             ▼                ▼                   ▼
        DP monitors       center / LFE       stereo rear pair
```

The design keeps kernel-mode responsibilities small. Speaker layouts, effects, visual meters, and routing stay in user mode.

## Project status

Harmonia is being developed in public, with individual experiments isolated in feature branches and pull requests. The `main` branch may lag behind the latest graphical prototype.

- [Virtual playback device architecture — Issue #14](https://github.com/bhawkins03/harmonia/issues/14)
- [Native multichannel input investigation — Issue #11](https://github.com/bhawkins03/harmonia/issues/11)
- [Development branches and pull requests](https://github.com/bhawkins03/harmonia/pulls)

**Current known limitations:** the loopback source can already be stereo after Windows mixing; synthesized rear channels are not faithful discrete surround; independent hardware endpoints may drift apart; and there is no signed virtual device for ordinary Windows installation.

## Roadmap

- [x] Split stereo across independent Windows endpoints
- [x] Add a Windows desktop speaker-layout interface
- [x] Add live output signal meters and drag-and-drop assignments in development
- [ ] Validate independent 5.1-channel routing with reliable test sources
- [ ] Build an initial SysVAD-derived virtual render endpoint in a safe test environment
- [ ] Implement a bounded, real-time driver-to-user-mode PCM transport
- [ ] Connect native 5.1 input to the existing routing engine
- [ ] Handle device clocks, resampling, hot-plug, and recovery
- [ ] Add robust EQ, calibration, profiles, and polished distribution

The broader development plan is in [ROADMAP.md](ROADMAP.md).

## Contributing

Ideas, reproducible bug reports, code reviews, and Windows audio expertise are welcome.

Start with [CONTRIBUTING.md](CONTRIBUTING.md), or open an [issue](https://github.com/bhawkins03/harmonia/issues). Useful contributions include channel-isolation tests, WASAPI/WaveRT expertise, endpoint synchronization, Windows Forms accessibility, and safe driver packaging.

Please don't deploy unfinished kernel drivers to your everyday system or disable Windows security protections to test experimental builds.

---

<div align="center">

**Built for the speakers you already have.**

[Repository](https://github.com/bhawkins03/harmonia) · [Issues](https://github.com/bhawkins03/harmonia/issues) · [Pull requests](https://github.com/bhawkins03/harmonia/pulls)

</div>
