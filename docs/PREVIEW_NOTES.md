# AuralWeave — Native-only Windows x64 Preview

This is an experimental preview, not a final product release.

## What's changed

- **Native C++ WASAPI is the only audio backend.** Managed WASAPI/NAudio and the engine selection checkbox were removed.
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

- RNNoise, EQ and AI effects, presets and optional virtual audio drivers are not implemented.
- Native engine supports mono/stereo Float32/PCM16 mix formats; unsupported device configurations report errors instead of falling back.
- A subjective user test found Native latency imperceptible, but no measured end-to-end millisecond figure is available.
- The native linear resampler is not a production-quality anti-aliased resampler. Longer-session drift, hotplug, sink isolation and audio fidelity still need formal testing.
- No audio driver is installed by this preview.
