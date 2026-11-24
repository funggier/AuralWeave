# AuralWeave — Windows Portable Preview

This is an experimental engineering build, not a production release.

## Launch

1. Extract the entire archive to a writable folder.
2. Run `AuralWeave.exe`. It is a self-contained Windows x64 desktop app.
3. Choose one input device, then select one or more output devices using the dropdown and **+** button.
4. All newly added outputs start **muted for safety**. You must explicitly unmute an output if you want audible playback.
5. The default engine is Managed WASAPI; use the separate Native C++ WASAPI checkbox only for experimental native testing.
6. Use Start route / Stop route to control capture and routing.

## Capabilities available now

- Capture from enumerated Windows audio inputs and supported loopback endpoints.
- Route a single input to multiple outputs with separate output level/mute.
- Prevent duplicate output selection within a route; options reappear when removed.
- Add multiple independent Gain effects of the same type, change order, enable/bypass or remove them.
- Native backend uses per-endpoint mix formats and bounded linear resampling (prototype).

## Known missing features and safety

- RNNoise and AI voice effects are not implemented.
- Virtual microphone/audio driver is not installed or included.
- This preview has no built-in preset persistence or multi-route manager.
- Mixed-rate output has been tested muted; audible quality and long-running clock drift have NOT been validated. Linear resampling does not provide production-quality anti-aliasing.
- Avoid acoustic feedback (microphone near speakers) and routing a loopback source into its own playback output.
- Some multichannel or unsupported output formats require the Managed backend.
- See THIRD_PARTY_NOTICES.md, LICENSE, and licenses/NAudio-2.2.1-LICENSE.txt for available project/dependency notices. Full redistribution audit is still required before any public release.

No drivers or Windows services are installed by this preview.
