# D2 transport implementation contract (working draft)

Status: D2 in progress. This document **does not** assert an operational driver-to-app transport.
Based on the D1 baseline at `49315c1003239449b29cadeab73de7d053a4eddf`.
The existing WASAPI loopback backend is unchanged.

D2.1 now defines the explicitly serialized v1 protocol and C++/C# codecs in
[PROTOCOL.md](PROTOCOL.md), with executable golden/malformed/continuity tests.
It adds no kernel handlers, queue or service. Before D2.2 handlers are written,
request explicit approval of the service identity and access policy in
[D2_SECURITY.md](D2_SECURITY.md). See [D2_1_STATUS.md](D2_1_STATUS.md).

## Design decisions requiring review before kernel integration

- Keep the WaveRT cyclic render buffer **distinct** from a private, bounded,
  nonpaged PCM queue. Never map either buffer writable to user mode.
- Use a **single authorized consumer** and a separate secured device interface.
  Define and verify an explicit SDDL and service SID; do not use an unrestricted
  device interface or grant ordinary users access simply because the audio
  endpoint is publicly selectable. Service lifecycle and identity must be
  independently verified in a test VM.
- Implement bounded `METHOD_OUT_DIRECT` read IOCTLs; treat output buffers as
  I/O-manager-owned MDLs. An alternative buffered control IOCTL is appropriate
  for small capability/state responses. IOCTL method bits and access bits must
  be asserted in shared native tests.
- Initial PCM profile: little-endian Float32, 48 kHz, stereo mask `0x3`
  or six-channel masks `0x60F` / `0x3F`. Do not infer channel order from
  count alone. The first four channels of 5.1 are FL FR FC LFE; the final
  two are SL SR for side or BL BR for back.
- Capacity target: **8192 whole frames**. At 48 kHz this is ~171 ms;
  do not silently accumulate more latency. Read-batch maximum:
  **480 frames** (~10 ms) in the current profile. Never split an audio frame.

## Ownership and publication

- Allocate ring storage, metadata and scratch resources at PASSIVE_LEVEL.
- The actual WaveRT consumption position must be derived from the upstream
  implementation, not assumed to be the producer cursor. Document precisely
  where the OS has committed frames and where the stream has consumed them.
- Capture newly consumed frames with cyclic-wrap handling. If a callback has
  advanced by more than the WaveRT buffer capacity, mark discontinuity and
  increment a loss counter; **do not copy overwritten bytes**.
- A short, bounded DPC/notification path may enqueue complete frames only.
  No pool allocation, paging, user-mode waits, I/O or C# callbacks at elevated
  IRQL. Lock discipline and interrupt/DPC teardown require a separate review.
- A single synchronization strategy must protect cursors, data publication,
  generation changes, detach and removal. Avoid making a consumer-visible
  write cursor advance before the corresponding bytes are committed.
- On ring exhaustion, drop the oldest **whole frames**, increment overrun,
  and let Windows render continue. If no authorized consumer is attached,
  do not retain an indefinitely growing backlog or play stale sound after
  reconnect. Define separate overrun vs disconnected-drop counters.

## Consumer API and wire framing

Proposed IOCTL operations: QUERY_CAPABILITIES, ATTACH, GET_STATE,
READ_FRAMES, DETACH. All are versioned and reject unknown versions, invalid
lengths, integer overflow and unauthorized handles. Never use user-supplied
pointers or kernel-address-like offsets.

Do not publish a binary ABI until fixed byte offsets and C/C# golden-vector
tests agree. Read response should contain:
magic, version, header length, stream generation, sequence, first-frame
counter, QPC timestamp/frequency, sample rate, format tag, speaker mask,
channel count, frame size, frame count, payload size, cumulative loss and state.
Choose a fixed byte order and explicit serialization rather than C# struct
marshalling or compiler-default C++ padding. On unsupported or mismatched
formats, return a clear state/error, never reinterpret six-channel bytes as
stereo.

## Cancellation / destruction

- At most one attached consumer; extra attach attempts fail predictably.
- Pending reads must be cancel-safe and refcounted. Define a lock ordering
  between the stream/ring, cancel/completion path and PnP.
- Cancel, closing the handle, process termination, surprise removal, PnP
  stop/remove and power transitions must all complete outstanding requests
  **exactly once**, without callbacks after freed buffers.
- A generation change flushes queued PCM and signals discontinuity.
  The consumer resets downstream buffers and does not play prior-generation
  data. On reconnect, resume from fresh committed frames, not stale backlog.
- The kernel must continue or fail closed without waiting on the consumer.
  Do not fall back invisibly to the physical-default loopback backend.

## Protected audio / DRM

Audit SysVAD `SetContentId`, content rights and protected stream paths
before exporting **any** PCM. If permitted-export status is ambiguous,
transport must fail closed (no PCM export). Test that protected audio cannot
be obtained through the control interface. Do not treat a missing rights flag
as permission without documenting the upstream semantics.

## D2 acceptance tests

1. Build unsigned x64 Debug/Release with zero new driver verifier/static warnings.
2. Validate IOCTL ACL denial for ordinary unauthorized processes and enforce
   one consumer, valid access bits and malformed requests.
3. Verify exact sample content with deterministic 2/6 channel patterns,
   including side/back mask values and isolated rear L/R.
4. Force overflow, delayed reads, consumer kills, reconnect and format changes:
   bounded memory, accurate losses, no stale audio.
5. Exercise cancel/close/PnP remove/sleep-wake races under verifier on an
   isolated, correctly signed test environment.
6. Show no protected PCM export. Verify that routing still requires an
   explicit user selection of Harmonia as the Windows render endpoint.

## Scope boundaries

D2 is transport work. D3/D4 separately refactor the desktop audio engine into
a UI-independent service/host, implement the transport input backend, channel
routing, and independent sink clock correction. Retain WASAPI loopback as an
explicit diagnostic/legacy input choice. Do not install drivers, certificates,
change Secure Boot or Windows default devices during builds.
