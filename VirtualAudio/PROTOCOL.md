# Harmonia transport protocol v1 (D2.1)

This document freezes the v1 **wire encoding**, not an operational kernel API.
C++ `protocol/TransportV1.h` and C# `protocol/TransportV1.cs` implement the same
allocation-free codecs and validation. No device, IOCTL handler, queue, service,
or routing backend is implemented by D2.1. See D2_CONTRACT.md for later work.

## Framing and compatibility

Every message is exactly a 160-byte header followed by `PayloadBytes` bytes.
All integers are unsigned, little endian. Offset zero is the first header byte.
There is no struct packing, native pointer, string, cursor, or writable mapping
in the ABI. Header fields use fixed-width encodings independent of host padding.
`Message` objects are logical objects only; never memcpy/marshal them to the wire.
Reserved bytes and unused fields MUST be zero. No trailing bytes are accepted.
Unknown versions, kinds, operations, statuses, flag bits, state values, and
noncanonical lengths are rejected; extensions require a new version. V1's
maximum message size is 11,680 bytes (160 + 480 * 24).

Decoders validate the entire message before returning the header and a borrowed
payload view. Failure returns a zero header and empty/null payload. The caller
must keep the input alive and immutable while using the view. Encoders validate
before writing and return zero bytes written on failure. Input payload and
output buffers must not overlap. No codec owns, allocates, pins or maps audio
buffers. Kernel callers must first copy untrusted input to owned storage and
validate actual buffer lengths; codecs cannot validate an arbitrary pointer.

## Header offsets

| Offset | Bytes | Field | Meaning |
| --- | --- | --- | --- |
| 0 | 4 | Magic | 0x31544148; bytes `48 41 54 31` (HAT1) |
| 4 | 2 | Version | 1 |
| 6 | 2 | HeaderBytes | 160 |
| 8 | 4 | TotalBytes | Exactly 160 + PayloadBytes |
| 12 | 2 | Op | 1 capabilities, 2 attach, 3 read frames, 4 state, 5 detach |
| 14 | 2 | Type | 1 request, 2 response |
| 16 | 4 | Flags | Bit flags below |
| 20 | 4 | Result | Status below; zero in requests |
| 24 | 8 | Session | Nonzero attach token; scoped to owning file object |
| 32 | 8 | Generation | Stream generation; read request carries expected generation |
| 40 | 8 | Sequence | Last completed successful audio-read sequence, or this block's sequence |
| 48 | 8 | FirstFrame | First audio frame in read; next available frame in state/attach snapshot |
| 56 | 8 | Qpc | First-frame time in read; observation time in snapshot |
| 64 | 8 | QpcFrequency | QPC ticks per second; nonzero for active-generation snapshots/audio |
| 72 | 8 | OverflowFrames | Cumulative oldest frames dropped from a full private queue |
| 80 | 8 | DisconnectedFrames | Cumulative consumed frames dropped without an attached consumer |
| 88 | 8 | TimingLostFrames | Cumulative consumed frames whose source bytes cannot safely be recovered |
| 96 | 4 | SampleRate | 48000 when a format is present |
| 100 | 2 | SampleFormat | 1 = interleaved IEEE Float32 little endian |
| 102 | 2 | Channels | 2 or 6 when a format is present |
| 104 | 4 | SpeakerMask | 0x3 stereo, 0x60F 5.1 side, 0x3F 5.1 back |
| 108 | 2 | FrameBytes | Channels * 4 (8 or 24) |
| 110 | 2 | State | Stream state below |
| 112 | 4 | FrameCount | 1..480 on successful reads; otherwise zero |
| 116 | 4 | RequestedFrames | 1..480 in read request; 480 in capabilities reply; otherwise zero |
| 120 | 4 | PayloadBytes | Exact payload length; bounded before multiplication/copy |
| 124 | 4 | QueueCapacity | 8192 in capabilities reply; otherwise zero |
| 128 | 4 | FeatureBits | 7 in capabilities reply; otherwise zero |
| 132 | 4 | FormatCount | 3 in capabilities reply; otherwise zero |
| 136 | 24 | Reserved | All zero |

Feature bits: 1 bounded direct reads; 2 separate loss counters; 4 QPC timestamps.
These describe the v1 contract, not evidence that handlers are currently present.

States: 0 Idle (no generation), 1 Running, 2 Paused, 3 Stopped,
4 Protected (export denied), 5 Removed. A nonzero generation includes a complete
supported format and nonzero QPC frequency, and cannot have Idle state.
Generation zero means an all-zero snapshot, including Idle state.
Protected state and the ProtectedContent flag must occur together.

Flags: 1 Discontinuity, 2 GenerationChanged, 4 Overflow, 8 Disconnected,
16 ProtectedContent, 32 EstimatedTimestamp. GenerationChanged, Overflow and
Disconnected each imply Discontinuity. Only Running, unprotected read responses
can carry audio. Protected/removed/paused/stopped snapshots never carry PCM.

Statuses: 0 Ok, 1 NoData, 2 NotAttached, 3 Busy, 4 GenerationMismatch,
5 UnsupportedFormat, 6 Protected, 7 Stopped, 8 Removed, 9 CounterExhausted.
Every non-Ok response is canonical: only Op, Type, Result and Session may be
nonzero beyond the framing prefix. It contains no snapshot or payload. Query
STATE for a fresh snapshot. The session is zero for capabilities and failed
attach, otherwise echoes the request. Transport errors are not NTSTATUS values.
Invalid input, access denial, cancellation and unusable output buffers return
an OS failure with zero returned bytes, not a purported successful wire reply.

## Operations

All requests are exactly 160 bytes with no payload. Fields not listed are zero.
`ValidReply` must be used after decoding to bind a response to its request:
matching operation and session, and for successful reads, matching generation
and no more than RequestedFrames. An attach response establishes its new token.
At most one outstanding operation per consumer handle; v1 has no request IDs.
No request may grant, select or override PCM export permissions.

| Operation | Request fields | Successful response |
| --- | --- | --- |
| CAPABILITIES | Op=1, Type=1 | Session=0, empty snapshot; RequestedFrames=480, QueueCapacity=8192, FeatureBits=7, FormatCount=3, PayloadBytes=48 |
| ATTACH | Op=2, Type=1 | Nonzero new Session; empty Idle snapshot or full current snapshot; no payload |
| READ_FRAMES | Op=3, Type=1, Session, expected Generation, RequestedFrames | Current Session/Generation, supported format, Running, Sequence, timing/counters/flags, FrameCount and PCM payload |
| STATE | Op=4, Type=1, Session | Full current snapshot or empty Idle snapshot; no payload |
| DETACH | Op=5, Type=1, Session | Same Session, empty snapshot, no payload; token invalidated |

Capabilities payload is three 16-byte records in fixed order: stereo, side,
back. Each record: SampleRate u32 at +0, SampleFormat u16 at +4, Channels u16 at
+6, SpeakerMask u32 at +8, FrameBytes u16 at +12, Reserved u16=0 at +14.
The codecs verify the exact v1 list. No PCM16 or 7.1 format exists in v1.

READ payload size is exactly FrameCount * FrameBytes. The count is bounded
before multiplication; a maximum stereo read is 3840 bytes, 5.1 is 11520 bytes.
Samples are copied bit-for-bit, including float special values; the future DSP
host must handle nonfinite samples safely. Stereo order: FL FR. Side order:
FL FR FC LFE SL SR. Back order: FL FR FC LFE BL BR. Never substitute masks or
reinterpret channel count. Golden payloads preserve distinct per-channel values.

ATTACH is exclusive device-wide. A second attach (including the same handle)
returns Busy and does not steal/reset the current session. A token alone is
not authority: later operations require the same authorized file object.
Fresh attach discards backlog. Closing the handle implicitly detaches; duplicate
or stale tokens return NotAttached. DETACH is not silently idempotent. Idle
attach is legal; the consumer polls STATE until a nonzero generation appears.
READ is nonblocking in v1: return available whole frames up to the requested
limit, otherwise NoData. GenerationMismatch returns no data; poll STATE and
reset downstream buffers. D2.2 still needs cancel-safe rundown for operations
in flight, even though v1 defines no waiting/pending read queue.

## Generation, sequence and timing

Generation is a nonzero monotonic device-lifetime u64, incremented on stream
creation/restart, format/mask changes, stop/pause/resume, protection transitions,
and reconnect to an existing stream. Flush queued audio atomically with that
transition. No generation wrap: stop exporting with CounterExhausted. A new
control-device instance invalidates every old handle/session and starts a new
lifetime; callers must create a new continuity tracker after ATTACH.

Sequence starts at 1 per attached session, increases by exactly one only when
an audio read is successfully committed, and never resets on a generation
change. State/attach snapshots report the last committed sequence (zero before
the first read). Failed/canceled requests consume neither frames nor sequence.
At u64 exhaustion stop export rather than wrap. Session identifiers are nonzero,
never reused within the device lifetime, and bound to a file object; they are
not secrets or authentication credentials.

FirstFrame is an absolute consumed-frame position within a generation, starting
at zero. It advances across dropped frames. Overflow drops oldest whole frames;
disconnected and timing losses are counted separately, without double counting.
The three loss counters accumulate over the control-device lifetime, survive
attach/detach and generation changes, and saturate at UINT64_MAX (never wrap).
CounterExhausted prevents further PCM export if exact accounting is exhausted.
No underrun counter is fabricated for an empty nonblocking read. Protected
frames are never copied/exported; protection transitions are discontinuities,
not ordinary queue overflow. D2.2 must specify any additional protected-drop
telemetry through a future version, not repurpose these fields.

Qpc is the raw QueryPerformanceCounter tick time corresponding to the FIRST
frame of a read. QpcFrequency is constant during a session; timestamps are
nondecreasing and never Unix time or completion time. If exact correlation to
the WaveRT clock cannot be established, set EstimatedTimestamp. The specific
correlation, integer rounding and delayed-callback loss accounting require D2.2
review before publishing live PCM. Do not reconstruct overwritten cyclic data.

First read after attach requires Discontinuity. First read of a new generation
also sets GenerationChanged. The consumer discards previous downstream buffers
on either event. A gap in frame positions or sequence requires Discontinuity;
frame positions cannot overlap or regress within a generation. Counters cannot
decrease. `Continuity` enforces these rules plus stable format/mask within a
generation, strictly increasing sequence, session identity, and QPC monotonicity.
It updates state only after accepting a block. Use ValidReply first: Continuity
alone cannot authenticate a response or know the requested generation/limit.

## IOCTL definitions and security gate

These are reserved definitions ONLY; **no kernel handlers are implemented**.
DeviceType=0x8000, functions 0x800..0x804, access=FILE_READ_DATA|FILE_WRITE_DATA.
Method is METHOD_BUFFERED for controls, METHOD_OUT_DIRECT for READ_FRAMES.
Both languages assert the exact codes and method/access bits in tests.

| Op | IOCTL |
| --- | --- |
| CAPABILITIES | 0x8000E000 |
| ATTACH | 0x8000E004 |
| READ_FRAMES | 0x8000E00A |
| STATE | 0x8000E00C |
| DETACH | 0x8000E010 |

InputBufferLength must be exactly 160 for all operations. Controls need at least
160 output bytes (208 for capabilities). Reads require at least
160 + RequestedFrames * 24 output bytes, regardless of active channel count,
so the caller can allocate before knowing a changed format. The actual returned
byte count must exactly equal TotalBytes. Reject insufficient buffers before
consuming frames. Capture input before writing shared METHOD_BUFFERED output;
for direct reads, map only the I/O-manager-owned output MDL, validate its byte
count, never accept user addresses, and zero any returned reserved bytes.
Any unused output capacity is not returned or decoded as part of the message.

See [D2_SECURITY.md](D2_SECURITY.md) for the proposed identity/SDDL, threat model,
and **explicit permission request required before any kernel IOCTL handlers**.
Public audio endpoint access must never imply control-device permission.
Microsoft documents the required [method/access bits](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/defining-i-o-control-codes)
and [device namespace protection](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/controlling-device-namespace-access).

## Executable conformance

```powershell
./VirtualAudio/scripts/Test-TransportProtocol.ps1
./VirtualAudio/scripts/Test-TransportProtocol.ps1 -Configuration Debug
```

These run C++ and C# golden encoding/decoding, every-byte truncations, malformed
headers/fields/reserved bytes, bounded/oversized counts, continuity/replay/gaps,
and 10,000 identical deterministic mutations. Both languages' encoded bytes
and mutation acceptance decisions must match. Fixtures in
`tests/vectors/transport-v1.txt` are independently authored immutable bytes;
tests never regenerate them. The native codec also compiles with /kernel.
The root Build.ps1 includes these checks. No tests send real IOCTLs or require
a service, device, certificate, administrative audio change, or installed driver.
