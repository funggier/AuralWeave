# AuralWeave — current native-only implementation (2026-11-24)

## Architecture

- The only production-path audio backend is `AuralWeave.Native.dll` (C++20, WASAPI shared mode).
- .NET 8/WPF implements the desktop interface and native P/Invoke control plane, **not** managed audio streaming.
- Native endpoint enumeration is exposed through a C ABI; no NAudio package reference remains.
- The UI has a single Start/Stop button, dark dropdowns, 1 input, unique selected outputs per route, separate output gain/mute and repeatable Gain effect instances.
- Noise selection now supports **Bypass (default)** or **RNNoise v0.1.1 Standard**. Little and Custom model options are placeholders, not available in this build.
- Build and Portable Preview output are retained under the source project `T:\Space\Projects\ProjectsProgram\AuralWeave`. Copies under `T:\Space\Products` are user-managed testing copies, not the product's build location.

## Subjective user evidence

The tester reported that Native C++ performed much better and that its latency was **not perceptible** during listening. This is qualitative evidence, not a measured round-trip or capture-to-render time. Preserve the native WASAPI event-driven capture/render architecture that passed their testing.

## Automated evidence from prior checkpoint

- 16 native smoke/unit checks and 13 managed route-model tests passed.
- 30-second muted 192-kHz Line In -> 192-kHz USB Headphones + 48-kHz display audio test: 5,765,760 captured frames, 0 buffer overruns; no new overrun or underrun after the 2-second warmup.
- Dark theme and native event-driven multi-output design retained.

## Important unfinished work

- RNNoise v0.1.1 Standard is integrated; no Little/Custom model, EQ, voice-changing AI, presets or virtual audio driver.
- The native linear resampler has no production-grade anti-alias filter.
- Long-term independent device drift correction, hotplug, sink fault isolation and calibrated end-to-end latency measurement are pending.
- Native mix formats currently require mono/stereo Float32 or PCM16; unsupported formats fail with a clear error.

See [ADR 0003](adr/0003-native-only-audio.md), [Preview Notes](PREVIEW_NOTES.md), and [Test Plan](TEST_PLAN.md).
## RNNoise local preview progress

Native model and MSVC compatibility changes are recorded in [RNNOISE_INTEGRATION.md](RNNOISE_INTEGRATION.md). Muted hardware Bypass/RNNoise switching passed on mixed-rate output devices; subjective quality and measured end-to-end latency are not yet established.
