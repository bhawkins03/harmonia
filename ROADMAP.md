# Harmonia roadmap

The audio-driver architecture on `main` is the official development baseline.
Use the root `Build.ps1` for the app, native policy checks and driver package.
This baseline is a development milestone, not a signed runtime release.

## Completed build milestones

- [x] WinForms app with experimental loopback routing and speaker controls.
- [x] Pinned SysVAD reference builds in x64 Debug and Release (D0).
- [x] Harmonia render-only driver fork with unique device/service identity (D1).
- [x] Stereo and 5.1 Float32 format policy at 48 kHz.
- [x] Local native builds, policy checks, WDK INF/API and catalog validation.

## Next: driver audio transport (D2)

- [ ] Bounded kernel PCM queue and secured user-mode control interface.
- [x] D2.1 versioned wire format, C++/C# codecs and golden-vector tests.
- [ ] Publish live format/state changes and loss counters through D2.2 handlers.
- [ ] Cancellation, close, disconnect, PnP removal and DRM behavior.

## Routing integration (D3/D4)

- [ ] Connect the native transport to a UI-independent routing host.
- [ ] Preserve discrete channels and validate masks/subformats.
- [ ] Channel-isolation, overflow, restart, sleep/wake and removal tests.
- [ ] Clock-drift correction and long-running multi-device playback checks.

## Distribution and later features

- [ ] Signing, installation/uninstallation and signed test-machine validation.
- [ ] Validate actual Windows shared-mode stereo/5.1 negotiation.
- [ ] DSP/EQ, calibrated layouts, presets and additional sources.

Audio configuration changes must be explicit and recoverable. Building source
does not install a driver or change Windows devices or security settings.
