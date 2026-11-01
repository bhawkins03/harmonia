# Harmonia virtual audio endpoint

Status: the minimal Harmonia driver fork (D1) builds in x64 Debug and Release.
See [D1 build instructions, behavior and limitations](D1_STATUS.md).

The driver advertises stereo and 5.1 Float32 at 48 kHz with its own device ID,
service, binary and INF. It is unsigned and has not been installed or runtime
tested. It currently discards render audio; transport to the Harmonia routing
engine remains to be implemented.

`driver/` contains the Harmonia-owned SysVAD fork and retained Microsoft MIT
license. `scripts/Build-Harmonia.ps1` builds it. The pinned unmodified reference
workflow remains available through Prepare-SysVad.ps1 and Build-SysVad.ps1.
See [implementation sequence](IMPLEMENTATION.md) for the remaining phases.
