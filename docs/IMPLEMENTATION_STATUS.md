# AuralWeave implementation status

## Studio v0.4.0 — EQ, Compressor and Pitch Shift

- The EXE icon uses the owner's 256 × 256 ICO. The two Studio logo images use the same artwork, decoded to PNG and scaled by WPF.
- Native Filters include a 3-band EQ, a stereo-linked Compressor and Pitch Shift with formant compensation. Preset schema 2 stores the new settings and reads existing schema 1 files. The original scalar native API is retained alongside the extended API.
- Windows 10 22H2 x64 Release build and all 34 Core tests passed. Native tests verified EQ gain by band, compressor gain and attack/release, and ±12-semitone tone transposition at 48 and 192 kHz across uneven packet sizes.
- UI Automation launched the Portable ZIP from an extracted test folder, selected all three filter kinds, adjusted all nine sliders, and saved a portable preset with the new values.
- The muted 192 kHz Line In route to 192 kHz headphones and 48 kHz display audio showed zero new overruns or underruns in 15 seconds after warmup. Live graph replacement, pitch direction change, reorder, bypass and stop passed. A separate muted run used both VB-CABLE endpoints with the Portable DLL and all three effects for 10 seconds after warmup, with zero new overrun or underrun frames.
- Audible voice quality and end-to-end latency still require listening and measurement. See [filter details](ORDERED_FILTER_CHAIN.md) and the [Windows release notes](releases/v0.4.0.md).

## Portable Studio v0.3.1

Windows 10 22H2 x64 is the primary test platform. The app routes through the Windows endpoints selected in Studio, including independently installed VB-CABLE devices. See the [VB-CABLE guide](VB_CABLE.md) for application-to-application routing.

The Windows 10 build passed all 32 Core tests. UI Automation verified the single Studio screen. The installed VB-CABLE playback and recording endpoints both streamed in muted WASAPI routes. Mixed-rate physical outputs and live filter reordering passed separately.

## Lower-latency WASAPI checkpoint

Native WASAPI requests a shared-mode engine period of about 5 ms on endpoints that support it, rounded to a period allowed by the driver. If the endpoint cannot use the shorter period, the shared-mode route uses the device's default period and Windows-selected buffer capacity. WASAPI loopback uses the default shared-mode path. Each render endpoint writes at most one period per event.

On Windows 10 22H2 x64 the VB-CABLE playback endpoint negotiated 5 ms (11 ms buffer capacity), down from a 10 ms period (40 ms capacity) on v0.3.1. VB-CABLE capture and Realtek Line In stayed at 10 ms. Cable endpoints used 192 kHz Windows mix formats with VB-CABLE's internal setting at 48 kHz. The published and updated libraries both ran concurrent muted VB-CABLE send and receive routes. The updated library showed zero additional underruns or overruns in 30 seconds after a 2-second warmup across mixed 192 kHz and 48 kHz outputs. A separate 20-second muted run used two simultaneous senders on the same `CABLE Input` and a receiver on `CABLE Output`. The playback engine held at 5 ms with zero additional underruns or overruns after warmup. The end-to-end latency through VB-CABLE and Discord still needs a signal measurement.

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

- RNNoise v0.1.1 Standard and portable filter presets are available. Little/Custom models, correction to a musical key, and AI voice conversion remain future work.
- The native linear resampler has no production-grade anti-alias filter.
- Long-term independent device drift correction, hotplug, sink fault isolation and calibrated end-to-end latency measurement are pending.
- Native mix formats currently require mono/stereo Float32 or PCM16; unsupported formats fail with a clear error.

See [ADR 0003](adr/0003-native-only-audio.md), [Preview Notes](PREVIEW_NOTES.md), and [Test Plan](TEST_PLAN.md).
## RNNoise local preview progress

Native model and MSVC compatibility changes are recorded in [RNNOISE_INTEGRATION.md](RNNOISE_INTEGRATION.md). Muted hardware Bypass/RNNoise switching passed on mixed-rate output devices; subjective quality and measured end-to-end latency are not yet established.

## v0.2.0-preview.3 checkpoint — new route filters / custom chrome

Native C++ ordered filter chain is now integrated and tested: initial empty bypass, repeatable Gain/RNNoise/Gate/Limiter, per-instance bypass, true processing order, and live graph replacement. WindowChrome + grayscale scrollbars/sliders are in the Desktop UI. Windows endpoint names are shown before audio-format metadata in braces and raw IDs remain internal. See [ORDERED_FILTER_CHAIN.md](ORDERED_FILTER_CHAIN.md). Older sections in this document describe previous milestones.
