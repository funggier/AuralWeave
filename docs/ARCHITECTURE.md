# Architecture — AuralWeave

## Suggested components

```text
Desktop UI / ViewModels
    | Commands + snapshots (no DSP on UI thread)
Application / Route Manager
    | Atomic route/graph configuration
AuralWeave.Core
    | Audio graph, processing scheduler, bounded ring buffers
    +--- Device abstraction / discovery
    |      \--- AuralWeave.Audio.Windows (WASAPI capture/render/loopback)
    +--- Noise processor slot (Bypass, RNNoise adapter)
    +--- Ordered Effects Chain (instance ID / config / state)
    +--- Output Distributor (one branch per device; drift handling)
    +--- Metrics snapshots and diagnostics
    \--- Optional Virtual Device integration (later)
```

## Module responsibilities

- `AuralWeave.Core`: immutable route specs, graph lifecycle, pipeline, fan-out, buffer synchronization, status events
- `AuralWeave.Audio.Windows`: WASAPI endpoints, endpoint IDs, format negotiation, hotplug, loopback, session/event handling
- `AuralWeave.Dsp`: noise-reduction interface and effect processor API, channel/frame adapters, latency reporting
- `AuralWeave.Desktop`: WPF candidate, selection state, routing UI, meters, presets, error surfaces
- `AuralWeave.VirtualDriver`: optional Windows driver artifacts and installer plan; isolated from portable app distribution

## Threading

- Control plane: UI and route configuration; serialize state updates and validate changes
- Real-time audio plane: capture/process/render with bounded queues and no blocking work inside callbacks
- Background plane: device enumeration, plugin/model loading, persistent settings, logs
- Publish meter/status snapshots at a low controlled rate to avoid UI polling loops

## Interface contracts (design-level)

- `IAudioDeviceProvider`: enumerate, open, listen for add/remove/default-change events
- `INoiseReducer`: prepare, process, reset, latency, dispose
- `IAudioEffect`: create-instance, prepare, process, bypass, config, reset, latency
- `IAudioRouteEngine`: prepare/start/stop/reconfigure, snapshot, report errors
- `IOutputSink`: format negotiation, render, buffer/clock telemetry

The exact ABI and implementation language boundaries will be set during core prototyping. Version plugin APIs explicitly.

## Safe reconfiguration

Validate new route configuration outside the audio callback; initialize resources and build a replacement graph; swap at an audio-block boundary; retire old resources after in-flight processing is complete. Do not block realtime callbacks for model loads, file I/O, or device enumeration.

## Failure isolation

A failing sink can be marked disconnected without stopping healthy siblings; report per-output status. If a source fails, mark its route degraded/stopped. Recovery is explicit/retry-based and bounded; do not create unbounded reconnect or polling loops.

## Portability

Prefer paths relative to the executable root. User-specific settings/cache/models/presets stored locally by default. Driver installation is OS-level and separately managed; portable does not mean driverless virtual audio.
