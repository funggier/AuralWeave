# Implementation status — 2026-11-24

This document distinguishes implemented functionality from proposed architecture.

## Verified on the developer Windows 10 machine

- **MSVC compiler is already installed:** Visual Studio Community 2026, MSVC toolsets 14.51 and 14.52. `cl.exe` works when invoked through `VC\Auxiliary\Build\vcvars64.bat`. A C++20 hello-world smoke test compiled and ran.
- **Native C++ prototype:** `src/AuralWeave.Native/` compiles with `/W4 /WX`, performs Windows Core Audio endpoint enumeration, and passes 10 RouteGraph tests (duplicate-output prevention, independent repeated-effect instance IDs, ordering and scalar gain processing). Native route graph is **not wired into the desktop live audio path yet**.
- **Desktop/WASAPI prototype:** WPF UI can enumerate input, loopback and render devices; allows output selection without duplicates inside one route; runs shared-mode capture and render with per-output buffering, device format conversion and gain/mute; supports a repeatable *Gain* effect instance. The .NET Desktop and Core Release builds passed with zero warnings/errors.
- **Hardware read-only device discovery:** 1 active capture input, 5 render endpoints and 5 corresponding loopback endpoints detected.
- **Silent real-device smoke:** WASAPI `Line In (Realtek(R) Audio)` → `Headphones (Realtek USB2.0 Audio)` with output muted, 349,440 frames captured, 1 output telemetry entry, no reported errors and successful stop. This verifies opening and running the route; it does **not** establish actual sound quality or audible output.
- **Core rule tests:** 13 passing unit-style checks for dropdown filtering, uniqueness, repeated effects, bypass, PCM16/Float32 gain and peak metering.

## Deliberately incomplete / not yet proven

- No RNNoise processing implementation; UI shows a disabled future selector.
- No true EQ, voice conversion or AI voice effects; Gain is the only working DSP effect type.
- No actual native C++ WASAPI capture/render backend integrated with the WPF app.
- No persisted presets, multi-route management, driver installation or virtual audio endpoints.
- No clock drift correction for concurrent independently clocked output devices. Multi-output engine code exists but audible simultaneous output has **not yet been hardware verified**.
- No device hotplug recovery, long-duration stability testing, or accurate end-to-end latency measurement.
- Output selection changes during playback are intentionally disabled in current UI; per-output gain/mute updates are allowed.
- The current .NET capture callback and buffering approach is a prototype, not a hard real-time audio guarantee.

## Build tools

- [Windows / Desktop build script](../scripts/build-windows.ps1)
- [Native C++ smoke build script](../scripts/build-native.ps1)
- [Native C++ code](../src/AuralWeave.Native/)
- [Core tests](../tests/AuralWeave.Core.Tests/)

During Visual Studio Installer updates, avoid invoking build scripts concurrently with installer maintenance. Build scripts do not automatically install toolchains.

## Immediate engineering priorities

1. Stabilize native C++ Core Audio/WASAPI streaming with bounded buffers, deterministic Stop, and sink isolation; then decide the P/Invoke/IPC boundary to UI.
2. Validate muted multi-output fan-out and device drift on real hardware.
3. Correct plugin/processing lifecycle for dynamic effects without per-callback allocations.
4. Integrate RNNoise with 48-kHz frame-adaptation and clear bypass semantics.
5. Extend tests for hotplug, concurrency, processing order and portable release packaging.
