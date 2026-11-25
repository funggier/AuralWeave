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
## Approved Windows-wide Virtual Driver architecture (2026-11-25)

AuralWeave targets **one driver family / set per Windows installation**, exposing a single Windows Shared Mode **recording** endpoint and a single **playback** endpoint. The physical PnP device count is separate from the number of audio clients: multiple Windows applications and multiple Portable Studio instances must concurrently share these endpoints without custom app plugins or a per-application driver install.

The production kernel model may use two root-enumerated PnP nodes (one per role) and one kernel service/package; all Studio copies discover them through SetupAPI. Windows playback mix is handled through the Windows Audio Engine, while authorized Studio contributors to the virtual microphone are mixed before feeding the common microphone endpoint; each Windows recorder obtains an independent capture session. **Do not route several programs as competing consumers of a single SPSC ring**.

Installation must be serialized and idempotent across Studio copies, using a mutex **only around PnP mutation**, not while audio runs. Device removal is exclusively Windows managed; Studio is Install-only. A Windows Apps uninstall entry requires a separately registered installer/uninstaller. An installed driver and its trusted background bridge must not depend on a particular Studio window lifetime. Production IPC must enforce genuine Windows security, not trust editable mapping PID/guessed name or test broker handshake. Driver signing, actual WaveRT IOCTL/audio clock coupling and Windows 10/11 tests remain outstanding.

For authoritative implementation and acceptance requirements see [Virtual Driver product contract](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md) and [handoff](VIRTUAL_DRIVER_HANDOFF_2026-11-25.md).

## Existing implementation versus target design

- `AuralWeave.Core` (C#): route configuration rules and Gain DSP instances; tested.
- `AuralWeave.Audio.Windows` (C#/NAudio): active WASAPI capture, optional loopback, one-route fan-out into physical rendering; preliminary silent single-output smoke passed.
- `AuralWeave.Desktop` (WPF): prototype route editor and real-time meters; currently one active route; devices cannot be changed while running.
- `AuralWeave.Native` (C++20/MSVC): native RouteGraph and Windows endpoint enumeration, plus a separate C ABI WASAPI capture/render streaming engine with per-output bounded linear resamplers. Native backend is optionally selectable in WPF; full production-grade resampling and clock drift recovery are pending.
- `AuralWeave.Dsp` and `AuralWeave.VirtualDriver`: placeholders only.

Do not present the desired fully-native low-latency engine as delivered. This split is an incremental bridge; see [implementation status](IMPLEMENTATION_STATUS.md). After Visual Studio Installer activity is finished, validate any newer toolsets without forcing changes to installed components.

## Native-only architecture decision (2026-11-24)

[ADR 0003](adr/0003-native-only-audio.md) supersedes the earlier dual-backend prototype. The application now has **one** audio implementation: native C++ WASAPI. The WPF UI and C# Core remain a control plane only. NAudio and the managed capture/render modules have been removed. Read earlier sections as historical architecture planning where they describe dual backend behavior.
