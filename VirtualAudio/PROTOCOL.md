# Harmonia Transport Protocol — Draft 0

This is **a proposed ABI**, not a final or functioning kernel interface. It must be reviewed for IRQL compatibility, synchronization, ACLs and threat model before any driver coding.

## Separation

The initial prototype direction in [IMPLEMENTATION.md](IMPLEMENTATION.md) uses
a driver-private ring with batched direct-I/O reads. The shared header below
remains a draft requirements list, not an implemented mapped-memory ABI.

- **Kernel render stream** produces PCM frames.
- **User-mode Harmonia audio service** consumes.
- **Control plane** negotiates capabilities, attaches session, reads counters, and signals stream state.
- **Real-time data plane** moves bounded PCM frames without RPC calls per sample.

## Header proposal

| Field | Type | Purpose |
| --- | --- | --- |
| magic | u32 | Identify Harmonia stream |
| abiVersion | u16 | Compatibility; reject unknown |
| headerBytes | u16 | Layout length |
| streamGeneration | u64 | Prevent stale reads after restart |
| formatTag | u32 | Float32LE = 1; PCM16LE = 2 |
| sampleRate | u32 | 48000 for initial profile |
| channelCount | u16 | 2 or 6 initially |
| frameBytes | u16 | channelCount × sample bytes |
| speakerMask | u32 | WAVEFORMATEXTENSIBLE mask |
| bufferFrames | u32 | Fixed power-of-two capacity |
| readFrameCounter | u64 | Monotonic consumed frame count |
| writeFrameCounter | u64 | Monotonic produced frame count |
| overrunFrames | u64 | Diagnostics |
| underrunFrames | u64 | Diagnostics |

All wire types are fixed width, little endian. Padding and memory alignment must be explicitly versioned in C and C# definitions. Use an agreed atomic publication protocol; a plain C struct shared across languages is not sufficient. The driver MUST validate all handles, offsets and size arithmetic.

## Startup / recovery

- On attach, both sides exchange supported protocol versions and exact PCM format.
- Mutating stream format increments streamGeneration and flushes ring.
- Service restart attaches from latest valid frame; never plays prior generation data.
- On overflow, drop oldest frames to maintain bounded latency; report dropped frames.
- On silence/no service, virtual render may continue accepting writes, but report disconnected state. Do not block Windows audio callbacks indefinitely.
- No user-controlled code, custom DSP, or disk operations run in kernel callbacks.

## Security

The service connection is granted only to an appropriately scoped local identity. The data plane should have read-only exposure to the user-mode consumer wherever feasible. Validate sizes, counts and offsets both sides. Avoid any IOCTL that writes arbitrary kernel addresses or permits user-mode injection into unrelated processes.

## Tests

- Negotiation mismatch; corrupted header; invalid channel masks; overflow, underrun, restart
- Delayed consumer; abrupt process kill; logoff; sleep/wake; endpoint format change
- Sample-exact generated 5.1/7.1 channel test, including independent surround L/R
- Consumer disconnected with the endpoint selected as Windows default
