# Harmonia roadmap

## v0.2 — Experimental stereo mirror
- [x] Capture existing default Windows playback device via WASAPI loopback
- [x] Send left channel to one monitor and right to another
- [x] Duplicate mono channel to both speakers per monitor
- [x] Remember selected endpoints in Harmonia's own settings file
- [ ] Validate build and real hardware playback
- [ ] Verify Windows audio settings remain untouched

## v0.3 — Windows desktop interface
- [ ] WPF device selection, start/stop, status and errors
- [ ] Left/right channel test controls
- [ ] Independent volume sliders
- [ ] Handle device removal and defaults changing

## v0.4 — Reliable multi-device audio
- [ ] Adaptive sample-rate/clock drift correction
- [ ] Better low-latency buffering
- [ ] Robust shutdown, recovery and synchronization tests

## v0.5+ — Universal audio engine
- [ ] Routing matrix / modular processing graph
- [ ] Per-app and multi-source input routing (investigate Windows APIs / virtual device tradeoffs)
- [ ] EQ, filters and DSP
- [ ] 5.1/7.1 and custom surround downmix/upmix, speaker calibration
- [ ] Presets, profiles and system-tray controls

### Non-negotiable principle
No permanent modifications to Windows audio configuration. If any future feature requires temporary routing changes, it must be clearly opt-in and include safe recovery for crashes and forced termination.