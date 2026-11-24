# AuralWeave — Dark UI & Audio Stability Preview

This is an experimental engineering build, not a stable public release.

## Changes in this Preview

- Dark dropdowns, dropdown options, buttons and disabled states; no white-on-white device names.
- Start and Stop combined into a single button. Click Start to begin, then the same button says Stop.
- Default backend is **Native C++ WASAPI** if `AuralWeave.Native.dll` is available.
- Uncheck **Native C++ engine** to use Managed WASAPI compatibility mode.
- Native uses independent render endpoint events; shorter buffering and separate overrun/underrun telemetry.
- Managed fallback lowers requested buffer/latency and avoids converting already-matched PCM.
- Output selections use a dropdown and +/−, and already-selected endpoints are excluded until removed.
- Duplicate Gain effects can still be inserted and configured independently.

## Launch

1. **Close old AuralWeave.exe before switching to the new preview.** The previous test installation is not automatically overwritten or terminated.
2. Extract the ZIP into a new, writable folder. Keep `AuralWeave.exe` and `AuralWeave.Native.dll` together.
3. Run `AuralWeave.exe`.
4. Select Line In or microphone from Input source.
5. Add an output from the Output dropdown with `+`. Newly added outputs start **Muted**; explicitly unmute to hear.
6. Click Start route. The button becomes Stop route while running.
7. If the native backend rejects an unusual device configuration, stop and uncheck Native C++ to retry with Managed WASAPI.

## Cautions / limitations

- Routing to speakers while capturing a microphone in the same room can create feedback. Start with low gain and headphones.
- **RNNoise, EQ, AI Voice Changer, Virtual Audio Driver and preset persistence are not included yet.**
- Muted real-device smoke tests cannot prove audible quality. Sound quality, glitches on other devices, long-term clock drift and exact end-to-end latency remain to be measured.
- Native resampling uses linear interpolation, not an anti-aliased production resampler.
- Windows audio endpoint settings can affect total delay; a setting of 40 ms does not mean measured 40 ms latency.
- No driver or Windows service is installed. See included THIRD_PARTY_NOTICES.md and licenses for third-party rights.
