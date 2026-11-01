# Suite release and installation contract

The Suite updater is separate from Lite. Startup checks are optional and saved;
the full interface includes a manual check button and app/service/driver version
reporting. It considers only stable `v<version>-suite` releases and the exact
`Harmonia-Suite-win-x64.zip` asset. It never runs a GitHub upload merely because
the filename matches.

## Trust and manifest

Production builds require `SuitePublisherSha256` and
`SuiteDriverPublisherSha256`, SHA-256 fingerprints of the expected application
publisher and driver-signing publisher certificates. They are compiled into
Core assembly metadata, not downloaded or read from user settings. Empty pins
disable update checks and installation; no trust-on-first-use option exists.
The driver publisher may be Microsoft's hardware publisher after attestation/
WHQL signing, independently of the application publisher.

Each archive contains `suite-manifest.json` and detached `suite-manifest.p7s`.
Schema 1 JSON uses these exact property names:

```json
{
  "Schema": 1, "Channel": "suite", "Version": "0.3.1",
  "ServiceVersion": "0.3.0", "DriverVersion": "9.30.11.164",
  "AppServiceMinimum": "0.3.0", "AppServiceMaximum": "0.3.0",
  "ServiceDriverMinimum": "9.30.11.164", "ServiceDriverMaximum": "9.30.11.164",
  "UpdateService": true, "UpdateDriver": true, "TransportVersion": 1,
  "Files": [ {"Path": "App/Harmonia.exe", "Sha256": "<64 hex characters>", "Size": 123} ]
}
```

The example file list is abbreviated and cannot pass verification. Required
files include the app, installer, selected service component, and selected
driver INF/SYS/CAT. File paths must stay in App/Service/Driver/Installer. Every
file is covered by the signed manifest and SHA-256/length. Executables and driver
SYS/CAT require pinned publisher signatures and successful Windows trust
validation. CMS requires a strong digest, the pinned code-signing certificate,
a trusted chain and online revocation validation. Offline verification fails
closed. Archives reject traversal, links, duplicate/unlisted entries, invalid
paths, oversized files and excessive expansion. Certificates and installation
scripts are not accepted payloads.

## Packaging

`scripts/Package-SuiteRelease.ps1` publishes the app, service and installer,
signs executables with the supplied production certificate, requires an already
signed driver, checks catalog membership under Windows kernel signing policy,
creates/signs the manifest, runs the client verifier and produces a versioned
archive. `-ApplicationOnly` omits service/driver replacement and still requires
the manifest compatibility ranges to admit the installed versions. App/service
versions can advance independently. Driver INF version must match the actual
signed package. Never replace it with a timestamp guessed by the updater.

No release is published automatically by this script. The publisher must review
the verified archive and use the Suite tag/asset channel. Lite publishing is
unchanged. A production signing certificate and Microsoft-signed driver are not
available in this development checkout; successful real signed release/update
testing therefore remains unverified.

## Coordinated installation and recovery

Extract a verified publisher release and run its Installer executable. It asks
for UAC elevation and verifies the manifest again. The updater launches the
currently installed, signature-checked bootstrap from protected Program Files,
passes the staged directory, stops routing, exits, and lets that bootstrap wait
for the UI process. No privileged executable is launched from user-writable
download staging.

The installer copies into an immutable version directory beneath
`Program Files/Harmonia Suite/versions`, applies administrator/SYSTEM ownership
of configuration and read/execute access for the restricted service SID, and
reverifies the protected copy. Compatibility is checked against the components
actually being retained or replaced. A protected pending journal records the
previous and proposed installation before mutations.

It stops the service, updates/creates the exact root Harmonia devnode through
SetupAPI/NewDev, configures restricted LocalService, verifies service versions
and a real driver transport health call, then atomically commits `installed.json`.
Each version carries its own updater bootstrap; the committed state points at
the new bootstrap without overwriting a running installer. Explorer brokers
frontend relaunch, and the frontend independently refuses an elevated token.
User settings in LocalAppData are not replaced or migrated destructively.

Failures invoke rollback to the retained previous service/driver/app state.
Failed recovery retains the journal and reports both failures. If Windows
requires reboot, the installer returns 3010 and retains an AwaitingReboot
journal. After reboot run the protected version's installer with `--resume`;
`--recover` restores the previous installation instead. These maintenance
commands request administrator approval. They do not force a reboot or change
boot/security settings.

`--uninstall` stops/deletes only the named service and exact matching Harmonia
devnode. It unregisters the Suite uninstall entry while retaining user settings,
version directories and the driver-store package for recovery. It does not
delete arbitrary OEM driver packages. Reboot may be needed after removal.

The installation/rollback transaction is covered by injected failures at every
step; the Windows SetupAPI, real reboot/resume and trusted release upgrade paths
still need end-to-end signed-package tests. Do not describe these as passed or
ship a public updater until the missing signing and lifecycle gates are met.
