# Harmonia Virtual Audio — Driver Design v0.1

Status: architecture proposal. **No functional Windows driver is present.**

## Goal

Expose one Windows render endpoint, **Harmonia Virtual Audio**, advertised with stereo, 5.1 and (later) 7.1 modes. The user manually selects it in Windows Sound. Applications may send multichannel PCM to the Windows audio engine, which mixes and delivers PCM to our virtual render stream. Harmonia's user-mode engine distributes channels to physical DP speakers, headphones, etc. Selecting an ordinary physical endpoint bypasses Harmonia. The driver never silently changes default Windows devices.

## Components

```text
Applications / games / media players
       │  WASAPI / DirectSound etc.
       ▼
Windows Audio Engine (audiodg.exe)
       │  supported PCM render formats
       ▼
Harmonia Virtual Audio render endpoint [SysVAD-derived, signed .sys + INF]
       │  WaveRT render stream
       ▼
Driver-owned bounded PCM transport [not implemented]
       │  versioned shared ring / sanctioned kernel-to-user transfer
       ▼
Harmonia Audio Service [future standalone user-mode process]
       │  format and channel-mask validation
       ▼
Input abstraction + DSP/routing [existing C# engine, refactor required]
       ├── FL --> monitor left (dual mono)
       ├── FR --> monitor right (dual mono)
       ├── FC --> center monitor (dual mono)
       ├── LFE --> optional subwoofer endpoint
       └── SL/SR or BL/BR --> one stereo headphone endpoint
```

## Scope / exclusions

Driver provides **render endpoint, PCM timing, safe bounded transfer**. No equalizer, speaker-position configuration, app UI, rendering directly to arbitrary endpoints, MP3/FLAC decoding, or Windows default-device automation in kernel.

User-mode service hosts transport consumer independently from tray UI so closing a window need not interrupt sound. If service unavailable, the virtual device must fail safely: endpoint may render silence with visible UI warning; no stale audio and no uncontrolled routing. Bypass requires user choosing another Windows device.

## Input format proposal

- v1: 48,000 Hz, interleaved IEEE-754 32-bit float PCM, 2 and 6 channels
- optional compatibility: 16-bit integer PCM (explicit conversion at input boundary)
- v2: 8 channels and more sample rates, with resampling in user mode
- channel order is defined by **WAVEFORMATEXTENSIBLE** speaker mask, not simply channels count
- 5.1 side channels: FL FR FC LFE SL SR, mask 0x60F
- 5.1 rear channels: FL FR FC LFE BL BR, mask 0x3F
- 7.1: FL FR FC LFE BL BR SL SR, mask 0x63F
- Stereo: FL FR, mask 0x3

**Do not advertise unsupported formats.** Windows default mix mode must be tested at 6 channels on installed driver. An application still may choose stereo; Harmonia's selectable upmix can process stereo as desired.

## Driver-to-user-mode contract

Use a versioned bounded PCM ring with minimum fields: magic, ABI version, sample format, interleaved channel count, speaker mask, sample rate, frames per chunk, monotonic sequence, frame timestamp, read/write cursors, stream generation, overrun/underrun count and connection state. Define memory ordering, bounds, alignment and ACLs explicitly before implementation. Kernel must not wait on UI, network, disk or C# GC. Driver must copy/validate lengths and never trust user-provided pointers or ring offsets.

Proposed framing and failure rules in [PROTOCOL.md](PROTOCOL.md).

## Sequence / lifetime

1. Windows opens endpoint and negotiates a supported render mix format.
2. Driver allocates WaveRT stream and ring; starts its clock.
3. Service attaches to named, access-controlled stream transport.
4. Driver advances stream even if consumer lags, recording overflow and delivering silence rather than blocking kernel paths.
5. Service validates format and routes blocks to destination buffers. Existing multi-device drift correction still needed.
6. On stop, flush generation, invalidate stale consumer data, release handles.
7. On restart, negotiate new generation; never reuse stale frames.

## Validation milestones

- D0: build **unmodified SysVAD sample** in VS + WDK on developer machine; do not install on daily-use PC
- D1: fork minimal single render endpoint, show in Device Manager/Sound on test VM with verified supported formats
- D2: implement kernel-to-service PCM transport with test generator and silence recovery
- D3: test 6 unique channel tones and side/back layout masks; prove no FL/FR bleed into rear
- D4: plug transport into Harmonia user-mode routing and independent headphone L/R meters
- D5: stability testing: underruns, app exits, unplug, sleep/wake, default switching, 24h playback
- D6: signing, clean installation/uninstallation and diagnostics

## Practical requirement

Windows driver binaries require appropriate trustworthy signing on Windows 11. Do not disable Secure Boot, memory integrity or driver signature enforcement on your everyday PC to run early test builds. Microsoft now emphasizes modern WHCP-based signing policy (2026).

References:
- https://github.com/microsoft/Windows-driver-samples/tree/main/audio/sysvad
- https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/
- https://support.microsoft.com/en-us/windows/hardware/drivers/the-windows-driver-policy
