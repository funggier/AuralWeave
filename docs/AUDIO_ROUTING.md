# Audio Routing & Signal Pipeline

## Route model

```text
Route:
  routeId (stable UUID)
  enabled, displayName, sourceEndpointId
  captureSettings { mode, requestedRate, requestedChannels, requestedBuffer }
  noise { engineId, config }
  effects [ { instanceId, engineId, enabled, order, config }, ... ]
  chainBypass
  outputs [ { outputId, endpointId, gain, mute, formatMode }, ... ]
```

`endpointId` refers to a real OS-enumerated persistent endpoint identity (where available), **not** display name. UI display names can repeat or change.

## Output dropdown rules

- Candidate set = currently discovered compatible output endpoints MINUS endpoints already selected in this Route.
- An already-selected endpoint remains in its own editing dropdown, but is excluded from other output rows.
- If candidate set is empty: show empty dropdown and disable add.
- Removing an output returns its endpoint to the candidate set.
- Output uniqueness is **per route**, not global across routes.
- On unplug: keep selected endpoint ID and show Disconnected; do not silently remap to a different endpoint with a similar name.
- On reconnect: re-evaluate endpoint identity and capability, then reopen safely; preserve output settings.

## Pipeline

1. Capture input into bounded timestamped buffers.
2. Normalize formats only when needed; channel mapping and resampling are explicit.
3. Process optional noise reduction.
4. Execute ordered, enabled effect instances, even if a type appears repeatedly.
5. Fan out processed blocks to independent output sinks.
6. Per-output gain, mute, format conversion, drift/buffer adaptation and render.
7. Collect buffer, callback, underrun, drop and latency telemetry.

## Bypass semantics

- Noise Bypass: skip denoising processor; effects still execute.
- Effect Bypass: skip only the specific effect instance; retain settings.
- Chain Bypass: skip noise and effect DSP stages (policy can be made explicit in UI); audio routing and necessary device format conversion still operate.
- Direct path optimization is a possibility, not a guarantee; device format, WASAPI shared-mode mixer and clocks may still transform the stream.

## Timing and clock domains

Two independent audio devices typically have independent clocks. Output branches require independent bounded buffer management, drift correction or adaptive resampling to avoid gradual underrun/overrun. Capture and output block sizes may differ; represent latency using measured components rather than one unverified fixed number.

## Risks and controls

- Never route output back to a captured loopback source without intentional feedback protection.
- Handle incompatible formats and exclusive/shared-mode conflicts explicitly.
- Allow a sink to fail independently while continuing remaining sinks.
- Avoid infinite restarts, unbounded queues, and busy-wait loops.
