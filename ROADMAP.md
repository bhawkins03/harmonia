# Harmonia roadmap

The audio-driver architecture on `main` is the official development baseline.
Use the root `Build.ps1` for the app, native policy checks and driver package.
This baseline is a development milestone, not a signed runtime release.
The owner has designated the VM-validated D2.2 driver as the Suite main build;
further development and runtime testing take place in a VM. Lite branches and
its driverless release remain independent. See VirtualAudio/VM_HANDOFF.md.

## Completed build milestones

- [x] WinForms app with experimental loopback routing and speaker controls.
- [x] Pinned SysVAD reference builds in x64 Debug and Release (D0).
- [x] Harmonia render-only driver fork with unique device/service identity (D1).
- [x] Stereo and 5.1 Float32 format policy at 48 kHz.
- [x] Local native builds, policy checks, WDK INF/API and catalog validation.

## Next: driver audio transport (D2)

- [x] D2.2 source: bounded kernel PCM queue and secured control interface.
- [x] D2.1 versioned wire format, C++/C# codecs and golden-vector tests.
- [x] D2.2 handlers for format/state, generations and loss counters.
- [x] Signed VM installation, healthy endpoint enumeration and exact package hashes.
- [x] Live SYSTEM/restricted LocalService PCM export and six-channel isolation.
- [x] Unauthorized-access denial and simulated CopyProtect export denial.
- [ ] Remaining cancellation, process-death, PnP and Driver Verifier stress checks.

## Routing integration (D3/D4)

- [x] Native transport client and restricted LocalService routing host.
- [x] Full frontend controls the service through an authenticated local pipe.
- [x] Discrete role mapping, live gains, bounded adaptive output buffers and WDL resampling.
- [x] Real driver-to-service six-channel PCM delivery and interface/persistence VM checks.
- [x] Suite updater verification, compatibility, coordinated deployment and rollback source/tests.
- [ ] Production signing and end-to-end signed app/service/driver update tests (public updates disabled).
- [ ] Channel-isolation, overflow, restart, sleep/wake and removal tests.
- [ ] Clock-drift correction and long-running multi-device playback checks.

## Distribution and later features

- [x] Development signing, installation and isolated VM runtime validation.
- [x] Shared-mode 48-kHz six-channel Float32 render and software loopback in VM.
- [ ] Additional stereo/5.1 negotiation, production signing and uninstall validation.
- [ ] DSP/EQ, calibrated layouts, presets and additional sources.

Audio configuration changes must be explicit and recoverable. Building source
does not install a driver or change Windows devices or security settings.
