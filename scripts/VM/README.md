# Harmonia VM development workflow

Suite user-mode testing is in `Test-SuiteService.ps1`; see
[Suite integration](../../docs/SUITE_INTEGRATION.md). It deploys the restricted
service and limited-token interactive probes, checks protocol parity, UI and
driver-to-service delivery, and removes its test service/task. It leaves the
kernel driver unchanged. A separately prepared protected-simulation driver can
be tested with `-ExpectProtected`; physical routing can be tested with an
explicit `-RoutingPlanPath` when distinct service-visible output devices exist.
`Test-SuiteProtectionCycle.ps1` wraps the protected test, service restart checks
and normal Release restoration in a finally block; desktop sign-in is required
after each reboot. `Test-SuiteDevelopmentDeployment.ps1` tests the separate
development archive's install/uninstall without replacing the kernel driver.

Run from the repository root in **elevated Windows PowerShell 5.1** in VS Code.
Only the Generation 2 VM **Harmonia Test Bench** is accepted. No checkpoint is
created, restored, deleted or overwritten by the top-level workflow. Before any
changes it requires exactly one existing **Harmonia pre-driver** checkpoint.
Inspect its provenance in Hyper-V Manager; the name alone cannot prove it is clean.
Use the existing `Initialize-Checkpoint.ps1` deliberately on a known clean guest
if the baseline is missing. Do not create a new baseline over an installed driver.

```powershell
# First: inspect host state and intended actions; does not boot or connect to VM.
.\scripts\VM\Test-Harmonia.ps1 -DryRun
.\scripts\VM\Test-Harmonia.ps1 -PrepareGuest -DryRun

# One-time guest preparation (interactive approvals required):
.\scripts\VM\Test-Harmonia.ps1 -PrepareGuest

# Normal repeatable build, sign, deploy, install, smoke-test and log collection:
.\scripts\VM\Test-Harmonia.ps1 -Configuration Release `
    -DevconPath 'C:\Program Files (x86)\Windows Kits\10\Tools\10.0.28000.0\x64\devcon.exe'

# Also test removal after successful installation and smoke checks:
.\scripts\VM\Test-Harmonia.ps1 -DevconPath 'C:\path\to\WDK\x64\devcon.exe' -Cleanup

.\scripts\VM\Test-Harmonia.ps1 -BuildOnly
.\scripts\VM\Test-Harmonia.ps1 -DeployOnly
.\scripts\VM\Test-Harmonia.ps1 -CollectLogsOnly

# Offline safety tests; do not access Hyper-V or prompt for credentials:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\VM\Test-Workflow.ps1
```

Choose at most one mode; `-Cleanup` applies only to a full run. Debug is available
with `-Configuration Debug`. BuildOnly uses the existing driver build script;
DeployOnly signs and copies existing artifacts but never installs or trusts them.
Build and deployment require existing nonempty package artifacts, including for
DryRun. The current package already supplies these. Guest modes prompt once per
run for local administrator credentials, kept only in memory. BuildOnly and
DryRun do not prompt. DryRun checks administrator, tools, actual VM state,
checkpoint and artifacts but cannot establish guest readiness without connecting.
It does not write reports, sign, build, start, shut down or modify the guest.

The workflow accepts Running or Off; transitional, Saved and Paused states fail
for manual review. Off guests are started after checkpoint verification. Restart
uses Hyper-V's graceful guest shutdown integration without Force or TurnOff,
waits for Off, then starts the same VM ID. Timeout never
falls back to forced power-off. Stale sessions are recreated with the original
in-memory credential. A connectivity probe can be retried; a driver or security
mutation is never replayed after an ambiguous transport failure. Review logs
before retrying. The VM remains running afterward for inspection.

Every sensitive operation requires typing **APPROVE**, independently: guest
Secure Boot disablement, guest BCD TESTSIGNING, guest certificate trust, kernel
driver installation/update, and optional removal. There is no unattended approval
switch. PrepareGuest changes boot settings only if necessary and verifies them
after restart. It does not install a driver or trust a certificate.

Before boot changes the guest BitLocker cmdlet must positively report
FullyDecrypted. Encrypted or transitioning volumes fail closed: back up and
verify recovery-key access outside this harness and deliberately prepare/decrypt
the guest OS volume before retrying. The harness never retrieves or logs recovery
keys and does not suspend BitLocker automatically. VBS running blocks full driver
testing; the harness does not disable it. No host security, host trust stores,
host boot configuration or host firmware settings are changed. Test mode and
guest certificate trust remain until deliberately removed by the operator.

Development signing uses the pinned WDK SignTool and Inf2Cat. It creates or reuses
a uniquely named, unexpired code-signing certificate in **host CurrentUser My**
(with a separate creation approval). Its private key is nonexportable and is never
copied. Only the public CER is deployed. The SYS is embedded-signed first, the
catalog is regenerated for Windows 11 x64 (`10_GE_X64`), then the CAT is signed.
Verification checks signer identity, catalog cryptographic signature and actual
INF/SYS catalog hash membership. An untrusted development root is permitted on
the host without adding host trust; other signature failures stop deployment.
Guest signatures must validate after explicit Root/TrustedPublisher trust. An
existing unsigned CAT never counts as a signed package. These artifacts are for
this development guest, not production distribution. Host build artifacts are
signed in place; subsequent builds may replace them and the next run signs again.

The actual INF is checked for its known MEDIA class, catalog, SYS and
`ROOT\HarmoniaVirtualAudio` requirements. Changed requirements block automation.
A trusted signed WDK **x64** DevCon is required for full testing: `install` creates
the root devnode only if absent; `update` handles an existing single devnode.
Multiple matching devices stop for manual review. Missing unrelated Media devices
is safe and does not trigger device creation or removal. PnPUtil staging alone
is not used to create a root device. Reboot-required native exit codes are handled.
All native failures stop their stage. Removal targets only the tested exact
instance and its verified `oemN.inf`, without force or wildcard deletion.

A successful test verifies a present healthy device, zero PnP problem code,
Harmonia provider, exact bound INF hash, running kernel driver, audio services
and a healthy Harmonia audio endpoint. This is an **enumeration smoke test**;
it does not prove audio streaming, latency, stress stability or Driver Verifier
correctness. Failed install/tests still attempt log collection. Failed tests do
not automatically remove a driver, undo security changes or restore a checkpoint.

Logs go to ignored `VirtualAudio/.work/vm-logs/`; JSON stage reports go to
`VirtualAudio/.work/vm-reports/`. SetupAPI logs and seven days of System,
Application, Code Integrity, Kernel-PnP, DeviceSetupManager and available audio
event channels are exported. Missing or disabled channels are noted, not enabled.
Guest diagnostic directories under `C:\HarmoniaTest` and ProgramData are copied
when present. Guest log staging is retained for recovery. Review logs for personal
information before sharing. Cleanup removes the driver/device only; deployed
builds, certificate trust, host development key and logs remain for reuse/review.

The original scripts remain usable individually: Connect-TestBench,
Test-Readiness, Initialize-Checkpoint, Deploy-TestBuild and Collect-Logs. The
orchestrator reuses Common checkpoint/session checks, the existing build,
deployment and collection scripts. Deployment copies three package files to a
unique guest folder and verifies SHA-256; repeated runs avoid duplicate root
devices and reuse certificate trust but retain immutable deployment folders.

If local execution policy blocks reviewed scripts, use a dedicated
`powershell.exe -NoProfile -ExecutionPolicy Bypass` process. This changes only
that process, not saved execution policy. Live VM behavior and signing require
an elevated host with Hyper-V/WDK; offline tests cover syntax and safety guards,
not actual driver load or guest recovery behavior.
