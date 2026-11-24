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

## v0.2.0-preview.3 checkpoint — new route filters / custom chrome

Native C++ ordered filter chain is now integrated and tested: initial empty bypass, repeatable Gain/RNNoise/Gate/Limiter, per-instance bypass, true processing order, and live graph replacement. WindowChrome + grayscale scrollbars/sliders are in the Desktop UI. Windows endpoint names are shown before audio-format metadata in braces and raw IDs remain internal. See [ORDERED_FILTER_CHAIN.md](ORDERED_FILTER_CHAIN.md). Older sections in this document describe previous milestones.

## Local v0.3.0-preview.1 checkpoint

- UI: Studio / Virtual Driver selector; a single Virtual Driver page with separate Virtual Input (recording) and Virtual Output (playback) cards; increased margins for mode selector, preset controls and driver action areas.
- Presets: Save/Load/Import/Export current ordered Filters as portable .awfilter JSON files, without Sound Input or Output device selection.
- Driver manager: Win32 SetupAPI enumerates reserved AuralWeave-family PnP hardware IDs independent of the executable folder; guarded per-instance removal with explicit confirmation and UAC is available for future matching devices.
- Not yet implemented: first-party signed virtual audio kernel driver, installation or creation of virtual endpoints, IPC audio bridge. Install controls remain intentionally disabled.
- Validation: 32 Core tests, real Windows two-mode UI test, native PnP scan (zero matching installed), Portable ZIP and custom WindowChrome maximize/restore across three displays passed.

## Driver development toolchain (2026-11-25)

WDK 28000, SDK 28000, VS 2026 WDK integration and pinned SysVAD
reference/WIL are installed. Driver toolchain checker passes.
Full upstream reference source compiles in compile-only mode.
No actual AuralWeave virtual audio driver binary has been created,
signed, validated, installed or released yet.

## Kernel prototype compiled — Windows 10 + Windows 11 target (2026-11-25)

A new AuralWeave-branded unsigned WDM/PortCls virtual audio
prototype now compiles into `AuralWeaveVirtualAudio.sys`. It
selects capture or render KS interfaces according to independently
enumerated AuralWeave root hardware IDs. Maximum reserved ID bank:
16 input + 16 output. Windows 10 build 19041+ / Windows 11 x64 INF
model selection; InfVerif /w /u and Inf2Cat /nocat signability pass.
Device discovery is hardened to the MEDIA class, AuralWeave service
and exact ID bank. No live PCM driver transport, Windows 11 runtime
tests, HLK, signing or install yet. The driver binary is excluded
from portable app publication.

## Native user-mode PCM bridge (2026-11-25)

C++ SPSC Shared Memory transport is implemented with per-role/slot
isolation, bounded PCM, random session name, Float32 mono/stereo,
cross-process tests, C ABI and managed wrappers. Studio now supports
a developer-only post-filter tap for a future virtual microphone;
physical outputs remain unchanged. Real WASAPI 192-kHz capture
resampled to a 48-kHz Shared PCM tap and was consumed without
overrun in a 1.2-second test. The unsigned kernel prototype is
still NOT attached to this transport, and virtual output input
selection, Windows 11 VM tests and Microsoft signing remain pending.
