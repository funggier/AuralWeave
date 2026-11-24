# AuralWeave — Native-only Windows x64 Preview (RNNoise development build)

This is an experimental preview, not a final product release.

## What's changed

- **Native C++ WASAPI is the only audio backend.** The legacy managed backend is removed.
- Device discovery, endpoint mix formats, physical capture/render and loopback are handled by the native DLL.
- The existing dark theme and single Start/Stop button remain.
- One input can route to several independently buffered output devices; newly added outputs start muted.
- You may add more than one Gain effect; each instance retains independent gain, enabled state and order.

## Usage

1. Run `AuralWeave.exe` with `AuralWeave.Native.dll` in the same folder.
2. Choose an input (Microphone, Line In or playback loopback) and add one or more output devices via the `+` button.
3. Newly added outputs start muted. Use headphones and low gain when testing to prevent acoustic feedback.
4. Click **Start route**. The same button becomes **Stop route**.
5. To change selected endpoints, stop the route and adjust the dropdowns.
6. Portable builds are generated in the project at `dist/AuralWeave-win-x64` and may be copied manually to a test folder.

## Known limitations

- **RNNoise Standard v0.1.1 is now optional in the noise dropdown.** Little and Custom models are not yet available. EQ and AI effects, presets and optional virtual audio drivers are not implemented.
- Native engine supports mono/stereo Float32/PCM16 mix formats; unsupported device configurations report errors instead of falling back.
- A subjective user test found Native latency imperceptible, but no measured end-to-end millisecond figure is available.
- The native linear resampler is not a production-quality anti-aliased resampler. Longer-session drift, hotplug, sink isolation and audio fidelity still need formal testing.
- No audio driver is installed by this preview.

## RNNoise notes

The default is Bypass, which preserves the native low-latency audio path. Selecting RNNoise introduces 48-kHz frame processing and resampling when required. Change modes during playback to compare quality, but do not assume RNNoise latency matches Bypass.

## Monochrome skin / icon preview (v0.2.0-preview.2)

- Transparent monochrome AuralWeave icon now embedded into EXE and window; no installation is required.
- Switch between Monochrome Dark and Graphite skins in the program header. Selection is stored locally at `settings/appearance.json`.
- UI has Sound Input → Filters → Sound Output stages and collapse controls for filter settings. Actual DSP order is still RNNoise-before-Gain, with repeated Gain instances supported. Arbitrary RNNoise/Gate/Limiter filter routing is not yet implemented.
- Skins are built-in WPF resource dictionaries. Unsafe third-party XAML themes are not loaded.

## Custom Window + ordered filters (v0.2.0-preview.3)

**This section describes the current preview and supersedes historical notes above about a separate RNNoise section / Gain-only chain.** The app has themed custom Windows title bar controls, styled scrollbars/sliders and START/STOP. The Filters area starts empty; + adds a Choose filter... slot. All currently supported filters (Gain, RNNoise Standard, Gate and Limiter) can repeat, be bypassed, and be reordered in the real native processing graph. Input/output labels start with the exact Windows endpoint Name followed by format details in {braces}. See [ordered filter documentation](ORDERED_FILTER_CHAIN.md) and [v0.2.0-preview.3 notes](releases/v0.2.0-preview.3.md).

## v0.2.0 release behavior

The supported packaged application now uses the white-gray AN ribbon icon
with transparent surroundings. Filter slots start **enabled** (but the blank
Choose filter... type is inert). Added output destinations now start
**unmuted**; use headphones or low hardware volume to avoid microphone
feedback. See `docs/releases/v0.2.0.md` for the release's feature set and
limitations. Older notes above describe historical previews.