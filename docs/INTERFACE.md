# User Interface Specification

## App identity

Name: **AuralWeave**. Desktop-first Windows app. Style: clean, compact routing studio; practical controls with clear labels; display live device and processing state rather than dummy values.

## Navigation

- **Routes:** main route list on the left; selected route editor on the right
- **Devices:** all enumerated endpoints with type/status/driver/capability
- **Presets:** save, import, export, activate route settings and complete profiles
- **Settings:** engine, latency/buffering, theme, model/plugin locations, virtual driver status

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
- Route start/stop and app-wide start/stop are separate actions.
- Reload UI from authoritative core state; opening UI should not unintentionally start audio or duplicate routes.

## Monitoring

Input and output meters, real measured latency if available, dropped/underrun frames, processor status, device disconnected, route errors. Throttle UI updates; do not use UI-driven high-frequency polling as the audio scheduler.

## Virtual Driver page — approved Install-only behavior (2026-11-25)

On the current Studio/Virtual Driver mode page, keep two compact status cards:

- **Virtual Input:** recording/microphone, installed PnP identity/status or absent/readiness
- **Virtual Output:** playback/speaker, installed PnP identity/status or absent/readiness

For each missing role, offer **Install** only if a trusted signed package is available. If both roles are missing, one combined install action is acceptable provided the UI reports per-role result and creates at most one instance of each. If one is present, install only the other. Installed, legacy, partially bound or unknown state must never enable duplicate creation. Explain missing package / authorization / pending reboot and refresh status from Windows PnP after operations.

**No Uninstall, Remove, Repair, Add driver instance or privileged device-delete button anywhere in Studio.** Windows Device Manager handles device removal; Installed Apps uninstall is possible only after a future driver setup registers a real uninstall entry. Do not silently install on startup. Multiple Portable Studio windows must display the same machine-wide PnP state and can share the same input/output devices via Windows shared-mode audio without a global audio lock.

The fact that test-only Mock Broker checks the PID that created a shared-memory mapping must not restrict ordinary Windows apps such as OBS or Discord from using the Windows endpoint.

Canonical UX/device specification: [VIRTUAL_DRIVER_PRODUCT_CONTRACT.md](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md).

## Localization

Thai-first labels; engineering names can remain in English where standard. Store user choices in portable configuration files.
