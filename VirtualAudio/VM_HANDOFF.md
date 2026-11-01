# VM development handoff

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
logs are ignored; regenerate them in the VM from the committed source.

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
