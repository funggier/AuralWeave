# Verification & Acceptance Plan

**Status:** Design-stage test specification; not executed.

## Functional scenarios

| ID | Test | Expected outcome |
| --- | --- | --- |
| ROUTE-01 | One mic → one speaker, start/stop | Audio flows; stop halts it cleanly |
| ROUTE-02 | One mic → speaker + USB DAC | Both receive audio without accumulating drift |
| OUTPUT-01 | Select one output | It disappears from add dropdown for that route |
| OUTPUT-02 | Select every output | Candidate list becomes empty; `+` disabled |
| OUTPUT-03 | Remove one selected output | Device returns to candidate list |
| OUTPUT-04 | Try selecting duplicate via row edit | Reject duplicate without corrupting routing |
| OUTPUT-05 | Same output in two separate routes | Allowed by model; runtime reports device contention if unsupported |
| FX-01 | Add EQ → Pitch → EQ | Both EQ instances persist independently and process in order |
| FX-02 | Bypass second EQ | First EQ and Pitch still process |
| FX-03 | Reorder effect instances | Config and ID remain bound to each instance |
| FX-04 | Save/load preset with repeated effect type | Exact order/parameters/IDs preserved |
| NOISE-01 | Noise Bypass with enabled EQ | EQ still processes |
| NOISE-02 | RNNoise 48 kHz input | Suppression engages with valid frame adaptation |
| NOISE-03 | Unsupported input rate | Resampling/adapter or clear explicit rejection |
| HOTPLUG-01 | Unplug USB output | Mark disconnected; sibling outputs continue |
| HOTPLUG-02 | Replug identical device | Reconnect safely; preserve settings |
| SAFE-01 | Change chain while playing | No crash/deadlock or invalid concurrent access |
| SAFE-02 | Stop/restart after device error | Route returns to consistent state |
| PORTABLE-01 | Move app folder to another drive | Local settings/presets continue to resolve |
| DRIVER-01 | No optional driver installed | Physical routing still works |

## Non-functional

Test reported vs measured latency, long-session stability, CPU use, buffer underrun/overrun, model unload, cancellation, and unexpected device changes. Define numeric thresholds only after baseline measurements on real devices.

## Release acceptance

- Build and automated checks pass.
- Functional audio tested on Windows 10 real devices.
- Documentation accurately describes shipped features.
- Third-party licenses and attribution verified for pinned dependencies.
- No release while core route start/stop, multi-output, or duplicated effect-instance data retention is broken.

## Observed prototype evidence, 2026-11-24

- Native compiler with /W4 /WX and desktop .NET builds succeeded without warnings or errors.
- 13 managed core checks and 16 native core/resampler tests passed.
- Muted native route: Line In (192 kHz) to USB Headphones (192 kHz) passed, 291,840 captured frames, 0 reported ring overruns.
- Muted mixed-rate native route: Line In (192 kHz) to USB Headphones (192 kHz) plus ASUS VG249 (48 kHz) passed for 2.5 s, 483,840 captured frames, 0 reported ring overruns.
- **Not yet passed:** audible signal quality, anti-aliased resampling fidelity, long-session drift, hotplug, per-sink isolation, Virtual Driver, RNNoise, and actual two-channel subjective listening tests.
