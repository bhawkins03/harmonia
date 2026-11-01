# D2.2 validation status

## Suite main baseline update: 2026-11-01

The owner designated the VM-validated driver as the main Suite development
build. Signed VM tests passed normal six-channel PCM publication/isolation
through SYSTEM and restricted LocalService, unauthorized access denial, and
simulated CopyProtect denial while playback advanced. The normal Release driver
was restored and retested with exact deployed INF/SYS hash checks. The complete
Suite Release build and protocol/queue/rights/security/package checks passed
again after synchronizing with current GitHub main. See [VM_HANDOFF.md](VM_HANDOFF.md)
for detailed results and evidence locations; [VM_DRM_REVIEW.md](VM_DRM_REVIEW.md)
records the approved default-content rights change. These updates supersede
historical no-runtime-test/zero-content-ID statements in the original status
and audit. Frontend transport integration, remaining lifecycle/Verifier checks,
latency/timestamp validation and production distribution remain pending.
Lite branch heads and Lite implementation are outside this Suite change.

The original implementation and October 31 static-validation record follow.

The owner has authorized promotion of the D2.2 implementation through PR #20
to main as the VM development build. The original implementation was reviewed
as a draft; this promotion does not establish runtime safety or a signed release.
Further development and testing take place in a VM. See VM_HANDOFF.md.
The security-approval commit `5c863ea` was incorporated before kernel handlers.

Incremental source commits:
- `4195630`: bounded transactional queue and focused native/compile-time tests.
- `b2bb366`: secured control interface, packet/rights/lifetime integration,
  additional native stress tests and reproducible static-validation options.

Read [D2_2_AUDIT.md](D2_2_AUDIT.md) for the implementation, exact lock/lifetime
rules, security boundary, source-publication audit and remaining limitations.

## Implemented

- 8192 whole-frame nonpaged queue; exact stereo/side-5.1/back-5.1 bytes/masks.
- Approved separate LocalSystem/service-SID control device, exclusive and
  handle/process-bound consumer, read/write access requirements, direct reads.
- Frozen v1 capabilities/attach/read/state/detach protocol integration.
- Nonblocking transactional reads, generation/sequence/discontinuities and
  separate overflow/disconnected/timing loss counters; no counter wrap.
- File-context rundown, cleanup revocation, explicit cancellation linearization,
  adapter/power/removal gates and retained queue lifetime through unload.
- Positive rights-query gate and post-rights-change fresh packet-commit floor.
- Timer callback draining before freeing stream resources or WaveRT mapping.

## Tests and builds on 2026-10-31

Both final commands passed:

```powershell
./Build.ps1 -Configuration Debug -Analyze
./Build.ps1 -Configuration Release -Analyze
```

Each command builds the desktop app, compiles existing format-policy checks,
runs all transport tests, validates the offline DACL configuration and forces a
native static-analysis driver/package rebuild. Ordinary Debug/Release builds
without analysis also passed during integration.

| Validation | Debug | Release |
| --- | --- | --- |
| Desktop build / existing endpoint policy | Pass | Pass |
| C++ wire tests | 11,795 checks; 15 vectors | 11,795 checks; 15 vectors |
| C# wire tests | 9,844 checks; 15 vectors | 9,844 checks; 15 vectors |
| Mutation parity | 10,000 inputs agree | 10,000 inputs agree |
| Queue tests + serialized producer/consumer stress | 14,999 checks | 21,249 checks |
| Compile-time production queue/window proofs (/kernel /W4 /WX) | Pass | Pass |
| Offline protected DACL/service-SID configuration | Pass | Pass |
| Driver link, Universal API/INF checks, catalog generation | Pass | Pass |
| Native static-analysis diagnostics | 99 inherited warnings | 99 inherited warnings |

Stress-test check counts depend on scheduling; both runs include 1000 producer
iterations and complete consumer drain. This is a user-mode model of the
single-lock queue contract, not a kernel concurrency/IRP test.

Static validation is **not warning-free**. The pre-integration source was
archived from `4195630` into ignored `.work/static-baseline/` and rebuilt with
the same /p:RunCodeAnalysis=true settings. It reported 99 warnings. Both final
configurations report the same 99 warning signatures after normalizing shifted
source/reference line numbers; there are no additional signatures. Inherited
findings include uninitialized sample-class members and null/bounds concerns
in sample helpers. They remain unresolved; no broad warning suppression was
added. The only local new suppression documents permitted WDM MajorFunction
registration/interposition (C28175); dispatch types/IRQL/pageability warnings
in the new paths were corrected rather than suppressed.

Evidence (ignored):
- `.work/logs/d22-final-debug.log` and `d22-final-release.log`.
- `.work/logs/d22-static-baseline.log`.
- Driver metadata/binlogs: `harmonia-Debug-x64-20261031-203102.*` and
  `harmonia-Release-x64-20261031-203207.*` under `.work/logs`.
- Golden/mutation outputs: `.work/tests/transport-{Debug,Release}/`.

Windows Application Control initially blocked a newly compiled native executable
and then the managed apphost. A later native retry succeeded. The managed suite
now builds and uses the standard `dotnet <test.dll>` entry point. All final
suites executed successfully; no Application Control/security policy was changed.

## Limits and what still needs a loaded driver

No driver, certificate or service was installed. Release `.sys` remains
NotSigned. No default audio devices, boot settings or Windows security settings
were changed. The WASAPI backend and desktop routing source are unchanged.

This is not proof of operational PCM export. A correctly signed isolated test
environment must verify real service tokens/ACL denials, namespace/remote/
impersonation/inheritance behavior, ordinary-user denial, direct-I/O mapping,
cancel/cleanup/close/process-death races, PnP/power/removal rundown, actual
WaveRT packet publication and exact channel content under load, and protected
content denial under Driver Verifier. No claim is made that these tests passed.

Export may remain entirely blocked on Windows audio paths without a nonzero,
positively queryable unprotected content ID or without SetWritePacket commits.
That is deliberate fail-closed behavior, not working-playback evidence. Rights
changes require fresh commits; an unsupported mode has no hidden loopback
fallback. Resume/PnP restart may require a fresh stream/rights callback. QPC
read timestamps are explicitly estimated; actual DPC latency is unmeasured.
Generation flushes are flagged but v1 has no generic flushed-frame loss counter.

A user-mode transport service/client and routing integration remain D3/D4 work.
Do not advertise this main development build as an installable or validated audio release.
