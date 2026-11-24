# Roadmap

No phase below is claimed complete beyond repository/document initialization.

## Phase 0 — Repository foundation

- [x] Establish product name AuralWeave
- [x] Project directories
- [x] Requirements, architecture, UI, routing, effects, driver, testing docs
- [x] MIT license and third-party notice policy
- [ ] Configure CI after implementation build/test tooling is selected

## Phase 1 — Minimal functional audio route

- [ ] C++/WASAPI capture and render proof of concept
- [ ] Device enumeration and stable identity
- [ ] Start/Stop one route with one input and one physical output
- [ ] Basic UI and real meter/status
- [ ] Format adaptation and bounded buffering
- [ ] Recovery from stop/disconnect, measured performance

## Phase 2 — Multiple simultaneous outputs

- [ ] Add/remove output controls with per-route uniqueness
- [ ] Simultaneous fan-out and independent output gain/mute
- [ ] Per-device clock drift/underrun handling
- [ ] Hotplug and per-output status
- [ ] Regression tests for every dropdown edge case

## Phase 3 — Noise and effects

- [ ] Noise processing selector: Bypass/RNNoise
- [ ] Frame adapter and RNNoise quality tests
- [ ] Ordered effect instances, repeated types, independent settings
- [ ] Enable/bypass/reorder/remove and safe reconfiguration
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
- [ ] Virtual playback and recording driver with installer and tested signing
