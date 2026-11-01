# Harmonia virtual playback endpoint — implementation phase 1

## Current status
**No virtual Windows playback endpoint exists in this branch.** A selectable device in Windows Sound requires an installed, signed audio driver. The current .NET program still captures the Windows default device through WASAPI loopback. This branch implements UI device drag/drop and the transport/interface design.

## Driver approach
Base the future C++ Windows driver on Microsoft's SysVAD virtual audio driver sample:
https://github.com/microsoft/Windows-driver-samples/tree/main/audio/sysvad

SysVAD is a reference driver, not a working loopback cable to our WinForms process. Do not simply rename its INF or distribute its sample binary. The driver must provide:
- A named Harmonia render endpoint with supported stereo, 5.1 and 7.1 PCM formats and WAVEFORMATEXTENSIBLE masks
- Safe real-time transfer from render stream into a user-mode Harmonia service with deterministic channel order
- Stream timing/clock synchronization, shared-memory ring buffers (or validated alternative) and backpressure
- Device/app start-stop and shutdown behavior: silence on daemon failure, not uncontrolled bypass
- A properly packaged and signed installer/uninstaller
- No default-device modifications: user chooses Harmonia in Windows Sound; choosing ordinary headphones bypasses it

## Suggested transport contract (provisional)
- Fixed header: protocol version, sample rate, channel mask, frame count, sequence number, presentation timestamp and format flags
- Interleaved 32-bit float PCM frames initially, stereo/5.1/7.1
- A producer ring with atomic read/write indices and bounded buffer size
- Receiver service does not trust counts, offsets, or formats from kernel boundary
- Separate configuration/status RPC from real-time audio path
- Driver-ownership / application-lifetime testing in a VM

## .NET integration contract
Introduce an IAudioInputSource abstraction and preserve WASAPI loopback as legacy input. A VirtualAudioInputSource can be added once its real driver transport exists. Then the existing output routing, speaker meters, saved assignments and DSP can be reused with limited changes.

## Development environment
Visual Studio C++ workload, Windows SDK, WDK, dedicated test machine or isolated virtual machine. Kernel driver builds need appropriate signing. **Do not disable driver signature enforcement or Secure Boot on your normal workstation to run an experimental driver.**

References:
https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/
https://learn.microsoft.com/en-us/windows-hardware/drivers/install/introduction-to-test-signing

## Drag/drop test
Stop routing first. Choose layout. Drag one device tile onto Front L, Front R, Center, Subwoofer or either rear node (both rear nodes assign the *same stereo endpoint*). The mapping is saved per layout. Starting routing validates that mandatory roles are set, endpoints are distinct and not equal to the current loopback source. Test against physical hardware; UI code not compiled on Windows yet.
