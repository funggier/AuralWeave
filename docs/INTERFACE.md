# User Interface Specification

## App identity

Name: **AuralWeave**. Desktop-first Windows app. Style: clean, compact routing studio; practical controls with clear labels; display live device and processing state rather than dummy values.

## Single Studio screen

The window opens to Sound Input, Filters, and Sound Output. The header holds Refresh devices, skin selection, and Start/Stop. Filter presets are managed in the Filters area.

## Route editor layout

1. Route header: route name, enabled, Start/Stop, state indicator
2. Input Source: endpoint dropdown, meter, format and mode options
3. Noise Reduction: dropdown `Bypass`, `RNNoise`, future engines; parameters appear only when needed
4. Effects Chain: ordered rows; type dropdown, enable checkbox, settings, move up/down, delete; bottom `+` to append
5. Outputs: list of selected devices with per-output gain/mute/status and `−`; candidate dropdown and `+`
6. Signal overview: capture → processing → output links, latency, buffer and error counters

## Interactions

- Effect type dropdown **does not filter out already-used effect types**.
- Output add dropdown **does** filter out already-used endpoints **within the current route**.
- At zero available outputs show an empty/disabled dropdown and disabled add button; do not show a fake selectable placeholder device.
- Output rows can change selection without allowing duplicates; if swap causes conflict, reject with meaningful UI feedback.
- Enable/disable effects non-destructively; show actual active/bypassed/not-loaded state.
- A single Start/Stop button controls the active route.
- Reload UI from authoritative core state; opening UI should not unintentionally start audio or duplicate routes.

## Monitoring

Input and output meters, real measured latency if available, dropped/underrun frames, processor status, device disconnected, route errors. Throttle UI updates; do not use UI-driven high-frequency polling as the audio scheduler.

## Localization

Thai-first labels; engineering names can remain in English where standard. Store user choices in portable configuration files.
