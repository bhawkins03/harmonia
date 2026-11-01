# Harmonia editions

Harmonia is **one codebase with two distribution targets**, not two diverging forks.

## Harmonia Lite

Driverless Windows WinForms audio router, based on the existing WASAPI
loopback backend. It can distribute audio among selected Windows render endpoints
and retains the existing experimental speaker maps, channel tests and meters.
It must not claim native, independently selectable virtual surround capture.
A default Windows endpoint cannot also be reused as a routed output in this
backend, and multi-device clock drift remains a limitation.

Lite packaging never includes .sys, .inf or .cat files. It should remain
independently usable and testable without driver privileges.

## Harmonia Suite

Future full desktop app + signed Harmonia Virtual Audio render driver +
secured transport service + routing engine. Intended to accept real stereo
and 5.1 48-kHz Float32 via the selectable Windows virtual playback endpoint.
Users choose it explicitly in Windows Sound. The suite requires signed
installation, privilege-aware upgrades/uninstallation, separate service
lifetimes and real VM/hardware verification.

**Not a finished edition yet.** Although D2.1 and D2.2 driver source is on
main, there is no deployed, validated end-to-end driver-to-app transport or
user-mode audio service. The driver does not yet provide a working virtual
sound output. Do not label an unsigned development archive a public Suite
release.

## Friend-friendly download

When the automated preview release succeeds, Lite can be downloaded from the
[Releases page](https://github.com/bhawkins03/harmonia/releases) as
`Harmonia-Lite-win-x64.zip`. Extract and run `Harmonia.exe`; no SDK, Git,
PowerShell or separate .NET runtime is required. This is a portable unsigned
preview, not an installer. Suite has no user-ready release yet.

## Lite updates

Lite checks the official `bhawkins03/harmonia` GitHub Releases API once when
its window first opens, if **Check for Lite updates on startup** is enabled.
The checkbox is saved in the existing per-user
`%LOCALAPPDATA%/Harmonia/settings.json`; it is enabled by default. Users can
turn it off or click **Check for updates now** at any time.

The check considers only newer numeric `vX.Y.Z-lite-preview` releases with
a `Harmonia-Lite-win-x64.zip` asset. It does not check Suite or other apps.
The user must approve the update before the application downloads the ZIP.
A temporary helper then attempts to replace the portable `Harmonia.exe`
after the app exits and relaunch it; no admin permissions or driver actions.
Updates can fail if the directory is write-protected or the archive is damaged.
Per-user speaker settings are stored separately and remain unchanged.

**Security / distribution limits:** This is an unsigned preview updater;
HTTPS/GitHub metadata and archive shape/size are checked, but the downloaded
binary is not yet authenticated with an independent code-signing trust chain.
Only enable it for releases you trust. Production releases should use
Authenticode signing, SHA-256 verification and an atomic rollback-safe updater.
The updater should undergo VM smoke tests before this feature is shipped.

Maintainers must update the `<Version>` in `Harmonia.csproj` to match the
numeric version of the GitHub release tag. Pushing an annotated tag such as
`v0.2.0-lite-preview` triggers `.github/workflows/build-lite.yml` to
compile and publish that release. Do not reuse existing release tags.

## Development packaging

From the repo root on Windows with the .NET 8 SDK:

```powershell
./scripts/Package-Edition.ps1 -Edition Lite
./scripts/Package-Edition.ps1 -Edition Lite -SelfContained
```

Lite produces `dist/Harmonia-Lite-win-x64.zip`. By default the published app
is framework-dependent and requires the .NET 8 Desktop Runtime. The
self-contained option bundles the runtime but increases the archive size.
The default release is portable, not an installer.

For *developer-only VM work* with the full Visual Studio C++/SDK/WDK toolchain:

```powershell
./scripts/Package-Edition.ps1 -Edition Suite -ExperimentalSuite
```

This explicitly gated build produces
`dist/Harmonia-Suite-win-x64.zip` with the current desktop app plus a
separate `ExperimentalUnsignedDriver/` folder. **Not** a functional Suite,
signable release, or a driver installation workflow. The archive includes a
prominent warning; nothing installs drivers, certs or services.
Suite support is x64 only for now.

## Future shared-code structure

As the service arrives, keep a small shared routing core and two
application configurations:

- `Harmonia.Audio.Core`: PCM frames, channel masks, gains, DSP, routing,
  clock correction and sink abstractions. No UI, driver installation or OS
  default-device mutation.
- `Harmonia.Input.Loopback`: existing legacy input, shipped in Lite and
  optionally as an explicit diagnostic mode in Suite.
- `Harmonia.Input.Virtual`: transport protocol client, generation and
  discontinuity checks; Suite only.
- `Harmonia.Service`: long-running authorized consumer and routing host;
  Suite only, with strict service identity and ACLs.
- `Harmonia.App`: common UI/control plane; edition build constant controls
  which input backends and controls are offered.

Do **not** clone or reimplement the current `AudioEngine.cs` twice.
Prefer interface extraction and tests while preserving the working loopback
path. Never silently switch to loopback if a selected virtual input fails.

## Release gates

Lite: clean .NET build, device/loopback smoke tests, verified absence of
driver artifacts, no privilege requirement, versioned archive.

Suite: isolated VM security and cancellation tests, precise 5.1 channel
isolation and format negotiation, approved DRM behavior, restart/unplug and
sleep/wake, sustained multi-device playback, signed package and clean
install/uninstall. No functional Suite release before these pass.

Existing outstanding UI branch `feature/galaxy-inspired-ui` must be
integrated separately after build/runtime review; edition packaging does not
implicitly merge it.
