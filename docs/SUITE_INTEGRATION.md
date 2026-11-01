# Suite development integration

Suite 0.3.0 retains the pre-split WinForms interface. The final drag/drop baseline
is commit `3f494c1` (after the speaker layouts, live meters, strict-input, rear
diagnostics and recovery commits); `1400a79` adds the retained branding. These
features were still present on main. The incorrect Lite tray label is corrected.
The unrelated galaxy UI branch is not merged. Lite source/updater/workflow and
branch refs are unchanged by this implementation.

## Processes and audio flow

`MainForm` -> `SuiteAudioEngine` -> authenticated local named pipe ->
`HarmoniaAudioTransport` restricted LocalService -> existing v1 kernel transport
-> per-role channel mapper -> per-device adaptive stereo buffer -> WASAPI.

`Suite/Service/DriverClient.cs` uses the existing `Harmonia.Transport` codec and
reply binding checks. Only the service opens the kernel device. Attach/state/read/
detach, generation changes, discontinuities, overflow, removal, close and retry
are handled explicitly. Denied/protected/inactive states flush output queues.
Reconnection never substitutes loopback. The UI selects diagnostic loopback
explicitly; its original engine is retained, with Suite gain support added.

Roles map to FL, FR, FC, LFE, and the separate rear L/R pair. Mono roles duplicate
their assigned channel to their physical stereo endpoint; headphones retain
separate rear channels. Stereo transport never synthesizes center/LFE/rears.
Layout modes select roles, not an automatic content upmixer. The independent
loopback diagnostic retains the original optional stereo upmix behavior.
Gains are saved and sent to the service live without reopening output devices.
Unavailable device IDs stay saved so reconnecting a device does not erase maps.

Each output has a bounded 48-kHz stereo ring with fractional interpolation,
50-ms target occupancy and a smoothed, bounded +/-2000-ppm correction. WDL
resampling adapts other endpoint sample rates. Overflow is bounded and counted;
underrun silences and reprimes. This is software clock following, not a claim
that independently clocked DACs are sample synchronous. Physical latency,
long-running stability, resampler quality and actual device-removal recovery
remain hardware test gates.

Device discovery for virtual routing comes from the service's Windows audio
session. This matters under RDP/Hyper-V enhanced sessions: the UI may see only
Remote Audio while the service sees the local driver. Session-private remote
outputs are not silently advertised as usable service sinks.

## Control security

The pipe is versioned, length-framed and capped at 32 KiB. Its protected DACL
allows SYSTEM, the service identity/SID and interactive users. The persistent
first pipe instance prevents replacement while the service is running.
Impersonation identifies each client; unauthenticated/network-only tokens are
denied. An owner SID lease prevents another desktop user from changing an
active route. A 15-second missing-controller heartbeat stops routing. There is
no PCM download API and no user-mode content-protection override.

The service has its own process, restricted service SID and administrator-only
service configuration controls. Service files and the runtime are read-only to
the service. It is deployed self-contained with sidecar runtime files: a native
single-file extraction cache would require additional writes from the restricted
token and failed live VM testing. The ordinary frontend refuses elevation.

## Build and test

```powershell
./Build.ps1
./scripts/Build-Suite.ps1
```

The root build includes the service, installer and deterministic Suite tests,
alongside native profile/protocol/security checks and the unsigned driver.
The extended script adds WinForms persistence/control tests and an independent
Lite compilation. If host Code Integrity blocks unsigned development test
binaries, preserve that policy and explicitly defer execution:

```powershell
./scripts/Build-Suite.ps1 -UiInVm
$session = ./scripts/VM/Connect-TestBench.ps1
./scripts/VM/Test-SuiteService.ps1 -Session $session
```

The VM harness validates the named Generation 2 VM and checkpoint, deploys
hash-checked files with protected ACLs, runs C++/C# protocol parity, Suite unit
tests and the real UI tests, creates a restricted service, and launches an
interactive limited-token controller while generated six-channel audio plays.
It removes its temporary service/task and retains logs. It never modifies the
kernel package. `-ExpectProtected` requires a separately installed simulation
driver. `-RoutingPlanPath` accepts a plan with distinct real output IDs and runs
physical WASAPI loopback frequency/isolation checks; it does not invent sinks
when the VM lacks the required hardware.

The deterministic tests cover channel/gain identity, rear separation, stereo
silence policy, bounds, owner authorization, ten-minute simulated clocks at
-1000/0/+1000 ppm, compatibility, release policy, and failed-update transaction
rollback. Simulations and injected transaction failures are not substitutes for
physical clocks or signed Windows installation/update tests.

## Updates and deployment

`scripts/Package-SuiteDevelopment.ps1` produces a clearly marked, self-contained
VM development archive containing App/, Service/, integrity hashes and a
VM-identity-checked service installer. It includes no kernel driver or
certificate and cannot be consumed by the public updater. Its installer accepts
only Harmonia Test Bench with an already running validated driver, copies into
protected Program Files, installs restricted LocalService, and leaves the
ordinary app unelevated. `-Uninstall` removes only its exact development service;
driver, settings and retained files are preserved. This is separate from the
trusted production release workflow.

See [Suite release contract](SUITE_UPDATES.md). Public automatic updates remain
disabled because no production publisher certificate/Microsoft-signed package
was supplied. This was explicitly selected by the owner. No development
certificate, test-signing setting or Secure Boot modification is added by the
updater/installer. Kernel mutations in the installer are currently restricted
to the guest registry identity `Harmonia Test Bench`.
