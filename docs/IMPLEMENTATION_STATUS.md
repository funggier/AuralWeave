# AuralWeave implementation status

## Portable Studio v0.3.1

Windows 10 22H2 x64 is the primary test platform. The app routes through the Windows endpoints selected in Studio, including independently installed VB-CABLE devices. See the [VB-CABLE guide](VB_CABLE.md) for application-to-application routing.

The Windows 10 build passed all 32 Core tests. UI Automation verified the single Studio screen. The installed VB-CABLE playback and recording endpoints both streamed in muted WASAPI routes. Mixed-rate physical outputs and live filter reordering passed separately.

## Architecture

- The only production-path audio backend is `AuralWeave.Native.dll` (C++20, WASAPI shared mode).
- .NET 8/WPF implements the desktop interface and native P/Invoke control plane.
- Native endpoint enumeration is exposed through a C ABI; no NAudio package reference remains.
- The Studio opens directly to Sound Input, ordered Filters, and Sound Output. It has a single Start/Stop button, dark dropdowns, 1 input, unique selected outputs per route, separate output gain/mute and repeatable Gain effect instances.
- Filters start empty. Gain, RNNoise v0.1.1 Standard, Noise Gate and Limiter can be repeated and bypassed individually.
- Build and Portable Preview output are retained under the source project `T:\Space\Projects\ProjectsProgram\AuralWeave`. Copies under `T:\Space\Products` are user-managed testing copies, not the product's build location.

## Subjective user evidence

The tester reported that Native C++ performed much better and that its latency was **not perceptible** during listening. This is qualitative evidence, not a measured round-trip or capture-to-render time. Preserve the native WASAPI event-driven capture/render architecture that passed their testing.

## Automated evidence from prior checkpoint

- 16 native smoke/unit checks and 13 managed route-model tests passed.
- 30-second muted 192-kHz Line In -> 192-kHz USB Headphones + 48-kHz display audio test: 5,765,760 captured frames, 0 buffer overruns; no new overrun or underrun after the 2-second warmup.
- Dark theme and native event-driven multi-output design retained.

## Important unfinished work

- RNNoise v0.1.1 Standard and portable filter presets are available. Little/Custom models, EQ and voice-changing AI remain future work.
- The native linear resampler has no production-grade anti-alias filter.
- Long-term independent device drift correction, hotplug, sink fault isolation and calibrated end-to-end latency measurement are pending.
- Native mix formats currently require mono/stereo Float32 or PCM16; unsupported formats fail with a clear error.

See [ADR 0003](adr/0003-native-only-audio.md), [Preview Notes](PREVIEW_NOTES.md), and [Test Plan](TEST_PLAN.md).
## RNNoise local preview progress

Native model and MSVC compatibility changes are recorded in [RNNOISE_INTEGRATION.md](RNNOISE_INTEGRATION.md). Muted hardware Bypass/RNNoise switching passed on mixed-rate output devices; subjective quality and measured end-to-end latency are not yet established.

## v0.2.0-preview.3 checkpoint — new route filters / custom chrome

Native C++ ordered filter chain is now integrated and tested: initial empty bypass, repeatable Gain/RNNoise/Gate/Limiter, per-instance bypass, true processing order, and live graph replacement. WindowChrome + grayscale scrollbars/sliders are in the Desktop UI. Windows endpoint names are shown before audio-format metadata in braces and raw IDs remain internal. See [ORDERED_FILTER_CHAIN.md](ORDERED_FILTER_CHAIN.md). Older sections in this document describe previous milestones.
