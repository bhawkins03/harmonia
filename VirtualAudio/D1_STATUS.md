# Harmonia D1: minimal render endpoint

The Harmonia-owned fork is in `VirtualAudio/driver`. It derives from Microsoft
Windows-driver-samples commit `f4c1ff842b6a0001152f44cb61788b216a1c7627`
(audio/sysvad); Microsoft's MIT license and copyright notices are retained.
The reference checkout under `.work` remains unmodified. WIL is not needed by
this fork because the APO projects are excluded.

## Build

From the repository root in a PowerShell session permitted to run scripts:

```powershell
./VirtualAudio/scripts/Build-Harmonia.ps1
./VirtualAudio/scripts/Build-Harmonia.ps1 -Configuration Release
```

Requires the same native tools as D0, including amd64 MSBuild and SDK/WDK 28000.
The build wrapper always requests x64, signing off, and deployment disabled.
Logs, binary logs and invocation metadata go under `VirtualAudio/.work/logs`.
Outputs are ignored under:

```text
VirtualAudio/driver/Package/x64/Debug/HarmoniaPackage/
VirtualAudio/driver/Package/x64/Release/HarmoniaPackage/
```

Each contains `HarmoniaVirtualAudio.sys`, `HarmoniaVirtualAudio.inf` and an
unsigned `harmoniavirtualaudio.cat`. No installation workflow is included.

## Implemented

- Friendly name: Harmonia Virtual Audio. Hardware ID: ROOT\HarmoniaVirtualAudio.
  Service and binary: HarmoniaVirtualAudio. Separate project/sample GUIDs and
  HarmoniaWave/HarmoniaTopology interface reference strings; standard Windows
  KS GUIDs are preserved.
- Exactly one render endpoint, one host render stream, and a topology bridge.
  No capture endpoints, offload pin, loopback pin, sample APO package,
  keyword-detector adapter package, or Bluetooth/USB sideband registration.
- Extensible IEEE Float32 at exactly 48 kHz: stereo mask 0x3; six channels
  mask 0x60F (side) or 0x3F (back). Six-channel side is the proposed default.
  RAW and DEFAULT modes only. No integer PCM, alternate rates, or 7.1.
- Explicit extensible data-range intersection, exact format/mask validation,
  valid-bits/alignment/average-byte checks, and pin bounds checks. The profile
  is compiled into the driver and shared with native policy checks.
- SaveData initialization, buffering, writes and work-item waits removed from
  the stream path; savedata.cpp is not linked. Render position/notification
  accounting is retained. Removed the sample's synthetic capture-tone path.

## Validation and limits

On 2026-10-31 both x64 configurations compiled, linked, passed WDK Universal
API/INF checks, and generated catalogs with no reported errors or warnings.
`tests/EndpointProfileTests.cpp` compiles with /W4 /WX and contains compile-time
positive/negative cases for the exact scalar format policy, including invalid
masks, integer PCM, sample rates, valid bits, byte rates, alignment and 7.1.
It does not simulate PortCls or Windows Audio runtime behavior.

The endpoint has not been loaded or tested on Windows. No claim is made yet
about actual shared-mode stereo/5.1 negotiation. No driver or certificate was
installed, and no audio defaults or boot settings changed.

This is a render sink: it advances audio position and discards PCM. There is
no kernel-to-user transport and no Harmonia routing integration yet. Installing
this build would not send sound to speakers. D2 must add a bounded, secured
transport with cancellation/removal/DRM handling before user-mode integration.
Signing, installation and actual audio/runtime validation remain separate work.

Final build evidence:
- Debug: `.work/logs/harmonia-Debug-x64-20261031-193037.{log,binlog,json}`.
- Release: `.work/logs/harmonia-Release-x64-20261031-193040.{log,binlog,json}`.
- Release driver size: 64,000 bytes. Authenticode status: NotSigned.
- Release driver SHA256: D343069C13E4322A5019F4E89FB3B6C2C2C1BCEB11EECAAD518CD1FA003ABF39
