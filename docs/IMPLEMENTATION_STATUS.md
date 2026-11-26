# AuralWeave implementation status

## Studio v0.6.2 — Editor theme and Studio display

- Both VST formats share a native editor frame that reads its colors from the Studio palette. An open editor follows a skin change immediately. The plugin still receives its requested viewport dimensions.
- The Add Filter button uses an 18-DIP centered plus and is 60 DIPs high. The logo header shows **NATIVE AUDIO ROUTING** at 16 DIPs.
- The footer calculates capture frames per second from the native frame counter and elapsed time. It updates about once a second and shows zero after stopping.
- Windows 10 22H2 x64 passed the native smoke suite, 44 managed tests, and a Portable UI run with Crystalline VST2 and VST3. Pixel checks confirmed the centered plus and both caption colors. Both editors kept their 1016×398 viewport and passed close, reopen, and reload. A muted live route reported about 192,000 frames/s before and after changing effects.

## Studio v0.6.1 — VST discovery and reload

- Custom Location scans the specified folder tree. Common Files automatically scans the shared and per-user Windows VST locations listed in the [VST guide](VST_PLUGINS.md). Re-scan refreshes the choices in the card.
- Reload Plugin creates a fresh native instance of the chosen file. The active filter graph is updated before the previous instance is disposed. Changing the discovery source keeps the current effect and audio route active.
- Preset schema 6 stores the Common Files choice even for an empty card. Schemas 4 and 5 restore as Custom Location. Both kinds of card use the same native stage and existing VST2/VST3 host paths.
- Windows 10 22H2 x64 passed the native smoke tests, 44 managed checks, and a Portable UI test with Crystalline VST2 and VST3. The UI test verified source isolation, Graillon format discovery, both same-file reloads, format replacement, Re-scan, preset restoration, and continued capture on a muted WASAPI route. A selected file removed after scanning was rejected and the original effect stayed active.

## Studio v0.6.0 — VST formats, themed path, and audio buffering

- The VST folder box and other text fields use the shared WPF palette in Monochrome Dark and Graphite. Scanning refreshes the corresponding VST card.
- The plugin browser discovers x64 VST2 DLLs and x64 VST3 modules in the selected folder and the standard Windows VST3 directory. It displays both formats when installed and keeps the exact selected module in the preset. VST3 uses the pinned MIT interface headers and supports mono/stereo main buses.
- Matching rate and channel count uses direct PCM buffering. Ring reads copy at most two contiguous spans, and the Noise Gate and Limiter coefficients are computed when building the filter graph.
- On the Windows 10 test PC, the median synthetic stereo PCM ring test at 192 kHz was 11.873 → 2.809 microseconds per 10 ms packet for matching rates and 2.671 → 1.519 microseconds for 192-to-48 kHz. These measurements cover ring processing, not the entire audio path.
- The Portable UI test selected both Crystalline formats from the same scan, switched to VST3 on a muted live route, verified both native editors, and restored a preset using the exact VST3 module. Both WPF palettes were checked for field and text colors.
- Native tests detected comparable Crystalline reverb tails through VST2 and VST3 at 48 and 192 kHz. Separate 192 kHz Windows WASAPI tests captured 768,000 frames per format after warmup without additional overrun or underrun frames. The 15-second mixed-output soak had zero additional overruns or underruns after warmup.

## Studio v0.5.2 — Slider wheel hit areas

- Transparent template borders extend wheel input 5 DIPs above and below the existing slider box without adding layout space.
- Windows WPF hit tests compared rendered pixels before and after, verified scrolling outside and over disabled controls, and checked a gap between adjacent targets in seven real EQ rows. Wheel input at an extended point changed only the intended band.
- The tested Portable build uses a separate versioned folder.

## Studio v0.5.1 — VST preset locations and fine slider control

- Preset schema 5 stores the selected VST2 file path and SHA-256 identity. Relative paths within the portable folder move with the app. DLLs previously loaded in that portable copy auto-restore when the identity matches. Missing files become empty VST cards. Schema 4 VST slots remain readable.
- New VST folder inputs start empty and show a chooser hint. Older global folder settings are ignored.
- Mouse-wheel steps apply to the hovered enabled filter or output slider. EQ changes by 0.1 dB and output gain by 0.01x per notch.
- Windows 10 x64 Release build and 42 Core tests passed. The extracted Portable ZIP saved the actual Crystalline 1.8 location, restored it after restart, and loaded it during a muted WASAPI route. A changed fingerprint required the existing Load plugin action. A missing-file fixture kept the VST card empty with its neighboring Gain settings intact. WPF event checks verified fine steps, high-resolution wheel accumulation, limits and unchanged controls outside the target.

## Studio v0.5.0 — VST Plugin card

- Each VST Plugin card loads an explicitly selected x64 VST2 effect. The folder scan checks PE exports without executing candidates. The last scanned directory is saved beside the executable.
- The WPF UI owns the plugin and its native editor. Ordered graph snapshots reference the same loaded instance across filter edits, reorders and bypass. The UI prepares the plugin at the input sample rate before the WASAPI worker begins.
- Windows 10 22H2 x64 build and all 39 Core tests passed. Crystalline 1.8 produced a reverb tail from synthetic PCM at 48 and 192 kHz. The 192 kHz muted route captured 768,000 frames after warmup with zero additional overrun or underrun frames. Live bypass and stop passed.
- An extracted Portable ZIP found Crystalline through the folder UI, loaded it, opened and closed its native editor twice and kept a muted route running while the editor was open. The archive contains the host and runtime, with third-party effect DLLs supplied by the user.
- Filter Presets schema 4 retains VST card order and enabled state. On restore, select and load the plugin explicitly. See [VST Plugin cards](VST_PLUGINS.md) for formats and preset details.

## Studio v0.4.1 — EQ (3 Band), EQ (5 Band), EQ (7 Band)

- The three EQ layouts share the same native biquad implementation. All bands start flat and have independent ±12 dB controls.
- Pitch Shift and its dependencies were removed. Loading a v0.4.0 preset skips only that stage and keeps the remaining filters and their settings. Presets with five- or seven-band EQ use schema 3. The three-band EQ retains its schema 2 format.
- Windows 10 22H2 x64 Release build and all 38 Core tests passed. Native tests checked every new band at 48 and 192 kHz, flat bypass and bypass above the input's Nyquist frequency.
- UI Automation checked all three dropdown options and 15 sliders in the extracted Portable ZIP, saved a mixed EQ preset, and restored the three-band settings from a legacy preset containing Pitch Shift.
- Muted WASAPI routing from 192 kHz Line In to two outputs ran all three EQ types for 15 seconds after warmup with zero additional overrun or underrun frames. Live reorder, bypass and stop passed.

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
