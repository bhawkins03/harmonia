# D2.1 status: transport protocol v1

Implemented on `feature/d2-transport-contract`, incorporating upstream contract
commit `c9d2373`, based on official main `49315c1`.

## Delivered

- Fixed 160-byte, explicitly serialized little-endian header and exact lengths.
- C++ kernel-compatible header and independent .NET 8 C# transport library.
- Capabilities/attach/read/state/detach definitions; private IOCTL codes with
  both read/write access bits, buffered controls and out-direct audio reads.
- Float32 48 kHz only: stereo 0x3 and discrete 5.1 side 0x60F/back 0x3F.
- Session/request correlation, stream generation, monotonic read sequence,
  first-frame/QPC timing, separate cumulative loss counters and discontinuities.
- Bounded 480-frame reads; 8192-frame queue capacity advertised by the contract.
- Borrowed payload views, strict unknown/reserved/unused field rejection,
  canonical no-payload errors, bounds checks before arithmetic/copy.
- Continuity guards reject replay, stale generations, frame/counter/timestamp
  regression, unmarked gaps and format changes without a generation change.
- Fifteen fixed independent golden messages and cross-language mutation parity.
- Root Build.ps1 runs the new protocol suite. The app project excludes the
  separate protocol/test C# files from its default recursive compile glob.

There are **no kernel IOCTL handlers, live ring, device interface or service**.
The actual driver continues to discard audio. The driver source and existing
WASAPI routing implementation were not changed. No push or merge is included.

## Validation on 2026-10-31

Both commands passed:

```powershell
./Build.ps1 -Configuration Debug
./Build.ps1 -Configuration Release
```

For each configuration:
- Desktop application: build passed, zero warnings/errors.
- Existing endpoint format-policy checks: passed.
- C++ protocol executable: 11,795 checks passed, 15 golden vectors.
- C# protocol executable: 9,844 checks passed, the same 15 golden vectors.
- 10,000 deterministic mutations per language: acceptance decisions identical;
  accepted messages reencode byte-for-byte. Golden encoder outputs identical.
- Every truncation of each golden message, extra bytes, all reserved bytes,
  unknown version/op/kind/flags/status, invalid formats, oversized counts,
  short output buffers, request/reply mismatches and continuity cases tested.
- Maximum 480-frame stereo and 5.1 payloads tested.
- Native codec smoke compile: /kernel /W4 /WX passed.
- Unsigned x64 driver build, Universal API/INF checks and catalog generation:
  passed with no reported warnings/errors.

Logs (ignored): `.work/logs/d21-full-debug.log`,
`.work/logs/d21-full-release.log`. Native/managed parity outputs are under
`.work/tests/transport-{Debug,Release}/`. The standalone suite is
`scripts/Test-TransportProtocol.ps1 [-Configuration Debug|Release]`.

One format-change test initially kept the original stereo layout; it was
corrected to use a genuine stereo-to-5.1 change and then passed in both languages.
No driver/service/certificate installation, Windows audio defaults, boot
settings, or security settings were changed. `sc.exe showsid` was a read-only
lookup of the proposed service SID. Actual ACL/DRM/IRQL/PnP behavior remains
untested and requires later signed isolated-environment work.

## Decisions requiring review before D2.2

1. **Access approval is mandatory before writing handlers.** Review and request
   explicit permission for the concrete LocalService/service-SID identity and
   LocalSystem + service-only DACL in D2_SECURITY.md. The public MEDIA endpoint
   ACL cannot protect PCM export. D2.1 grants no approval for a control device.
2. Confirm nonblocking READ_FRAMES (NoData rather than pending wait), one
   outstanding operation per handle, one exclusive consumer, failed/canceled
   reads consuming neither data nor sequence, and handle-bound attach tokens.
3. Confirm device-lifetime generations/loss counters, session-lifetime read
   sequence, generation flushing on stop/pause/resume/protection/reconnect, and
   fail-closed CounterExhausted instead of wrapping.
4. Review exact WaveRT first-frame/QPC correlation and integer rounding.
   EstimatedTimestamp must be truthful; delayed overwritten bytes are loss,
   never valid PCM. Confirm when pending discontinuity flags are cleared.
5. Audit SetContentId/rights provenance before exporting PCM. Missing or
   ambiguous export permission fails closed. Select and review synchronization,
   MDL handling, DPC copy budget, cancellation and teardown ordering separately.
6. Review the frozen v1 offsets/statuses/capability list and future separate
   control-device interface GUID. Wire compatibility is tested; it does not
   establish runtime transport safety or working playback.
