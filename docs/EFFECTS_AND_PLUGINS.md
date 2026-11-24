# Noise Reduction, Effects Chain & Plugins

## Separation of concerns

```text
Audio source → Noise slot [Bypass | RNNoise | future]
             → Effects [FX#1 → FX#2 → ... → FX#N]
             → Output branches
```

The noise-reduction dropdown contains **one active engine per route**; the effects list contains **zero or more ordered instances**, including multiple instances of the same type.

## Effect instance rules

- Each entry gets an immutable `instanceId`, unique within its route.
- Selecting the same `effectType` twice creates two **separate stateful processors**.
- Each processor has independent parameters, on/off state, smoothing state and latency.
- Order changes apply to instances, not effect types.
- Delete an instance without deleting its type/module from the installation.
- Save/load all instance IDs, their order, parameters and bypass states.
- An unavailable plugin remains represented in a preset as Missing/Disabled rather than being silently deleted.

Example: `EQ(A) → Pitch Shift(B) → EQ(C) → EQ(D)` is valid. EQ(A), EQ(C), EQ(D) must never share mutable parameter state merely because the type matches.

## Potential processors

- Noise: Bypass, RNNoise (initial); future WebRTC NS / other engines
- Effects: EQ, pitch/formant, compressor, limiter, gate, voice changer
- Advanced future effects: AI voice conversion and enhancement (model-dependent)

These are product design candidates. This project initialization does **not** provide implementations of those effects.

## Runtime contract

`prepare(sampleRate, channels, maxFrames)`, `process(buffer, frames)`, `setParameters(snapshot)`, `reset()`, `latencyFrames()`, `dispose()` are draft conceptual operations. Specify and stabilize actual plugin ABI after a minimal core prototype.

## RNNoise integration considerations

RNNoise normally processes 48 kHz PCM in fixed 480-sample frames. Any stream with another rate/frame configuration requires a safe adapter, buffering and timing accounting. Test whether suppression artifacts are acceptable for each content type; bypass should remain immediately available. Verify exact pinned RNNoise source version, notices and licensing before vendoring.

## Safeguards

No disk/network/model initialization in real-time callback. CPU-heavy processors should expose resources, latency, readiness, loading/failure statuses. If plugin fails, isolate and report failure; define whether route skips the effect or suspends according to configured policy.
