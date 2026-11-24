# RNNoise integration — native-only experimental processor

## Source and license

- Vendor: Xiph.Org
- Pinned version: v0.1.1 (commit `6cbfd53eb348a8d394e0757b4025c6ded34eb2b6`)
- Original code BSD-3-Clause; verbatim `third_party/rnnoise/COPYING`
- Embedded weights: `third_party/rnnoise/src/rnn_data.c`
- Portability patch: `_alloca` replaces original C99 VLAs in two upstream files for MSVC.
- The source and model compile offline from the pinned files.

## Pipeline

```text
Capture (native WASAPI, device mix format)
  -> bypass selection
       Bypass: original float PCM -----------------------------+
       RNNoise: resample to 48k, process 480-sample frames,   |
                resample to device rate ----------------------+
  -> ordered Gain effects
  -> multiple independent output converters and render events
```

The RNNoise Standard mode is built-in. Bypass does not invoke any denoiser or
its resamplers. Model choice is separated in the UI for future extension.
An active route can switch between Bypass and RNNoise. On re-enabling RNNoise,
the model's recurrent state and frame adapter are reset to avoid stale audio.

RNNoise frame size is 480 samples at 48 kHz, nominally 10 ms of audio.
The signal path necessarily accumulates a frame; no absolute 10-ms
end-to-end latency claim is made.

## Current limitations and follow-up

1. The linear input/downsample and output/upsample conversion is a prototype;
   use a quality anti-aliased converter for robust 192 kHz support.
2. CPU utilization and subjective voice distortion need human listening tests.
3. Add per-route telemetry of RNNoise processing time and actual signal delay.
4. Model formats differ by RNNoise version. Do not label arbitrary weights
   'Little' or allow incompatible models until their signatures are verified.
5. The existing native Windows device output fan-out and clock drift
   handling remain unchanged.

## Verification matrix (2026-11-24)

- 32 Native C++ checks passed (16 pre-existing checks plus 16 RNNoise checks across 44.1/48/96/192 kHz mono and stereo), plus 13 managed routing/configuration checks.
- Real hardware: Line In at 192 kHz to two muted output endpoints; live Bypass -> RNNoise (10 seconds) -> Bypass succeeded with zero reported overruns and no reported engine errors.
- The muted test does not validate subjective noise-reduction quality or calibrated end-to-end latency.
