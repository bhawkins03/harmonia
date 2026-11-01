# D2.2 implementation and audit

Status: source implementation on `feature/d2-transport-contract`; PR #20 must
remain a draft. This is **not an installed, signed or runtime-validated audio
transport**. No service or driver was installed. D3/D4 app integration is absent.
The existing WASAPI loopback backend is unchanged.

## Source and control interface

- `native/TransportQueue.h`: production 8192-frame private PCM queue/state machine.
  Fixed storage: 8192 * 24 PCM bytes plus per-slot frame index and QPC (about
  320 KiB total plus metadata). Stereo uses an 8-byte frame; side/back 5.1 use
  24 bytes. No channel conversion, reordering or mixing.
- `native/RenderWindow.h`: absolute committed-packet window, validated lengths,
  wrap and first-safe-frame floor. Tested against the production code at compile
  time. It never treats a modulo write cursor as proof that samples are valid.
- `driver/HarmoniaTransport.cpp`: separate WDM control device, v1 IOCTL dispatch,
  access checks, attachment ownership, direct-I/O output and lifecycle gates.
- `driver/EndpointsCommon/minwavertstream.cpp`: consumption hook, rights gate,
  packet commit tracking, and timer/buffer teardown ordering.

User-mode control path: `\\.\HarmoniaAudioTransport` (kernel name
`\Device\HarmoniaAudioTransport`). This is a standalone named control device
with a DOS symbolic link, not a PnP device-interface GUID. Its private security
class GUID is `{820D4501-6EC3-4AB0-8C78-C91F0AD91714}`. The public MEDIA endpoint
and standard KS interface categories are separate. Exactly one adapter is
supported; an additional active adapter is rejected.

The device is created at adapter start with IoCreateDeviceSecure,
FILE_DEVICE_SECURE_OPEN, exclusive opens, and the exact approved protected
LocalSystem/service-SID DACL in D2_SECURITY.md. Reject remote-origin/suffix
opens and shared opens. Require user-mode requests with both read and write
access. Bind the file context to a referenced opener process as well as its file
object, so inherited/duplicated handles in another process cannot issue IOCTLs.
The service must still request a noninheritable handle. A service compromised
inside its own process and LocalSystem are trusted, not excluded by this model.

All five v1 IOCTL codes/methods must match the decoded operation. Reject unknown
codes BEFORE inspecting buffered input (especially METHOD_NEITHER). Capture the
160-byte request before writing buffered output. Output capacity is bounded by
MaxMessage; READ maps only an I/O-manager-owned output MDL and validates its byte
count before entering the queue lock. Both I/O-manager access bits and
IoValidateDeviceIoControlAccess enforce read/write requirements. No user
addresses or queue cursors are accepted. No per-read pool allocation occurs.

## Serialization, cancellation and lifetime

The queue/core intentionally has no internal synchronization. Every production
call uses one nonpaged transport spinlock. Control output mapping and input
capture happen first; bounded header/payload copying happens under the lock;
IRPs complete only after releasing it. Enqueue copies at most 480 frames
(11,520 PCM bytes) per call; larger source advances become timing loss. A read
copies at most 480 whole frames to preallocated per-file scratch, serializes
into the pinned output, then commits cursors/sequence. Small buffers, malformed
requests, and failed encodes do not consume data or mutate an attachment.

One operation per handle is enforced with an interlocked busy flag. Reads never
pend: they return a bounded block or a canonical v1 status. No driver cancel
routine or pending IRP list exists. Cancellation observed before the locked
transaction completes STATUS_CANCELLED with zero bytes and no state changes.
Cancellation arriving after that linearization point loses to successful
completion; never report cancellation after consuming frames. Cleanup marks
its file context closed and revokes/flushes its attachment under the same lock.
Close drains file-context rundown before freeing it and defensively revokes any
remaining ownership. Every handled IRP has one completion path.

Lock order:
1. PASSIVE stream lifecycle mutex for SetState, SetWritePacket, SetContentId,
   and buffer allocation/free (DPCs never acquire it).
2. Stream PositionSpinLock when touching simulated DMA position/packet state.
3. Global transport spinlock for queue/session/rights publication.

Adapter control-device start/stop has a separate PASSIVE lifecycle mutex and
then the transport spinlock. No path holding the transport lock calls back
into a stream or takes either lifecycle mutex. No allocation, paging, mapping,
wait or IRP completion occurs under a spinlock.

The transport context/ring is allocated nonpaged at DriverEntry and retained
until unload. Stream/adapter pointers are opaque identity comparisons; the
control path never dereferences a stream pointer. STOP/surprise/remove gates
and flushes export before PortCls cleanup; REMOVE deletes the control link and
marks its device for deletion. Existing handles retain the device object until
close and see removal, not freed queue storage. Power-down gates/flushes and
revokes eligibility. Resume requires fresh valid stream state/rights; reopening
the stream may be necessary. No automatic restoration of export is assumed.

Stream timer deletion now precedes miniport/format/DPC-resource destruction.
Buffer free gates export, marks position updates inactive, deletes the ExTimer
with wait=TRUE, drains queued DPCs, then unmaps/frees the MDL. Position queries
check the inactive-buffer gate. Reallocation recreates the notification timer.
Correct real PortCls/PnP callback ordering still needs installed-driver tests.

## WaveRT consumption audit

Microsoft defines SetWritePacket as publication that the OS wrote valid data:
[SetWritePacket](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/portcls/nf-portcls-iminiportwavertoutputstream-setwritepacket).
A circular write position alone is insufficient. Only successful, recognized,
frame-aligned packet commits establish the export window. Notification buffers
are also required to contain whole frames per packet. Partial/zero EOS and
unsupported modes are handled conservatively; no inference of missing PCM.

UpdatePosition runs under PositionSpinLock in the timer/position-query paths.
It computes elapsed virtual DMA consumption. Copy occurs BEFORE publishing the
new play/write position or notification. The source span must be entirely
within the absolute committed window, within the last cyclic-buffer capacity,
whole-frame aligned, and no more than 480 frames. Wrap is copied frame-wise
into private storage. Unknown commit state, late/overwritten/uncommitted spans,
and delayed callbacks beyond that budget export no bytes and count whole-frame
timing loss. Non-packet/polled mode can render but does not export PCM.

The inherited 32-bit elapsed-byte product is now 64-bit with overflow guards.
The completed-packet counter follows consumed position rather than incrementing
only once after delayed callbacks. SetWritePacket validates unknown flags and
EOS alignment. The OS still owns and can write the mapped WaveRT pages: driver
locks cannot freeze user mappings. The safe-window argument relies on the
Windows packet/position publication contract, not on a hostile producer obeying
a kernel lock. These samples are untrusted audio data; no samples affect kernel
addresses/sizes. Actual scheduling/overwrite behavior must be checked in a VM.

## Rights audit and fail-closed behavior

Microsoft permits SetContentId at any stream lifetime point and defines copy
protection on capture paths:
[SetContentId](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/drmk/nf-drmk-idrmaudiostream-setcontentid),
[content rights](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/content-ids-and-content-rights).

Export starts denied. At every SetContentId callback, revoke and flush before
querying rights and clear the old packet window. Allow only a nonzero content
ID with a successful mixed-rights update, clear CopyProtect/DigitalOutputDisable
in the supplied rights with zero Reserved, and a successful IDrmPort::GetContentRights query for
the same ID with clear flags and zero Reserved. Unknown IDs, missing callback/interface, query
failure, protected flags and stale grant tickets remain blocked. Fix the sample's
old-ID rollback to restore the previous stream ID on failure. Playback itself
can continue while transport is blocked.

A generation ticket prevents a rights query completing after removal/state
invalidation from re-enabling export. After a successful eligibility decision,
a NEW packet commit must establish a NEW first-safe-frame floor; older buffer
contents cannot be exported merely because rights now permit new content.
No protected/unknown PCM is inserted into the queue. A protected wire flag is
not used as an authorization source. No DRM signing/compliance claim is made.

This policy may block all audio on Windows paths that never publish a positively
queryable unprotected content ID. That is an intentional unresolved usability
limit, not evidence that absence of protection implies permission. Live rights
callbacks and eligibility must be verified on a correctly signed isolated test
machine before declaring working export.

## Counters, generations and timestamps

Overflow drops oldest private whole frames and increments OverflowFrames.
Disconnected eligible consumption (and queued frames discarded on detach/cleanup)
increments DisconnectedFrames. TimingLostFrames counts consumed frames rejected
as an uncertain/unrecoverable source span. These classifications do not double
count. Queue flushes for a generation transition are signaled by discontinuity;
v1 has no additional generic generation-flush frame counter. Counter/generation/
session/sequence exhaustion stops export instead of wrapping.

Generations bump/flush on stream/state/rights changes and reconnect; read
sequence remains monotonic per session. FirstFrame advances across loss and
resets within the new generation. Reads stop at a frame-index gap, preserve
masks exactly, and report pending generation/overflow/disconnected flags until
a successful audio read. State queries and failed reads do not consume flags.

Read timestamps are explicitly EstimatedTimestamp: first consumed frame is
estimated from QPC at consumption minus whole-frame duration, with per-slot
QPC metadata. Read timestamps are clamped against prior delivered timestamps to
prevent regression across resets. They are not hardware timestamps, and
fractional-millisecond scheduling accuracy is not established. QPC frequency
changes fail closed. DPC latency/copy-budget performance remains unmeasured.

## Validation and unverified work

See D2_2_STATUS.md for exact build/test results. Native runtime and compile-time
tests exercise the production queue/window code; offline DACL tests validate
configuration, not real token access checks. A serialized native producer/
consumer stress test models the lock contract, not kernel scheduling.

Still required without any claimed completion:
- Signed isolated VM/test-machine load, actual service token and ACL denial
  tests, namespace/remote/impersonation/inheritance checks.
- Real Windows stereo/side/back negotiation and sample-exact channel tests.
- Actual rights transitions, protected capture denial, and blocked-default-ID
  usability investigation.
- Cancel/cleanup/close/process death, stop/surprise/remove/restart and sleep/wake
  races under Driver Verifier; verify queue/device/timer reference draining.
- Scheduling/commit-window correctness and latency under load, long playback,
  and timestamp correlation measurement.
- A user-mode transport service/client and routing integration (D3/D4).

Do not interpret passing compilation/unit tests as proof of any of these.
