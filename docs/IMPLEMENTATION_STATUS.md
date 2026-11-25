# AuralWeave — native-only implementation and historical checkpoints

**Current approved shared-driver contract (2026-11-25):**
[VIRTUAL_DRIVER_PRODUCT_CONTRACT.md](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md).
**Handoff:** [VIRTUAL_DRIVER_HANDOFF_2026-11-25.md](VIRTUAL_DRIVER_HANDOFF_2026-11-25.md).
Several older checkpoints below describe superseded experiments, notably
per-device removal and full-session exclusive role mutexes. Those historical
sections must **not** override the current Install-only/shared-device plan.
**No real signed and installed virtual audio driver has been verified.**

## Architecture

- The only production-path audio backend is `AuralWeave.Native.dll` (C++20, WASAPI shared mode).
- .NET 8/WPF implements the desktop interface and native P/Invoke control plane, **not** managed audio streaming.
- Native endpoint enumeration is exposed through a C ABI; no NAudio package reference remains.
- The UI has a single Start/Stop button, dark dropdowns, 1 input, unique selected outputs per route, separate output gain/mute and repeatable Gain effect instances.
- Noise selection now supports **Bypass (default)** or **RNNoise v0.1.1 Standard**. Little and Custom model options are placeholders, not available in this build.
- Build and Portable Preview output are retained under the source project `T:\Space\Projects\ProjectsProgram\AuralWeave`. Copies under `T:\Space\Products` are user-managed testing copies, not the product's build location.

## Subjective user evidence

The tester reported that Native C++ performed much better and that its latency was **not perceptible** during listening. This is qualitative evidence, not a measured round-trip or capture-to-render time. Preserve the native WASAPI event-driven capture/render architecture that passed their testing.

## Automated evidence from prior checkpoint

- 16 native smoke/unit checks and 13 managed route-model tests passed.
- 30-second muted 192-kHz Line In -> 192-kHz USB Headphones + 48-kHz display audio test: 5,765,760 captured frames, 0 buffer overruns; no new overrun or underrun after the 2-second warmup.
- Dark theme and native event-driven multi-output design retained.

## Important unfinished work

- RNNoise v0.1.1 Standard is integrated; no Little/Custom model, EQ, voice-changing AI, presets or virtual audio driver.
- The native linear resampler has no production-grade anti-alias filter.
- Long-term independent device drift correction, hotplug, sink fault isolation and calibrated end-to-end latency measurement are pending.
- Native mix formats currently require mono/stereo Float32 or PCM16; unsupported formats fail with a clear error.

See [ADR 0003](adr/0003-native-only-audio.md), [Preview Notes](PREVIEW_NOTES.md), and [Test Plan](TEST_PLAN.md).
## RNNoise local preview progress

Native model and MSVC compatibility changes are recorded in [RNNOISE_INTEGRATION.md](RNNOISE_INTEGRATION.md). Muted hardware Bypass/RNNoise switching passed on mixed-rate output devices; subjective quality and measured end-to-end latency are not yet established.

## v0.2.0-preview.3 checkpoint — new route filters / custom chrome

Native C++ ordered filter chain is now integrated and tested: initial empty bypass, repeatable Gain/RNNoise/Gate/Limiter, per-instance bypass, true processing order, and live graph replacement. WindowChrome + grayscale scrollbars/sliders are in the Desktop UI. Windows endpoint names are shown before audio-format metadata in braces and raw IDs remain internal. See [ORDERED_FILTER_CHAIN.md](ORDERED_FILTER_CHAIN.md). Older sections in this document describe previous milestones.

## Local v0.3.0-preview.1 checkpoint

- UI: Studio / Virtual Driver selector; a single Virtual Driver page with separate Virtual Input (recording) and Virtual Output (playback) cards; increased margins for mode selector, preset controls and driver action areas.
- Presets: Save/Load/Import/Export current ordered Filters as portable .awfilter JSON files, without Sound Input or Output device selection.
- Driver manager: Win32 SetupAPI enumerates reserved AuralWeave-family PnP hardware IDs independent of the executable folder; a guarded per-instance removal UI was present in this historical prototype, **but was subsequently removed**; current Studio is Install-only.
- Not yet implemented: first-party signed virtual audio kernel driver, installation or creation of virtual endpoints, IPC audio bridge. Install controls remain intentionally disabled.
- Validation: 32 Core tests, real Windows two-mode UI test, native PnP scan (zero matching installed), Portable ZIP and custom WindowChrome maximize/restore across three displays passed.

## Driver development toolchain (2026-11-25)

WDK 28000, SDK 28000, VS 2026 WDK integration and pinned SysVAD
reference/WIL are installed. Driver toolchain checker passes.
Full upstream reference source compiles in compile-only mode.
At this point of the historical timeline no AuralWeave kernel prototype
had yet compiled. Later checkpoints below document an **unsigned compile-only
prototype**, not a signed or installed functional audio driver.

## Kernel prototype compiled — Windows 10 + Windows 11 target (2026-11-25)

A new AuralWeave-branded unsigned WDM/PortCls virtual audio
prototype now compiles into `AuralWeaveVirtualAudio.sys`. It
selects capture or render KS interfaces according to independently
enumerated AuralWeave root hardware IDs. Maximum reserved ID bank:
1 input + 1 output. Windows 10 build 19041+ / Windows 11 x64 INF
model selection; InfVerif /w /u and Inf2Cat /nocat signability pass.
Device discovery is hardened to the MEDIA class, AuralWeave service
and exact ID bank. No live PCM driver transport, Windows 11 runtime
tests, HLK, signing or install yet. The driver binary is excluded
from portable app publication.

## Native user-mode PCM bridge (2026-11-25)

C++ SPSC Shared Memory transport is implemented with per-role/slot
isolation, bounded PCM, random session name, Float32 mono/stereo,
cross-process tests, C ABI and managed wrappers. Studio now supports
a developer-only post-filter tap for a future virtual microphone;
physical outputs remain unchanged. Real WASAPI 192-kHz capture
resampled to a 48-kHz Shared PCM tap and was consumed without
overrun in a 1.2-second test. The unsigned kernel prototype is
still NOT attached to this transport, and virtual output input
selection, Windows 11 VM tests and Microsoft signing remain pending.


## One Virtual Input / One Virtual Output (2026-11-25)

- The current INF exposes one PnP hardware ID for each role, both ending in 0001.
- The AuralWeave PnP manager allows future installation only when no current
  or legacy device of that role exists; unsigned driver installation stays disabled.
- Former 0002..0016 prototype IDs are recognized for safe, explicit cleanup.
- The developer-only Native Studio route now accepts a Virtual Output PCM source
  through the same ordered Filter graph and optional Virtual Input PCM tap.
- Windows 10 user-mode test: 57,600 frames consumed, 57,599 frames emitted,
  Gain applied, zero source overflow, physical output muted.
- Windows Kernel driver transport, signing and Windows 11 runtime validation
  have NOT yet been implemented or performed.


## Cross-folder virtual control and wire protocol (2026-11-25)

Two simultaneous AuralWeave Portable copies can run Studio separately.
**Historical, superseded:** this checkpoint used full-session Global
role mutexes and had PnP removal code. The current architecture uses brief
administrative install coordination only; multiple audio sources and listeners
can run concurrently from independent Portable copies and Studio has **no
Uninstall/Remove** action. Cross-process Windows 10 user-mode sharing tests
passed in the later checkpoint. See [approved shared-driver contract](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md).

A fixed 64-byte transport header and caller-synchronized S16/F32 PCM
ring have been added under the original MIT license. The same source
compiles inside the WDK kernel prototype and runs as a Windows 10
user-mode test. It validates direction, version, length and PCM format.
**Kernel IOCTL dispatch and WaveRT PCM transport are not implemented.**
No driver is signed or installed. Windows 11 compatibility is theoretical.


## Current Virtual Driver product behavior — install-only (2026-11-25)

The Virtual Driver screen no longer offers an uninstall button or
administrator PnPUtil removal path. It checks the Windows PnP ID list,
including legacy and partially registered nodes, and offers Install
only if the selected role is absent. Readiness also requires all
production INF/SYS/CAT/setup files and trusted Windows signatures.
Because the current kernel driver has neither a completed WaveRT PCM
transport nor a signed production installer, Install is deliberately
disabled in the current build.

Meanwhile the user-mode SharedVirtualAudioBus mixes simultaneous PCM
sources and broadcasts independent copies to listeners. Audio-session
activity guards are cross-folder; the brief PnP management mutex
does not monopolize ongoing audio use. These user-mode tests passed
on Windows 10. Windows 11 is a theoretical compatibility target.


## WaveRT internal PCM and installer-helper checkpoint (2026-11-25)

**Build-stage only. No Windows driver was installed or signed.**

The unsigned Windows 10 WDK prototype now compiles kernel-internal,
independently locked 64-KiB PCM rings for Virtual Input and Virtual
Output. The WaveRT microphone DMA path consumes queued PCM or emits
silence; WaveRT speaker DMA copies rendered frames into its bounded
PCM queue. The Microsoft Sample sine generator, file recording worker
and runtime registry toggles were unlinked. A global single-adapter
restriction was found and changed to allow **Input + Output together**
in the driver architecture. Digital rights that forbid copying disable
render PCM mirroring and flush queued frames.

No secure kernel device IOCTL or user broker is connected yet, so the
driver is NOT a usable installed Windows audio endpoint.

The separate Windows 10 DriverSetup project now builds as a self-contained
read-only preflight utility and contains future signed-only PnP SetupAPI
logic. The production certificate SHA-256 pin is intentionally blank,
so all actual installation requests remain disabled. Uninstallation
remains a Windows Device Manager operation.

See docs/WDK_WAVERT_PCM_CHECKPOINT.md for source, design and QA.


## Secured Kernel IOCTL PCM checkpoint (2026-11-25)

The unsigned Windows 10 WDK prototype now compiles a separate
SYSTEM/Administrator-only WDM control device with strict
METHOD_BUFFERED request validation. Audio PnP requests still reach
PortCls through PcDispatchIrp. Virtual Input accepts signed PCM32
and Virtual Output returns signed PCM16. The versioned protocol is
bounded to 2,048 frames per transfer and at most 16,384 bytes of PCM.

The managed WindowsVirtualKernelPcm client and the explicit
VirtualKernelAudioPump are compiled and mock-tested. A Native C++
Shared PCM input flowed through the simulated kernel protocol and
was received on an independent Shared PCM output with correct
stereo sample values. Real kernel installation/IOCTL transfer has
NOT been tested. An elevated trusted broker is not yet shipped.

See docs/KERNEL_IOCTL_TRANSPORT.md for the exact access, data layout,
QA and remaining signed-driver prerequisites. The current driver is
UNSIGNED and must NOT be installed on the host Windows 10 computer.