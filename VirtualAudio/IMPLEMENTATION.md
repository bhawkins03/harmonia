# SysVAD source preparation and implementation

Status: D0 passed on 2026-10-31: pinned SysVAD x64 Debug and Release builds
completed with zero warnings/errors. No operational Harmonia endpoint exists.

## Reproduce D0

From the repository root in PowerShell with script execution permitted:

```powershell
./VirtualAudio/scripts/Prepare-SysVad.ps1
./VirtualAudio/scripts/Build-SysVad.ps1
./VirtualAudio/scripts/Build-SysVad.ps1 -Configuration Release
```

The preparation script checks out the exact Microsoft and WIL revisions in
`upstream.json`, preserving licenses. Defaults are under ignored `.work/`.
Use `-Destination` and `-Source` for a sibling checkout. Existing dirty checkouts
are refused. Keep this reference pristine; adapt a separate tree after D0 passes.

Baseline: Visual Studio 2026 Desktop development with C++, Windows Driver Kit
individual component, matching SDK/WDK build 28000, x64 MSVC Spectre libraries
and ATL including Spectre libraries. Microsoft's general setup also lists MFC
with Spectre mitigations. Upstream's v142/VS2019 component names are historical.
SDK/WDK build numbers must match; QFE revisions need not. The directory version
10.0.28000.0 does not record QFE. Record installed component versions with logs.
VS2022 + WDK 26100.6584 is a supported alternative requiring a separate kit-pin
change and validation, not the baseline scripted here.

Official sources reviewed 2026-10-31:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk
- https://learn.microsoft.com/en-us/windows-hardware/drivers/other-wdk-downloads
- https://github.com/microsoft/Windows-driver-samples/blob/f4c1ff842b6a0001152f44cb61788b216a1c7627/audio/sysvad/README.md

Build uses native MSBuild, x64 throughout, explicit kit version, signing off and
deployment disabled. `-MSBuildPath` and `-KitRoot` support custom installations.
Text/binlogs and source/argument/MSBuild-version JSON go under `.work/logs`.
Outputs use upstream paths. Source pinning does not guarantee identical binaries.
Nothing installs certificates, changes boot settings or selects audio devices.
Do not follow upstream deployment/security-disable instructions in this workflow.

Initial environment: .NET SDK 8.0.427; no native MSBuild/C++, SDK or WDK detected
in standard locations or kit registry/environment. Pinned source preparation
including WIL succeeded. Build preflight fails for missing native MSBuild.
This initial toolchain blocker was resolved on 2026-10-31. SDK 28000.2957,
WDK 28000.2526, and Visual Studio WDK Build Tools were installed. Both reference
builds passed using the amd64 MSBuild host, now selected by the wrapper.
See D0_STATUS.md for evidence. No driver deployment was run.

## D1: minimal Harmonia fork

Implemented and built; see [D1_STATUS.md](D1_STATUS.md) for the current fork,
commands, validation and runtime limitations. The outline below is the original
implementation plan; the minimal fork omits loopback as well as offload.

After successful reference Debug/Release builds, copy the sample into a separate
source tree and preserve all upstream MIT notices. Adapt these files under
`audio/sysvad` in small commits:

| Source | Change |
| --- | --- |
| TabletAudioSample/minipairs.h | One render pair; remove capture registration and audit empty-array/count assumptions. |
| EndpointsCommon/speakerwavtable.h, speakertoptable.h | Stereo and six-channel 48 kHz Float32; align format tables, topology channel counts and masks. |
| EndpointsCommon/minwavert.cpp | Keep intersection/proposed-format/mode checks consistent with the exact profile. |
| TabletAudioSample project/resources | Harmonia target, resources, project GUIDs and service/binary identity. |
| TabletAudioSample/ComponentizedAudioSample.inx | Unique hardware ID, names and matching interface reference strings. |
| Package/package.VcxProj | Harmonia only; remove APOs, keyword detector and sample extension associations. |
| EndpointsCommon/minwavertstream.cpp | Remove SaveData initialization, writes and work items; preserve timing and notifications. |

Reserve friendly name `Harmonia Virtual Audio` and ID `ROOT\HarmoniaVirtualAudio`.
Generate Harmonia-specific project/interface GUIDs when forking; retain standard
KS category/property GUIDs. Disable offload and sample audio modules, updating
pin indices/descriptors and their users together. Initially permit one render
stream, rejecting extra streams predictably. No installer in this step.

`native/EndpointProfile.h` is the first Harmonia-owned native source, containing
format constants/predicates for the future negotiation code. It is not wired
into a driver project or compiled yet. Six-channel side/back masks are explicit
alternatives. Do not advertise integer PCM or 7.1 before implementation. Verify
actual six-channel Windows shared-mode negotiation on a signed test build.

## D2: transport decision for review

Refine PROTOCOL.md toward a private nonpaged ring with batched direct-I/O reads,
rather than exposing writable shared cursors. Six-channel Float32 at 48 kHz is
1,152,000 bytes/sec; a bounded copy is a reasonable prototype tradeoff. No ABI
is frozen and no kernel transport is implemented yet.

- Inspect `UpdatePosition`, `WriteBytes`, `TimerNotifyRT` in minwavertstream.cpp.
  Copy newly consumed whole frames, handle cyclic wrap in two spans, and report
  delayed callbacks spanning the DMA buffer as discontinuities, not valid audio.
- Preallocate at PASSIVE_LEVEL. DPC work has a fixed budget, no allocations,
  disk I/O or waits on a consumer. Document lock/IRQL and publication rules
  before coding so copying cannot race overwritten/partially published blocks.
- Proposed capacity: 8192 frames. Drop oldest whole frames on overflow, count
  loss and continue rendering when disconnected. Never replay stale audio.
- Secure a separate control interface to one service identity with explicit
  SDDL/service SID. Validate access, versions, output lengths and arithmetic.
  No user-provided addresses/offsets or writable kernel mappings.
- Control: capabilities, attach, format/state/counters, detach. Read batches:
  at most 480 whole frames through I/O-manager-owned output MDLs. Define cancel,
  close, PnP removal and DPC reference draining before buffers can be freed.
- Serialize version/header length, generation, sequence, first-frame counter,
  QPC timestamp/frequency, rate/format/mask, payload length, loss and state.
  Define byte offsets and C/C# golden vectors before implementation; never
  marshal compiler padding. Restart/reconnect/format changes flush old audio.
- Audit SetContentId/DRM rights; protected content must never be exported.

## D3/D4: user-mode integration

Extract a start/stop/format/block source interface from AudioEngine.Start,
retaining the existing loopback backend. Add a transport backend after native
tests. Validate 48 kHz Float32 with masks 0x3 (2 channels), 0x60F or 0x3F
(6 channels); channel count alone is insufficient. Preserve FL FR FC LFE
SL SR or BL BR until routing, with independent headphone L/R. Test six unique
impulses/tones sample-exactly, overflow, restart and abrupt consumer exit.

Current AudioEngine already indexes six-channel capture separately but trusts
channel count and binds capture to the default endpoint. Tighten PCM subformat
validation before reusing it. Move DSP, routing, resampling, clock correction,
speaker assignments and meters to a user-mode host independent of the UI.
Clear destination buffers on generation/discontinuity. Never silently fall back
to physical-default capture when virtual transport fails. Selecting a physical
endpoint must bypass Harmonia, with bounded draining of previously queued audio.

Complete cancel/close, sleep/wake, endpoint removal, format changes, channel
isolation and long-playback tests before deployment. Signing/installation is a
separate future task with Windows security protections enabled.
