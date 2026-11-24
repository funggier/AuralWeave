# ADR 0003 — Native C++ as the sole audio backend

- **Date:** 2026-11-24
- **Status:** Accepted
- **Supersedes:** The dual-backend selection and managed fallback in ADR 0002

## Motivation

The user compared the running Windows preview against the earlier managed implementation. They reported that Native C++ worked substantially better and that its latency was **not perceptible to them** during listening. This is qualitative feedback, not a measured delay in milliseconds.

## Decision

- All actual audio capture/render, device enumeration, mix format discovery and routing use the **AuralWeave.Native.dll** Windows Core Audio/WASAPI backend.
- Keep the .NET 8/WPF UI and a thin P/Invoke bridge, but remove NAudio, the managed capture/render implementation and the UI backend toggle.
- If the native DLL is missing or cannot be loaded, show an error. Do not silently switch to another backend.
- The source repository and portable build remain under `T:\Space\Projects\ProjectsProgram\AuralWeave`. The user copies portable builds to any testing folder manually.
- Preserve the current successful event-driven native streaming timing, multiple outputs, output gain/mute and ordered repeated Gain instances while consolidating the backend.
- Keep future effects and noise reduction modules independent of UI. A second audio backend is not a planned product feature.

## Implementation boundary

```text
WPF Desktop (controls & meters)
  -> AuralWeave.Core (route/output/effect data and validation)
  -> AuralWeave.Audio.Windows (P/Invoke wrappers and endpoint catalog)
  -> AuralWeave.Native.dll (MMDevice/WASAPI capture, DSP, fan-out, render)
```

## Residual risks

- Native currently supports only compatible mono/stereo PCM16 or Float32 mix formats. Unsupported endpoints must report an error.
- The resampler uses linear interpolation, and independent clocks may drift in long sessions. High-quality resampling, hotplug recovery, per-sink isolation and acoustic latency measurement remain future work.
- Keeping native-only does not mean the Windows app UI itself is written in C++; .NET/WPF is a non-audio control plane.
