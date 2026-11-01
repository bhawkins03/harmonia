# Development setup for driver work

For the pinned source preparation scripts, current toolchain requirements and
build results, see [IMPLEMENTATION.md](IMPLEMENTATION.md). D0 x64 Debug and
Release reference builds now pass. The historical outline below
is supplemented by that reproducible workflow.

The current Visual Studio/.NET Harmonia app is **not itself a driver**. SysVAD is C++ and uses the Windows Driver Kit (WDK).

1. Install Visual Studio with Desktop development with C++ workload, matching Windows SDK and WDK (check WDK/Visual Studio compatibility for your installed versions).
2. Clone the Microsoft reference into a *separate* working folder:
   ```cmd
   git clone --recurse-submodules https://github.com/microsoft/Windows-driver-samples.git
   ```
3. Open the `audio\sysvad\sysvad.sln` solution in Visual Studio. Configure all subprojects for matching platform/configuration. First build the unmodified reference **without deploying it**.
4. Preserve upstream license notices when adapting code. Use a separately identified device/INF and unique GUIDs and hardware IDs for Harmonia rather than shipping the sample unchanged.
5. Use a dedicated test computer/VM for any driver deployment. Keep normal driver signing protections enabled on personal daily-use machines.

Official build prerequisites and sample instructions:
https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/

This directory also contains pinned reference preparation/build scripts and a
native format profile. Kernel transport and packaging remain unimplemented.
