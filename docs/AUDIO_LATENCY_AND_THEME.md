# Audio stability and dark UI — 2026-11-24

## User reports addressed

A tester ran `T:\Space\Products\AuralWeave\AuralWeave-Windows-x64-Preview` and reported unreadable white text over light dropdown/button backgrounds, broken-up audio, very high delay, and separate Start/Stop buttons.

These changes address identified implementation causes and provide new diagnostics; they **do not establish an independently measured end-to-end latency or guarantee that all audible gaps are gone**.

## Theme / controls

- AuralWeave dark theme applies to the closed ComboBox, expanded dropdown, individual item rows, selected/hover rows, button surfaces, and disabled states.
- White text is used against dark navy input surfaces, and the Start/Stop button has readable foreground and contrasting active colors.
- One Start/Stop toggle per route replaces the two separate buttons.
- New outputs remain muted by default as a feedback precaution.

## Managed WASAPI compatibility backend

Previous:
- Up to 450 ms of buffered audio per sink
- 90 ms WasapiOut requested latency
- Always instantiated MediaFoundationResampler, including for equal input/output PCM layouts
- Buffered UI reported **capacity** rather than actual queued duration
- Overflow would discard incoming, fresh audio rather than stale queued samples

Changes:
- BufferedWaveProvider max reduced to 120 ms; requested WasapiOut latency lowered to 35 ms (requested values are **not measured end-to-end latency**).
- Identical PCM format uses a direct shared-mode sample path, skipping Media Foundation conversion.
- Different PCM formats still use MediaFoundationResampler.
- On capacity pressure, discard oldest queued audio first; count trimmed frames.
- UI reports **actual current buffered bytes converted to milliseconds**, not the configured capacity.

## Native WASAPI backend

Previous:
- Render packets were sent **only when capture events arrived**.
- A capture gap could starve outputs; varying hardware device clocks were not serviced individually.
- 100 ms requested WASAPI buffers and 250 ms bounded resampler queues.

Changes:
- Independent event-driven render callback for EACH selected output endpoint, using its own IAudioClient event handle.
- Capture and all render events are serviced in a single COM-initialized worker thread, with a stop event.
- Requested shared-mode client buffering reduced to 40 ms; per-output converter queue bounds reduced to 80 ms.
- Render output uses at most one approximately 10 ms packet per output event. No intentional backlog generation.
- Overrun and underrun telemetry are tracked separately and made available to the Desktop UI.
- The native backend is selected by default when its DLL is present, but can be turned off using the Native C++ checkbox; managed WASAPI remains available.

## Hardware evidence

- C++20 /W4 /WX native build passed; .NET Release build passed.
- 192 kHz Line In → 192 kHz USB Headphones plus 48 kHz display audio was rechecked, both destinations muted.
- 10-second muted mixed-rate run: 1,925,760 capture frames, 0 ring overruns, 2,401 total ring underrun frames. The overrun/underrun delta **after the initial 2-second settling interval was zero**.
- 30-second muted mixed-rate run: 5,765,760 captured frames; 0 ring overruns; 2,401 total startup underrun frames; **0 overrun delta and 0 underrun delta after the 2-second settling interval**. Exact audible latency is still unmeasured.

## Next engineering steps

- Measure actual capture-to-output delay using loopback/correlation and quantify underruns after startup.
- Add higher-quality anti-alias filtering before sample-rate reduction; current native converter is linear interpolation.
- Improve per-device clock drift compensation, sink isolation, hotplug and large-scale multi-output testing.
- Add settings for buffer/latency modes once safe ranges for supported devices are validated.
- RNNoise and other effects are future modules.
