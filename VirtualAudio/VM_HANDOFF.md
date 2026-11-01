# VM development handoff

## Suite integration update: 2026-11-01

The D3/D4 service/frontend source is now implemented; historical statements below
that these components do not exist are superseded. See
`docs/SUITE_INTEGRATION.md` and `docs/SUITE_UPDATES.md` for architecture and gates.
The full UI remains based on pre-split `3f494c1` plus `1400a79` branding. Lite
implementation, update workflow and branch refs are outside this Suite work.

The final protection/restore cycle passed:

- Simulated CopyProtect, service 0.3.0 and Debug driver `10.28.11.732`: zero PCM
  before and after controlled service restart, with Protected observed during
  generated-tone playback. Native SYSTEM/restricted-token probes also passed.
  Logs: `VirtualAudio/.work/vm-logs/20261101-104026-05316a25/` (service) and
  `20261101-103717-20b8d7e2/` (native driver).
- Restored normal Release driver `10.40.31.613`. Native publication, six-channel
  isolation and access denial passed. Logs: `20261101-104414-3bca3505/`.
- Suite service received 441,216 frames before restart and 441,744 after, peaks
  0.02 on all six channels. Restricted LocalService and fresh handle acquisition
  after restart were checked. Temporary service/task cleanup completed.
- Final Suite VM tests: 180,171 deterministic routing/clock/release/rollback
  checks; actual map drop and persistence, layouts, gains, saved missing devices
  and tray identity; C++/C# golden-vector and 10,000-mutation parity, native queue
  and 32 rights-policy checks. Logs: `20261101-104754-c0a4fdfa/`.

Service-visible physical outputs were an empty list; the interactive RDP session
reported only Remote Audio. Physical multi-device routing/isolation, removal,
drift and latency cannot be claimed from this VM run. The source provides an
explicit physical-routing/loopback probe for a suitable environment.

Public updates are disabled by explicit owner choice until production publisher
certificates and a Microsoft-signed driver are available. The production
installer rejects an unconfigured publisher; no trusted positive release or
real signed Windows update/rollback test has been performed. Injected transaction
tests are not substitutes for these gates. No public release was created.

The VM-only development archive
`dist/Harmonia-Suite-VMDevelopment-20261101-105226.zip` passed a real guest
install/controlled-uninstall test. Restricted LocalService, service health and
the quoted protected executable path were checked; driver version/INF and user
configuration were preserved. The temporary service was removed; installed
files remain for recovery. Logs: `20261101-110223-acda2089/`. The archive contains
no SYS/INF/CAT or certificate and is not a public update payload. SHA-256:
`97D6DE7D25FA6CA0D8E6379D41ACBED78B6B367D32183011F76C597665161505`.

Host Code Integrity blocked an unsigned rebuilt native protocol probe and one
UI assembly during development. Policy was preserved; the complete regression
was executed in the authorized VM. The transient slow VM connection recovered
after reconnecting; heartbeat and resource usage remained healthy.

## Live validation update: 2026-11-01

This update supersedes the historical unverified/deployment statements below.
Resumed using elevated host PowerShell and PowerShell Direct to the running
Generation 2 Harmonia Test Bench. No reboot or driver replacement was needed.
The guest reports Harmonia Virtual Audio bound to `oem10.inf`, version
`23.56.51.844`; package/source identity was not re-established in this run.

Rebuilt all runtime probes and ran `Test-DriverRuntime.ps1 -ObserveTransport`:

- All 32 content-rights policy checks passed.
- Unauthorized administrator transport access was denied (three checks).
- Restricted LocalService transport checks passed (3,559 checks).
- SYSTEM exported 55,232 frames; restricted LocalService exported 55,296.
  Both collected 4,096 tone frames, measured peak 0.02, and passed six-channel
  tone identity/isolation with zero Protected replies during unprotected audio.
- WASAPI rendered 100,320 frames at 48 kHz / six-channel Float32, with device
  clock progress and software loopback peak 0.02. No default endpoint changed.
- The temporary test service was deleted successfully.

The latest guest logs already contained a successful SYSTEM publication test;
the older saved host failure did not represent that later guest result.
Default-content rights handling is therefore live-tested for these unprotected
streams. Simulated CopyProtect denial also passed in the subsequent run below.
The lifecycle, Driver Verifier, latency and timestamp checks remain pending.
This does not establish production readiness or licensed-content certification.

Unprotected-run logs were collected to
`VirtualAudio/.work/vm-logs/20261101-092337-f1a2b553/`; cleanup inspection confirmed
no temporary transport service and zero remaining probe tasks.

Protection-test continuation: the Debug CopyProtect-simulation package was built,
signed, and installed as `oem11.inf`, version `9.24.8.519`. The workflow's
`Stop-Computer -Force:$false` was refused because a desktop user was logged in;
the failure report/logs are retained (logs `20261101-092448-7977f871`). A subsequent
Hyper-V `Stop-VM` without Force/TurnOff shut the guest down gracefully and it was
started again. The guest then reported the new package. After guest desktop
sign-in, simulated CopyProtect denial passed: SYSTEM exported zero frames with
125 Protected replies, and restricted LocalService exported zero frames with
126 Protected replies. WASAPI rendered 100,320 frames, clock progress was
1.96965 seconds, and software loopback peak was 0.02. The 32 policy checks and
access-denial/restricted-token checks passed; the temporary service was deleted.
Logs: `VirtualAudio/.work/vm-logs/20261101-092959-8dfd28ad/`.

Normal Release restoration completed successfully (version `9.30.11.164`).
The full workflow passed build, signature/catalog verification, installation,
graceful reboot, healthy endpoint enumeration, bound INF/SYS hash matching,
and live transport/WASAPI tests. SYSTEM exported 55,456 frames and restricted
LocalService exported 54,912; both passed six-channel identity/isolation with
4,096 sampled frames, peak 0.02, and no Protected replies. The 32 policy checks,
three administrator-denial checks and 3,559 restricted-token checks passed.
The temporary service was deleted. The VM is left running on the normal Release
driver, with no simulation requested. Final logs:
`VirtualAudio/.work/vm-logs/20261101-093207-1116b81b/`.

`Test-Harmonia.ps1` now uses the tested graceful Hyper-V shutdown path without
Force/TurnOff, instead of the Stop-Computer call that refused the signed-in
desktop. Offline workflow checks passed after the change; the Release workflow
also exercised the new reboot path successfully against the VM.

The owner designated the D2.2 audio-driver source as the main development build.
All further development and driver/runtime testing take place in a VM. This
handoff records the build and pending work; it does not install anything or
change VM/host security, boot settings, certificates or default audio devices.

## Get the source

```powershell
git clone https://github.com/bhawkins03/harmonia.git
cd harmonia
git switch main
```

For an existing checkout, preserve local work, fetch origin and fast-forward
main. Main includes the D2.1 protocol and D2.2 transport implementation from
PR #20. Generated executables, driver packages, build caches and machine-local
logs are ignored; regenerate them on the host from the committed source.
Compilation stays on the host; experimental installations belong only in the VM.
See [VM harness](../scripts/VM/README.md) for checkpoint, readiness, packaging
and log collection procedures. The harness installs drivers only in the named
test VM through its explicit installation approval step.

## Toolchain and validation

The validated configuration is Windows, .NET 8 SDK, Git, Visual Studio 2026
C++ Build Tools with WDK Build Tools and x64 Spectre/ATL libraries, plus matching
Windows SDK/WDK build 28000. See IMPLEMENTATION.md for detailed prerequisites.

From the repository root, in a PowerShell session permitted to run scripts:

```powershell
./Build.ps1 -Configuration Debug -Analyze
./Build.ps1 -Configuration Release -Analyze
```

These build the desktop app, run native profile/queue/window tests and C++/C#
wire conformance, check the offline control ACL configuration, and rebuild the
unsigned driver with static analysis and WDK API/INF/catalog checks. They do
not install the driver or service, add certificates, or change Windows settings.

Driver output: `VirtualAudio/driver/Package/x64/Release/HarmoniaPackage/`.
App output: `bin/Release/net8.0-windows/`. Machine-local evidence is under
`VirtualAudio/.work/logs/`. The build package remains unsigned.

## Read before continuing

- D2_2_AUDIT.md: packet publication, rights gate, lock order and lifetime model.
- D2_2_STATUS.md: exact prior test results and unverified behavior.
- D2_SECURITY.md: approved service SID and control-device access policy.
- PROTOCOL.md: frozen v1 offsets, bounds, operations and golden-vector contract.
- ROADMAP.md at the repository root: remaining development work.

The previous Debug/Release builds and available tests passed. Static analysis
reports 99 inherited warning signatures, unchanged from the pre-integration
baseline; this is not a warning-free driver. No loaded-driver verification has
been performed. Preserve these qualifications when reporting results.

## Next VM phase

Establish a separately reviewed signed test deployment before loading the driver.
Then verify real service-token ACL enforcement, packet publication/channel
content, protected-content denial, cancel/close/process-death behavior, PnP
removal/restart and sleep/wake with Driver Verifier. Measure DPC latency and
validate timestamp correlation. None of these is proven by unit tests.

Export deliberately fails closed without a positively queryable nonzero
unprotected content ID and fresh packet commits. Some Windows audio paths may
therefore export nothing. Read timestamps are estimated. Resume may require a
fresh stream/rights callback. V1 has no generic generation-flush loss counter.

A user-mode service/client and app transport/routing integration remain D3/D4.
Keep the existing WASAPI loopback backend intact. Do not claim end-to-end audio
or production readiness until the applicable VM tests have actually passed.
