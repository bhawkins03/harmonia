# Harmonia virtual audio endpoint

This directory is reserved for the independent C++ WDK driver and native transport.

Status: **design phase only**. No .sys, INF, device installer or signed virtual endpoint is included. See [implementation plan](../docs/VIRTUAL_DEVICE_IMPLEMENTATION.md).

The existing C# Harmonia application cannot itself register as a Windows playback device. A virtual endpoint must be installed through a Windows audio driver and deliver the actual PCM data to the user-mode routing engine.

Initial upstream base: https://github.com/microsoft/Windows-driver-samples/tree/main/audio/sysvad

Do not install an unmodified sample driver expecting Harmonia routing. It does not connect to this program.
