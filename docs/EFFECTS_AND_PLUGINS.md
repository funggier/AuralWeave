# Noise Reduction, Effects Chain & Plugins

## Separation of concerns

```text
Audio source → Noise slot [Bypass | RNNoise | future]
             → Effects [FX#1 → FX#2 → ... → FX#N]
             → Output branches
```

The diagram above describes the original proposal. In the current Studio, RNNoise is an ordinary, repeatable filter in the [ordered native chain](ORDERED_FILTER_CHAIN.md).

## Effect instance rules

- Each entry gets an immutable `instanceId`, unique within its route.
- Selecting the same `effectType` twice creates two **separate stateful processors**.
- Each processor has independent parameters, on/off state, smoothing state and latency.
- Order changes apply to instances, not effect types.
- Delete an instance without deleting its type/module from the installation.
- Presets save filter order, parameters and bypass states. Loading generates new instance IDs.

Example: `EQ(A) → Compressor(B) → EQ(C) → EQ(D)` is valid. EQ(A), EQ(C), EQ(D) must never share mutable parameter state merely because the type matches.

## Voice processing

- **Pitch Shift** raises or lowers the incoming voice by a fixed interval without changing its duration. The v0.4.0 implementation was retired in v0.4.1.
- **Formant shifting** moves resonances associated with vocal timbre. Separate formant controls are one option for a future voice changer.
- **Pitch correction** detects the note and moves it towards a target note in a chosen key or scale. AutoTune is one example. [AutoTune documentation](https://help.antarestech.com/hc/en-us/articles/42855736822932-AutoTune-FAQ)
- **AI voice conversion** uses a model to change vocal timbre, for example RVC. [RVC project](https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI)

## Runtime contract

`prepare(sampleRate, channels, maxFrames)`, `process(buffer, frames)`, `setParameters(snapshot)`, `reset()`, `latencyFrames()`, `dispose()` are draft conceptual operations. Specify and stabilize actual plugin ABI after a minimal core prototype.

## RNNoise integration considerations

RNNoise normally processes 48 kHz PCM in fixed 480-sample frames. Any stream with another rate/frame configuration requires a safe adapter, buffering and timing accounting. Test whether suppression artifacts are acceptable for each content type; bypass should remain immediately available. Verify exact pinned RNNoise source version, notices and licensing before vendoring.

## Safeguards

No disk/network/model initialization in real-time callback. CPU-heavy processors should expose resources, latency, readiness, loading/failure statuses. If plugin fails, isolate and report failure; define whether route skips the effect or suspends according to configured policy.
