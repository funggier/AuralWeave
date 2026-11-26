# Ordered Filter Chain — Native C++ architecture

## User contract

```text
Sound Input -> Filter 01 -> Filter 02 -> ... -> Filter N -> Multi-Output Distributor
```

- The chain starts completely empty. With no selected filters, audio is passed from input to outputs without denoiser DSP.
- Press + to add a slot. It starts as `Choose filter...` (unselected, inert).
- Any available filter kind may repeat, each with a stable UI instance ID and independent settings/state.
- The ordered native graph processes enabled stages exactly top to bottom. Disabled stages and unselected slots do not change the audio signal.
- Moving a filter changes processing order, not just visual order; status of enabled stages is preserved in UI.
- The global Collapse all / Expand all is a presentation-only control; per-card expand/collapse has no DSP effect.

## Current filter types

| Type | Parameter and default | Processing |
| --- | --- | --- |
| Gain | gain 1.00x (range 0.00x to 2.00x) | linear gain with clipping to [-1, 1] |
| RNNoise Standard | fixed model | Xiph v0.1.1 at 48 kHz, 480-sample frames, per-stage state |
| Noise Gate | threshold -45 dB (range -80 to 0 dB) | basic envelope gate; 5 ms attack, 80 ms release |
| Limiter | ceiling -1 dBFS (range -24 to 0 dBFS) | zero-lookahead peak ceiling, 100 ms recovery |
| EQ | 160 Hz low shelf, 1 kHz mid bell, 3.5 kHz high shelf. Each starts at 0 dB and adjusts from −12 to +12 dB | Three biquad bands with separate state per channel |
| Compressor | −18 dBFS threshold, 3:1 ratio, 10 ms attack, 100 ms release, 0 dB makeup gain | Stereo-linked peak detector with attack and release smoothing |
| Pitch Shift | 0 semitones by default, adjustable from −12 to +12 in 0.1-semitone steps | Transposes without changing duration, using Signalsmith Stretch with formant compensation |

Flat EQ and Pitch Shift at 0 semitones are exact audio bypasses. EQ and Compressor process each incoming sample without lookahead. An active Pitch Shift uses a 32 ms analysis window and an 8 ms hop, so it adds buffering to live monitoring. Increase or decrease pitch with the semitone control. Pitch correction to a song's key and AI-based timbre conversion are separate features.

## Processing architecture

- WPF manages the list of `FilterStage` records with UUIDs and presents controls.
- `AuralWeave.Audio.Windows` validates the list and sends ordered kinds/parameters/enabled flags through the versioned C ABI function `aw_set_filter_chain_v2`. The original scalar entry point is retained for existing native callers.
- The Native C++ route prepares the new `FilterChain` outside the packet-processing callback, then atomically publishes a shared graph pointer. Capture packets process the selected graph in order.
- Audio frames always pass through Native WASAPI, with output fan-out to selected device endpoints.
- For RNNoise frames and device mix-rate conversion the native code continues using its bounded resampling adapters.
- The C API retains legacy noise/effects entry points only for compatibility tests; the current Desktop app uses the ordered filter API.

## Risks and next steps

- Live graph replacement currently resets recurrent/envelope state instead of migrating it, so fast filter edits may produce a short audible discontinuity. Smooth crossfade / stable-state migration is future work.
- High-quality anti-aliasing/resampling, long-duration hardware-clock drift correction, real-time CPU metrics, device hot-plug recovery and formal audio latency measurements are incomplete.
- The UI/theme has no effect on the real-time audio thread; minimize UI refresh overhead.
- The maximum chain size in this preview is 64 slots.
