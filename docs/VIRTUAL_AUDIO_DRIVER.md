# Virtual Audio Driver — 1 Input + 1 Output shared by Windows

**Current source of truth:** [Virtual Driver Product Contract](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md) (confirmed 2026-11-25). Historical prototypes below do not override the product contract.

## Device model: one installed set, unlimited eligible audio clients

**AuralWeave Virtual Driver is one Windows-wide driver family**, exposing at most:

| Endpoint | Windows direction | Purpose | Maximum installed |
| --- | --- | --- | --- |
| AuralWeave Virtual Input | Recording / microphone | Feed the same virtual microphone to Discord, OBS and any supported Windows recording application | **1** |
| AuralWeave Virtual Output | Playback / speaker | Accept multiple playback sessions from browsers, games and other Windows applications | **1** |

The implementation may use **two Windows PnP devnodes and a shared kernel driver service/package**. This is still one installed AuralWeave driver set, not a new copy per client. The identity currently reserved for each role is respectively `ROOT\AURALWEAVE_VINPUT_0001` and `ROOT\AURALWEAVE_VOUTPUT_0001`.

An arbitrary Windows application must access these as ordinary Windows audio endpoints using Windows Audio Engine Shared Mode, **without a dependency on AuralWeave Studio, Mock Broker registration, custom shared-memory names, or client-side plugins**. Two or more eligible applications must be able to record or play simultaneously. Windows controls audio permissions, capabilities and the behavior of exclusive-mode requests; AuralWeave cannot promise that all unsupported or deliberately exclusive applications will cooperate.

Multiple Portable AuralWeave Studio instances from different folders may run simultaneously. Each retains independent routing, filter chains and settings; their eligible audio streams may be joined via a common audio bridge/mixer without adding PnP devices. The administrative install mutex must not be held during audio playback or recording.

## UI contract

A single **Virtual Driver** management page displays both Virtual Input and Virtual Output **status**. The only driver-related action inside Studio is **Install**, offered solely when one or both roles are missing and a signed production package is ready. The UI must not offer Uninstall, Remove, Repair, duplicate/extra endpoint slots, or silent auto-install on application startup.

Detect installed devices through Windows SetupAPI and exact PnP hardware ID + device class + service, including current, legacy and partially installed nodes. State comes from the computer, not the Portable folder. Missing driver package or invalid signatures must disable Install with an accurate explanation. If two Studio windows start installation concurrently, recheck Windows inventory under an installation-only mutex and again after elevation; create no duplicate device.

Removal is **outside Studio**: Windows Device Manager supports uninstalling the PnP devices. A Windows Settings **Installed apps / Apps & Features** uninstall entry is a desired packaging capability **only if** the future signed installer registers a real uninstaller; plain INF/SYS/CAT does not provide an entry automatically. See [Install-only management](VIRTUAL_DRIVER_INSTALLATION.md).

## Production signal-flow objective

```text
Windows app A + Windows app B + ... --> Windows shared-mode playback
                                           |
                                AuralWeave Virtual Output
                                           |
                       signed driver <--> authorized service/PCM bridge
                                           |
                               Native C++ routing / Filters
                                           |
                                  physical output(s)

Studio A + Studio B + ... --> mixed virtual microphone feed
                                           |
                             AuralWeave Virtual Input
                                           |
                       Windows shared-mode recording engine
                                       /       \
                                    Discord     OBS
```

The shared virtual endpoints must remain discoverable after closing all Studio windows, because installed PnP endpoints belong to Windows. A service or trusted background bridge may still be required to supply live audio; it must not pretend to be functional when stopped. The Windows engine's playback mixing is distinct from combining independent Studio microphone contributions. Design the output of the latter as one producer to the kernel microphone stream, with independent Windows capture clients rather than competing readers of one SPSC mapping.

The required production design includes secure driver↔service authentication independent of editable PCM mapping metadata, bounded buffer/cancelable IOCTLs, clock-driven timing, format normalization, interruption/restart recovery, hotplug and fail-safe silence.

## Implementation truth as of 2026-11-25

**Implemented and tested on Windows 10 (development/user-mode):**

- Native C++ WASAPI Studio routing and filter pipeline, WPF Portable host.
- SetupAPI read-only detection, exact current/legacy PnP ID guard and install readiness checks.
- Independent user-mode PCM rings; SharedVirtualAudioBus multi-contributor mixing and per-client listener fanout, including two Portable folders and owner-death cleanup.
- An unsigned, **compile-only** WDK WaveRT/PCM/IOCTL kernel prototype. The bridge mock covers independent virtual mic/speaker lifetimes, including zero-frame transfer while a role is idle. See [kernel transport](KERNEL_IOCTL_TRANSPORT.md).
- A non-elevated, same-user Mock Broker v0.3.0. Its owner-PID admission check protects only **test-only internal PCM mapping registration**, not Windows apps using an audio endpoint.

**Not implemented or proven:**

- Installed, signed and functional virtual microphone/speaker endpoints; production user↔kernel PCM bridge; privileged broker/service.
- Install-capable signed driver setup; Windows Apps registered uninstaller; tested uninstall/reboot.
- Real external Shared Mode concurrent Discord/OBS/browser/game acceptance; multi-Studio-to-real-driver audio.
- Windows 11 installed-driver runtime validation, HLK certification, measured production latency/drift.

**Install stays disabled until all production release gates pass.** Never install the unsigned WDK prototype on the host or enable driver test signing/Secure Boot bypass to simulate completion.

## Next steps

Follow the numbered implementation gates in the [Virtual Driver Roadmap](ROADMAP.md) and [Test Plan](TEST_PLAN.md). The [Handoff](VIRTUAL_DRIVER_HANDOFF_2026-11-25.md) names the exact unfinished tasks and current repository checkpoints.

Historical research remains in [Windows 10 and 11 driver prototype](VIRTUAL_DRIVER_WINDOWS10_WINDOWS11.md), [WaveRT checkpoint](WDK_WAVERT_PCM_CHECKPOINT.md), and [virtual PCM transport](VIRTUAL_PCM_BRIDGE.md). Earlier experiments with multiple device slots or a role-wide full-session lease are **superseded** by the approved product contract.
