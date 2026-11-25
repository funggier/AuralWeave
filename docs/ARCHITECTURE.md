# Architecture — AuralWeave

## Desktop and audio engine

- `AuralWeave.Desktop` is a WPF control plane. It shows Sound Input, ordered Filters, Sound Output, presets and route status in one window.
- `AuralWeave.Core` owns route and filter models, validation, and the portable preset format.
- `AuralWeave.Audio.Windows` exposes the native audio engine and endpoint catalog to .NET through the C ABI.
- `AuralWeave.Native` enumerates Windows endpoints, captures and renders in WASAPI Shared Mode, processes the ordered C++ filter chain, and resamples into an independent bounded queue per output.

[Native-only backend decision](adr/0003-native-only-audio.md) · [Audio pipeline](AUDIO_ROUTING.md)

## Endpoints and portable settings

A route selects one Windows capture or loopback source and one or more Windows playback endpoints. The endpoint IDs and shared-mode formats come from Windows. This includes physical devices and independently installed virtual cables. Each Portable folder stores its own filter presets and appearance settings. [Routing audio to other applications](VB_CABLE.md)

## Threading and timing

The native worker handles WASAPI capture and render events. Control-plane changes validate filter configuration and prepare the replacement graph before publication. The WPF thread displays telemetry on its own update cadence. Device clock drift, long-run latency and hotplug recovery are tracked in the [roadmap](ROADMAP.md).
