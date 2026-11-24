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

## UI and audio regression scenarios (2026-11-24)

| ID | Scenario | Expected |
| --- | --- | --- |
| DARK-01 | Open all device selectors | Popup and selected item show readable light-on-dark text |
| DARK-02 | Disabled + hovered + selected controls | States remain distinguishable and accessible |
| START-01 | One route: Start then Stop | Same button toggles text/colors and stops actual capture |
| START-02 | Start error/retry | Button remains Start, no zombie engine |
| LAT-01 | Managed, capture/render identical format | Direct provider path without Media Foundation resampler |
| LAT-02 | Managed, format mismatch | Conversion active and queue never grows unbounded |
| LAT-03 | Native, mixed-rate two outputs | Each output serviced by its own render event |
| LAT-04 | Run with input stopped/unplugged | Surface error cleanly; avoid unbounded busy loops |
| LAT-05 | 30-second muted mixed-rate smoke | Report capture frames, startup underrun, post-warmup underrun delta and overflow |

Observed: native 30-second muted smoke yielded **0 post-warmup overruns and 0 post-warmup underruns**, but audible latency and output quality have not yet been tested.
## Native-only consolidation verification (2026-11-24)

- C++20 full native build and 16 native tests passed.
- Managed **data/model validation** 13 tests passed; the managed audio-renderer and NAudio dependency have been removed.
- Native DLL now enumerates capture, loopback and render endpoints and obtains their actual shared-mode sample rate and channel count. Hardware read-only enumeration passed with 6 capture/loopback and 5 render candidates.
- Default Windows capture and render endpoints correctly identified after native enumeration identity fix.
- Real hardware muted single-output: Line In 192k -> Realtek Speakers 192k; 295,680 captured frames, 0 overruns, 1 startup underrun, no reported engine error; Stop succeeded.
- Real hardware muted mixed-rate: Line In 192k -> Speakers 192k + ASUS VG249 48k; 483,840 captured frames, 0 overruns, 2,401 cumulative startup underruns, **zero additional underruns or overruns after warmup**, no engine error; Stop succeeded.
- User reported Native C++ audio felt immediate with no perceptible delay; no calibrated millisecond measurement has been taken.
- Still required: prolonged real-audio testing, different rate/channel configurations, high-quality resampling and host-device hotplug validation.

## v0.2.0-preview.3 end-to-end UI and Native checks

- Desktop Release build: 0 warnings / 0 errors.
- Core data tests: 22 passed, including exact Windows Name-first endpoint labels, loopback metadata, no leaked raw IDs, empty/repeated filter slots and invalid-parameter rejection.
- Native DSP tests: zero-filter exact bypass, repeated Gain and RNNoise instances, per-instance Gate and Limiter, and genuine processing-order differences passed.
- Real muted hardware audio: 192 kHz Line In to two outputs, live ordered filter chain → reordered chain → empty bypass. 833,280 total captured frames, 0 buffer overruns, 2,401 startup underrun frames, no reported engine error. No subjective audio-quality or calibrated latency claim.
- Portable v0.2.0-preview.3 launched and UI Automation verified **actual selected text** `Line In (Realtek(R) Audio) {192 kHz · Stereo}`, `Headphones (Realtek USB2.0 Audio) {192 kHz · Stereo}`, and `Choose filter...` after invoking +.
- Custom window maximize and restore through its themed title-bar button passed in UI Automation.
- ZIP verified to include EXE/native DLL/MIT/RNNoise notice, exclude NAudio and .pdb; local Products copies were not overwritten.

## v0.2.0 release validation

- Native C++/RNNoise and .NET Release builds passed with no warnings/errors.
- Core route/data tests passed: 24 of 24, including fresh enabled Filter slots and unmuted OutputRoute defaults.
- Portable Windows x64 GUI Automation: newly added output Mute checkbox **Off**; new filter dropdown **Choose filter...**; native device labels begin with the Windows endpoint Name.
- EXE file version **0.2.0.0**; AN ribbon PNG 512x512 with transparent corners; Windows ICO has seven embedded sizes.
- Portable ZIP includes EXE, Native DLL, MIT project license, RNNoise BSD license, release notes and third-party notices; no NAudio or PDB.
- Audio quality/feedback, live audible latency, and hotplug recovery are not guaranteed by the muted automated tests.
## v0.2.1: Custom Window maximize/taskbar regression

Reproduction on v0.2.0 (Windows 10, Taskbar at top):
- Monitor 1 usable work area: `(0,30)-(2560,1440)`.
- Maximized window before fix: `(-7,-7)-(2567,1447)`; custom 39px caption bar clipped/hidden by taskbar.
- Maximized window after fix: `(0,30)-(2560,1440)`; minimize, restore and close controls remain visible.

The new `scripts/test-window-workarea.ps1` launches the real WPF application,
moves it between all connected monitors, maximizes/restores, and asserts the
window exactly respects each monitor's Work Area. It checks caption button
bounding rectangles and tests minimize, restoration and close.

- [x] Windows 10 DISPLAY1, primary, top taskbar
- [x] Windows 10 DISPLAY2, secondary, top taskbar
- [x] Windows 10 DISPLAY3, secondary, top taskbar
- [x] All three caption controls visible in maximized state
- [x] Maximize / restore / minimize / close actions succeed

This is a UI-only fix. Native WASAPI code and routing were not modified.
## v0.3.0-preview.1 — mode/preset/virtual driver diagnostics

- Managed Core: 32/32 tests including exact Filter Preset roundtrip, repeated RNNoise, enabled states, excluded audio device identifiers, strict schema checking and local discovery.
- Native device-discovery diagnostics: SetupAPI enumerated zero owned devices on the test machine; reserved INPUT/OUTPUT hardware IDs were recognized; unrelated hardware IDs rejected.
- Real Windows WPF UI automation: switch Studio → Virtual Driver → Studio; verify both Virtual Input and Virtual Output cards; verify PnP discovery ran successfully.
- Layout gaps: mode selector and Filter Preset button groups have expanded spacing; Virtual Input/Output installation status margins increased.
- Actual installation and audio transport require a future signed driver package; buttons remain disabled until then.

## Windows 10/11 virtual audio kernel prototype QA

- Compiler: Visual Studio 2026 / WDK 10.0.28000.0 x64.
- `scripts/build-virtual-driver-prototype.ps1` passed: .SYS built;
  `InfVerif /w` and `InfVerif /u` both passed; Inf2Cat
  `/nocat` signability passed with no errors or warnings.
- INF targets Windows 10 build 19041+ and Windows 11; candidate
  device IDs are 0001-0016 for each of Input and Output.
- Managed PnP ownership checks reject unrelated/bare/malformed
  hardware IDs; available classes and service are checked.
- Current Windows 10 development host: 0 owned devices installed.
- NOT YET TESTED: Windows 11 runtime, Windows 10 installed driver,
  concurrent devices, PCM transport, HLK, Microsoft signing, or
  portable-folder relocation of a real installed driver.
