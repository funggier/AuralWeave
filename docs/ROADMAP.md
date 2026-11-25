# Roadmap

The repository now includes working prototypes; boxes below distinguish fully proven milestones from remaining hardening. See [Implementation status](IMPLEMENTATION_STATUS.md) for test evidence.

## Phase 0 — Repository foundation

- [x] Establish product name AuralWeave
- [x] Project directories
- [x] Requirements, architecture, UI, routing, effects, driver, testing docs
- [x] MIT license and third-party notice policy
- [ ] Configure CI after implementation build/test tooling is selected

## Phase 1 — Minimal functional audio route

- [x] Native C++/WASAPI capture and render proof of concept (muted hardware smoke)
- [x] Device enumeration and stable endpoint identity (prototype on Windows)
- [x] Start/Stop one .NET/WASAPI route with one input and one muted physical output (smoke test)
- [x] Basic WPF UI and input meter/status (prototype)
- [ ] Media Foundation format adaptation and bounded buffering prototyped; full conversion matrix and stability not yet verified
- [ ] Recovery from stop/disconnect, measured performance

## Phase 2 — Multiple simultaneous outputs

- [x] Add/remove output controls with per-route uniqueness (prototype)
- [ ] Prototype fan-out and per-output gain/mute implemented; real multi-output hardware smoke and drift handling remain
- [ ] Per-device clock drift/underrun handling
- [ ] Hotplug and per-output status
- [ ] Regression tests for every dropdown edge case

## Phase 3 — Noise and effects

- [ ] Noise processing selector: Bypass/RNNoise
- [ ] Frame adapter and RNNoise quality tests
- [x] Ordered Gain effect instances, repeated types and independent gain settings (prototype)
- [ ] Enable/bypass/reorder/remove implemented for Gain; concurrent hot reconfiguration safety and DSP engine plugin ABI require hardening
- [ ] Actual EQ as first configurable built-in effect
- [ ] Preset save/load round-trips

## Phase 4 — Multi-route polish

- [ ] Concurrent independent routes
- [ ] Global and per-route controls
- [ ] Portable release structure, build/release tooling
- [ ] Robust diagnostics, accessibility/localization, integration tests

## Phase 5 — Optional expansion

- [ ] Additional noise engines and effect plugins
- [ ] Voice processing / optional AI processors
- [x] Document routing to and from external apps through Windows endpoints such as [VB-CABLE](VB_CABLE.md).
## Native C++ implementation checkpoint

- [x] Detect and invoke installed MSVC toolchain without requiring `cl.exe` globally on PATH
- [x] C++20 native RouteGraph: unique outputs, ordered repeated-effect instances and Gain processor
- [x] Core Audio endpoint discovery with Windows SDK; native non-audible smoke test
- [x] Native C++ WASAPI streaming prototype and optional WPF backend interop (hardening pending)

## Dark UI and buffering checkpoint (2026-11-24)

- [x] Dark ComboBox popup/selected/disabled/button templates
- [x] Single Start/Stop control
- [x] Native per-output render events, shorter bounded queues and underrun telemetry
- [x] Managed shorter queue, actual queue telemetry, direct-format path
- [x] 30-second muted native mixed-rate hardware soak; no post-warmup overruns or underruns
- [ ] Audible latency measurement, filtering quality and long-duration independent clock drift correction

## Native-only consolidation (2026-11-24)

- [x] Use C++ WASAPI as the sole audio engine
- [x] Move Core Audio endpoint discovery and mix-format reporting to native C ABI
- [x] Remove managed NAudio streaming and the backend selector
- [x] Build/Portable remains in project; no automatic copy to test Products folder
- [ ] Formal listening/latency and cross-device drift acceptance tests

## RNNoise processing milestone — local preview

- [x] Vendor pinned RNNoise v0.1.1 Standard model + BSD-3-Clause notice
- [x] Native C++ RNNoise frame adapter for 48/192 kHz and mono/stereo
- [x] Noise dropdown: Bypass vs RNNoise, including runtime switching
- [x] Preserve pure Bypass as default with no extra denoising DSP
- [ ] High-fidelity sample-rate conversion, audible QA and timing measurement
- [ ] Support compatible Little / Custom RNNoise models after source/ABI/licensing review

## Monochrome UI / icon milestone (2026-11-24)

- [x] Grayscale transparent icon and seven-size Windows .ico embedded in EXE
- [x] Monochrome Dark + Graphite WPF skin resource dictionaries
- [x] Skin switching with local portable appearance setting
- [x] Redesigned Sound Input → Filters → Sound Output UI, per-Gain collapse and Collapse all/Expand all
- [ ] Fully generic re-orderable native filter chain with repeatable RNNoise, Noise Gate, Limiter, EQ and other modules

## v0.2.0-preview.3 — ordered filter / themed window checkpoint

- [x] Custom dark WindowChrome title bar and resize system behavior
- [x] Styled ScrollBar thumb/track and Slider using theme resources
- [x] START/STOP single toggle, monochrome shades
- [x] Empty Filters section with centered + button / Choose filter... slots
- [x] Real native ordered filter graph, duplicate RNNoise/Gain/Gate/Limiter instances, moves and bypass
- [x] WPF ComboBox labels begin with endpoint Name, with {metadata} and no raw ID
- [ ] Formal listening/latency/clickless graph-swap tests and CPU benchmarking
- [ ] More filter types, independent per-stage advanced UI, state-preserving transitions

## v0.2.0 release (2026-11-24)

- [x] Ship AN silver/gray ribbon logo with transparent background in program EXE and PNG
- [x] Default newly added outputs to unmuted; newly added filter slots enabled
- [x] Verify ZIP contents, launch, native/managed tests and release documentation
- [ ] User listening checks with microphone + speakers at safe volumes
- [ ] Quantify per-filter CPU use and actual capture-to-output latency
## v0.2.1 Maintenance Release (2026-11-24)

- [x] Fix custom WindowChrome maximize geometry to respect monitor Work Area
- [x] Ensure close/minimize/restore remain visible with top-docked taskbar
- [x] Verify multi-monitor maximize/restore, minimize and close (3 displays)
- [x] Add repeatable `scripts/test-window-workarea.ps1` regression test

## Portable Studio v0.3.1

- [x] Open directly to Sound Input, ordered Filters and Sound Output.
- [x] Keep Portable filters and presets Windows-device independent.
- [x] Include the VB-CABLE routing guide in every generated release note.
- [ ] Measure end-to-end latency and test long-run independent device clock drift.
- [ ] Test reconnect behavior and per-output fault isolation.
