# Native multichannel audio plan

## Current implementation (experimental)

Harmonia captures the Windows default playback endpoint through WASAPI loopback. If the endpoint mix format is 2-channel PCM/float, Harmonia uses its stereo upmix. If the endpoint mix format is 6-channel PCM/float, Harmonia routes six channels directly using the conventional Windows 5.1 order FL, FR, FC, LFE, BL/SL, BR/SR.

**This does not cause games to output 5.1.** Many default endpoints expose only stereo, so discrete channel capture may remain unavailable even if media contains 5.1 sound.

The current output selections include the default playback device with an explicit label for diagnosis, but choosing it will be rejected to prevent endpoint loopback feedback. This particularly affects default headphones. Harmonia does not change the default endpoint automatically.

## Native game 5.1 without permanent Windows settings modifications

1. **Input inspection**: report channel count, extensible channel mask, PCM/float subtype, sample rate and capture endpoint.
2. **Multichannel capture**: support WAVEFORMATEXTENSIBLE channel masks rather than relying on six-channel order, plus 7.1 layouts.
3. **Virtual endpoint feasibility study**: games need an audio endpoint advertising 5.1 if the physical default endpoint is stereo. A virtual 5.1 playback endpoint is the conventional solution, but installing a signed Windows audio driver and choosing it as default may conflict with the no-OS-settings-change requirement. Explicit opt-in and crash-safe restoration would be needed; mere process termination is not enough.
4. **Alternative sources**: direct 5.1 media file decoder or application-specific WASAPI interception, with restrictions.
5. **Feedback-safe default output**: Microsoft's process-loopback capture API can exclude Harmonia's own process, but implementing and validating it requires Win32 audio interop. Endpoint loopback cannot safely send audio back to the captured endpoint.
6. **Robust output engine**: independent hardware clocks, resampling, channel mapping, gain, timing, and buffering need tests; the current buffer-clearing fallback may click or drift.

Microsoft documentation:
- https://learn.microsoft.com/windows/win32/coreaudio/loopback-recording
- https://learn.microsoft.com/windows/win32/coreaudio/device-formats
- https://learn.microsoft.com/windows/desktop/api/audioclient/nf-audioclient-iaudioclient-getmixformat
- https://learn.microsoft.com/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/

For a worn-around-the-neck headphone rear pair, use an ordinary stereo playback endpoint. Left and right may need swapping depending on how the headphones are worn; this will become an explicit channel-swap feature. Speakers near the neck will not reproduce spatial cues the same way as rear loudspeakers.

This branch has not been built or hardware-verified.