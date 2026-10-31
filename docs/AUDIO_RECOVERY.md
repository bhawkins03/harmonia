# Audio settings safety and recovery design

## Implemented now
- Buffered WinForms speaker map to minimize flicker while polling animated channel meters.
- A **read-only** audio configuration snapshot: default multimedia device identity, active devices and observed playback mix channel count, rate and encoding.
- Click **Audio backup** to write a durable JSON snapshot to `%LOCALAPPDATA%\Harmonia\recovery\last-audio-snapshot.json`.

## Not implemented (important)
- The snapshot is NOT a complete rollback image; it does not include all Windows default roles, per-app routing, vendor-specific driver controls, channel masks, speaker calibration or audio endpoint properties.
- **No Windows configuration mutation or automatic restoration exists in this branch.** A snapshot is useful for diagnostics but cannot safely reapply itself without a dedicated, tested implementation.
- Setting the default device to 5.1 by itself is not always possible: supported channel counts are constrained by the audio endpoint and driver. A stereo-only endpoint cannot be reliably converted to native 5.1 with a property edit.

## Proposed opt-in transaction design
1. Inventory all relevant endpoint properties and default role assignments; probe actual 6-channel support.
2. Show a proposed before/after diff and require user confirmation.
3. Persist an atomic transaction journal *before* changes, including original device IDs, formats and roles, version, and checksum; preserve a last-known-good snapshot separately.
4. Apply only supported configuration changes through verified APIs; confirm 6-channel loopback capture before claiming native mode.
5. On normal **Exit**, attempt restore and verify; on errors, report precisely which changes could not be reverted.
6. For crashes, application exit hooks are insufficient. Use a separate watchdog/helper process or recovery on next startup. On startup, detect an unfinished transaction and offer **Restore previous audio configuration** before starting routing.
7. Prefer an isolated virtual 5.1 endpoint over modifying physical monitor/headphone endpoint properties. If OS-default changes are ever used, make them explicitly opt-in, recoverable, and disclose that a power failure may delay restoration until reboot.

Relevant Microsoft references:
- https://learn.microsoft.com/en-us/windows/desktop/api/audioclient/nf-audioclient-iaudioclient-getmixformat
- https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-formats

The code is experimental and not yet compiled in Windows after this change.
