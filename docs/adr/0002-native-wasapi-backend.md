# ADR 0002 — Native C++ WASAPI and per-output format adaptation

- **Date:** 2026-11-24
- **Status:** Accepted for prototype; production quality pending

## Context

AuralWeave requires one source → one effects chain → many outputs. Windows endpoint mix formats vary. On the reference PC, Line In and USB Headphones were 192 kHz stereo Float32; display audio devices were 48 kHz stereo Float32. A fixed 48 kHz WASAPI prototype was rejected by an endpoint.

## Decision

- Keep NAudio managed WASAPI as the initial default backend, enabling a clearly labeled optional native backend.
- Implement separate native `AuralWeave.Native.dll`, using a versionable C ABI callable by .NET. Native audio/COM objects live and are released on their worker thread.
- Initialize each endpoint using `IAudioClient::GetMixFormat()` in shared mode.
- Normalize captured PCM16/Float32 to internal Float32, apply a single ordered gain-effect chain, then fan-out through per-output bounded streaming resamplers, channel adaptation, and independent gain/mute settings.
- Expose capture frame count, input peak, global queue-overrun frames, Start/Stop, fault error, per-output gain/mute, and effect settings through native API.
- Use prototype linear interpolation with explicit warning that quality, anti-aliasing, independent device drift compensation, and sink isolation remain unfinished.
- Do not automatically play audio or install a kernel driver.

## Consequences

- Native can be tested on devices with differing mix rates without a fixed format assumption.
- In prototype, Native accepts only mono/stereo Float32 or PCM16; unsupported formats must be rejected with an informative error.
- UI remains independent of backend selection through `IAudioRouteSession`.
- Native and managed implementations currently coexist. They are not identical in behavior or telemetry completeness.
- Before claiming product-ready, add higher-quality resampling, native per-sink error metrics/clock drift handling, nonblocking state swaps, and real-audio quality validation.
