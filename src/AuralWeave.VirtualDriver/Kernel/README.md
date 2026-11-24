# AuralWeave Virtual Audio — Kernel prototype

**STATUS: developer-only source. NOT an installable or production audio driver.**

This directory is a derivative of Microsoft's `audio/simpleaudiosample`
from `microsoft/Windows-driver-samples`, pinned at
`d49bb7d72f3651952b2b6e4ad28d14fe2247eb69`.
It is **licensed under the Microsoft Public License (MS-PL)**, not
under AuralWeave's root MIT license. All original Microsoft file
attribution/copyright notices are preserved. The full MS-PL text is in
[LICENSE.MS-PL.txt](LICENSE.MS-PL.txt).

## Implemented

- Reproducible Visual Studio / WDK 28000 Windows x64 C++ driver build.
- Product-specific `AuralWeaveVirtualAudio.sys` kernel binary (unsigned).
- Single INF with separate DDInstall/KS interface registrations:
  - **Virtual Input** — Windows capture/recording device (microphone).
  - **Virtual Output** — Windows render/playback device (speaker).
- The adapter reads the root-enumerated PnP device **hardware ID**
  and registers *only the correct direction* for that physical devnode.
  Nonmatching or malformed hardware IDs fail closed.
- The INF reserves one recording device PnP ID ending in VINPUT_0001
  and one playback device PnP ID ending in VOUTPUT_0001.
  These represent **candidate device identities**, not proven simultaneous
  working audio devices.
- Microsoft sample's synthetic tone generator is disabled by default.

## OS targets

Minimum Windows 10 **version 2004 (build 19041)** x64, including
Windows 10 22H2 (19045); also targets Windows 11 x64 (21H2 and later).

The minimum version is based in part on `ExAllocatePool2`, which is not
available on older Windows 10 builds. The common `NTamd64.10.0...19041`
INF model decoration covers Windows 10 and Windows 11.

**A matching INF is not proof of runtime compatibility.** The driver
has only been compiled and checked via InfVerif and Inf2Cat for
representative Windows 10 and Windows 11 operating system targets.
Windows 11 and Windows 10 runtime/HLK validation remains required.

## Test without installation

Run the project-root script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-virtual-driver-prototype.ps1
```

This runs the WDK build, `InfVerif /w`, `InfVerif /u`, and an Inf2Cat
**signability test** for multiple Windows 10/11 x64 variants.

Build outputs are staged under ignored
`build/virtual-driver-prototype-package/Release`. The process does
**not** issue an INF catalog, sign the kernel binary, call PnPUtil,
modify boot settings, or install a device.

## Still required

1. A real per-device audio transport between WaveRT miniport and
   AuralWeave's Native C++ DSP and routing engine (not implemented).
2. Driver instance isolation, timing, synchronization, cancellation
   and PnP lifecycle robustness, including parallel devices of both
   roles (not validated).
3. Explicit and safe device creation/removal flow using signed INF
   and a privileged helper. PnPUtil alone does not create root devices.
4. Proper Windows driver signing and Windows Hardware Lab Kit
   (HLK) certification/testing for the Windows versions claimed.
5. Validation on *separate Windows 10 and Windows 11 test machines/VMs*
   before enabling the application INSTALL buttons.

**Do not copy this SYS/INF into the public Portable ZIP or install it
on an everyday PC.** Studio must continue operating without a driver.
