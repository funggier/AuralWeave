# AuralWeave

**Modular Real-time Audio Routing & Processing for Windows**

AuralWeave คือโปรเจกต์โปรแกรมจัดเส้นทางเสียงแบบ Real-time สำหรับ Windows รองรับการรับเสียงจากอุปกรณ์จริงหรืออุปกรณ์เสมือน ประมวลผลตามลำดับ และกระจายเสียงออกไปหลายอุปกรณ์พร้อมกัน

> **Project status:** Experimental Windows desktop and WASAPI audio route implemented; **not yet a product release**. Capture/render routing and a repeatable Gain effect have passed preliminary checks. Native C++ streaming and muted mixed-rate multi-output routing have now passed preliminary hardware smoke tests. RNNoise Standard processing is now available in a newer **local development preview**, but production-grade clock correction and Virtual Audio Driver are not implemented.

## Concept

```text
Input Source
    |
Capture + Buffer + Format Normalization
    |
Noise Reduction [Bypass | RNNoise | future engines]
    |
Effects Chain [FX instance 1 -> FX instance 2 -> ...]
    |
Output Distributor
    +----> Physical Speaker / Headphones
    +----> USB DAC
    +----> Virtual Microphone (optional driver, later)
```

- หนึ่ง Audio Route มี Input หนึ่งช่องและ Output ได้หลายช่องพร้อมกัน
- Output ที่ใช้แล้วภายใน Route เดียวกันจะไม่ปรากฏในรายการเพิ่มอีก
- เลือก Output ครบทุกตัวแล้ว Dropdown เพิ่มจะว่างและปุ่ม + ถูกปิดใช้งาน
- การลบ Output คืนอุปกรณ์เข้าสู่ตัวเลือก
- Noise Reduction เป็น Dropdown แยกจาก Effects Chain โดยมี Bypass และ RNNoise เป็นชุดเริ่มต้น
- Effects Chain รองรับเอฟเฟกต์ชนิดเดียวกันซ้ำได้ โดยแต่ละ Instance มี ID และการตั้งค่าเป็นของตัวเอง
- Bypass Noise Reduction ไม่ได้หมายถึง Bypass Effects Chain
- ออกแบบให้โปรแกรมหลักเป็น Portable / Relocatable; การติดตั้ง Virtual Driver เป็นส่วนแยกและต้องยกระดับสิทธิ์ตามข้อกำหนดของ Windows

## Documentation

| Document | Description |
| --- | --- |
| [Implementation status](docs/IMPLEMENTATION_STATUS.md) | หลักฐานการ Build/Test และข้อจำกัดของต้นแบบ |
| [Windows development](docs/WINDOWS_DEVELOPMENT.md) | Compiler, Windows SDK และคำสั่ง Build |
| [Product requirements](docs/PRODUCT_REQUIREMENTS.md) | ขอบเขตและกฎที่ต้องรองรับ |
| [Architecture](docs/ARCHITECTURE.md) | โมดูลหลักและการแยกความรับผิดชอบ |
| [Audio routing](docs/AUDIO_ROUTING.md) | Signal flow, timing, device identity |
| [Effects & plugins](docs/EFFECTS_AND_PLUGINS.md) | DSP pipeline และ repeated effect instances |
| [Interface](docs/INTERFACE.md) | หน้าจอ Routes, Devices, Presets, Settings |
| [Virtual driver](docs/VIRTUAL_AUDIO_DRIVER.md) | แนวทาง Virtual Input/Output และข้อจำกัด |
| [Roadmap](docs/ROADMAP.md) | ระยะพัฒนา |
| [Test plan](docs/TEST_PLAN.md) | Acceptance criteria และ edge cases |
| [Third-party notices](docs/THIRD_PARTY_NOTICES.md) | นโยบายสิทธิ์ส่วนประกอบภายนอก |
| [ADR 0001](docs/adr/0001-initial-architecture.md) | การตัดสินใจสถาปัตยกรรมเริ่มต้น |

## Development status

Current implementation has a Windows .NET/WPF desktop for controls, but all real-time audio capture/render and endpoint discovery are provided exclusively by `AuralWeave.Native.dll` (C++20/WASAPI). The NAudio managed audio backend and engine selection were removed following a successful listening test. The user reported **no perceptible native latency**; exact timing has not been measured. See [ADR 0003](docs/adr/0003-native-only-audio.md), [implementation status](docs/IMPLEMENTATION_STATUS.md), and [known limitations](docs/PREVIEW_NOTES.md).

Quick-start development:

```powershell
.\scripts\build-windows.ps1
.\scripts\build-native.ps1 -ListDevices
```

Running either script is optional; no automatic startup or virtual audio driver install occurs.

## License

[MIT License](LICENSE) — Copyright (c) 2026 funggier. Third-party code/models/drivers retain their own licenses.
## Post-v0.1.0 local preview

Optional RNNoise v0.1.1 Standard processing is implemented in the source tree with an offline build and live Bypass/RNNoise switching. The published GitHub `v0.1.0` remains unchanged; see [integration details](docs/RNNOISE_INTEGRATION.md).

## Appearance — Monochrome Skin Preview

The current local preview includes the silver/charcoal transparent program icon, embedded multi-size Windows .ico, and a WPF UI skin selector (Monochrome Dark / Graphite). The new desktop flow is Sound Input → Filters → Sound Output. See [Skins and icon](docs/SKINS_AND_ICON.md). Audio processing remains Native C++ only; arbitrary filter reordering and repeatable non-Gain DSP are planned, not shipped.

## Current local preview: Custom Window and ordered Filters

The new local `v0.2.0-preview.3` supports a themed custom title bar and scrollbars, START/STOP, and a truly ordered Native C++ filter graph. Filters start empty and + creates a Choose filter... slot. Gain, RNNoise Standard, Noise Gate and Limiter may be repeated or reordered, with DSP processed in visual order. Device dropdowns display the actual Windows Name first followed by audio metadata in braces. See [ordered filters](docs/ORDERED_FILTER_CHAIN.md). GitHub v0.1.0 release remains unchanged.

## Windows v0.2.0 release

The latest packaged version uses the white/silver 3D AN ribbon app icon,
the fully custom dark window, and an ordered native Filter Chain. New filters
are enabled and newly added output devices are **unmuted** by default;
be careful of microphone feedback. The native audio path remains C++ WASAPI.
See [release notes](docs/releases/v0.2.0.md).
## Windows v0.2.1 maintenance fix

Custom maximize now respects the Windows taskbar's usable Work Area on
each monitor, ensuring the custom minimize, restore and close buttons remain
visible even when the taskbar is docked at the top. See
[release notes](docs/releases/v0.2.1.md) and the repeatable
`scripts/test-window-workarea.ps1` regression test.
## Local v0.3.0-preview.1: Virtual Driver Manager + Filter Presets

The Studio page now has portable Filter Presets (SAVE/LOAD/IMPORT/EXPORT without recording Sound Input or Output). The new Virtual Driver page has two separate cards on one page for Windows Virtual Input (recording) and Virtual Output (playback). Windows PnP discovery recognizes reserved AuralWeave hardware IDs even after the app is relocated. **No signed virtual audio driver ships yet, so INSTALL remains disabled.** See [Virtual driver design](docs/VIRTUAL_AUDIO_DRIVER.md), [Filter presets](docs/FILTER_PRESETS.md), and [preview notes](docs/releases/v0.3.0-preview.1.md).

## Windows Virtual Driver development

Driver development dependencies are now installed and verified.
See [Driver Toolchain Setup](docs/DRIVER_DEVELOPMENT_SETUP.md),
`scripts/check-driver-toolchain.ps1`, and
`scripts/build-sysvad-reference.ps1` for repeatable **reference
compile-only** tests. A signed and installable AuralWeave virtual
audio driver is **not yet shipped**; Studio remains fully usable
without any optional kernel driver.

## Virtual Audio Kernel Prototype — Windows 10 & 11 (2026-11-25)

This development checkpoint adds a separately licensed **MS-PL**
`AuralWeaveVirtualAudio.sys` source prototype with different PnP
registrations for Virtual Input (recording) and Virtual Output
(playback). The INF now reserves exactly one PnP ID per role and targets
Windows 10 build 19041+ and Windows 11 x64.

`scripts/build-virtual-driver-prototype.ps1` performs compile-only
WDK build, InfVerif Windows/Universal checks and Inf2Cat signability
tests. **No driver is installed or signed, and no audio bridge to Studio
exists yet.** The Virtual Driver page INSTALL buttons remain disabled.
Kernel source is under its own [MS-PL license](src/AuralWeave.VirtualDriver/Kernel/LICENSE.MS-PL.txt),
not covered by the MIT root license. See
[Windows 10 and Windows 11 driver progress](docs/VIRTUAL_DRIVER_WINDOWS10_WINDOWS11.md).

## User-mode Virtual PCM Bridge (2026-11-25)

The native Windows C++ engine now has an opt-in, independently buffered
post-filter PCM bridge for future virtual microphones. It uses isolated,
versioned, session-local Shared Memory mappings, with one writer and one
reader per mapping. Input/output roles and slots remain distinct; the
developer-facing C ABI and .NET wrapper are in the repository.

A real Windows 10 muted hardware smoke verified 192-kHz capture,
ordered Gain filter, conversion to 48-kHz stereo Shared PCM and
continuous consumption without dropped frames. The Virtual Output
shared channel also passed cross-process tests; developer-only Native routing is now implemented, while the Windows driver remains unconnected
to a WaveRT endpoint or Studio source selector.

This is **not yet a working installed kernel virtual audio driver**.
No driver install option has been enabled. For architecture, tests and
limitations see [Virtual PCM Bridge](docs/VIRTUAL_PCM_BRIDGE.md).


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


## Shared Virtual Audio and Windows Install-Only management (2026-11-25)

AuralWeave can run from more than one Portable directory. Each Studio
has its own presets and Filters. Windows supports only **one virtual
recording device and one virtual playback device**, but user-mode audio
can be shared among multiple processes: source streams are mixed
together and listeners receive independent copies of that mix.

The developer-only SharedVirtualAudioBus has passed Windows 10
cross-folder tests involving two simultaneous source processes and
two independent listeners, including short blocks, clipping, and
slow subscribers. It is **not a functioning installed Windows driver**.

The Virtual Driver UI now **only displays device state and offers
INSTALL when the role is absent**. It has no Uninstall button and never
calls PnPUtil to remove a device. Windows Device Manager is responsible
for uninstalling hardware driver devices. Install stays disabled
without the complete, trusted, signed production driver bundle.

Current unsigned WDK prototype has no completed WaveRT PCM transport;
no production installer exists yet. Windows 11 remains a theoretical
compatibility target, not a tested runtime.

Details:
- [Portable multi-copy audio sharing](docs/PORTABLE_MULTI_INSTANCE_CONTROL.md)
- [Windows install-only driver workflow](docs/VIRTUAL_DRIVER_INSTALLATION.md)
- [Native PCM bridge](docs/VIRTUAL_PCM_BRIDGE.md)


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

## Automated Windows portable Studio preview (2026-11-25)

The [Windows portable preview workflow](.github/workflows/windows-portable-preview.yml)
builds the native C++/WPF Studio and runs user-mode virtual PCM, mock kernel
protocol and cross-folder tests on a Windows runner. Only a successful run
uploads the **Studio-only Windows x64 Portable ZIP** as a temporary Actions
artifact. The ZIP audit rejects kernel SYS/INF/CAT, developer setup utilities
and debug files. The workflow never installs a kernel driver or enables
Windows test-signing.

**Installation of the Virtual Driver remains disabled.** An unsigned
kernel prototype passing a build is *not* a working Windows audio endpoint.
Windows 11 is still a compatibility target, not runtime-verified.
For download, safe listening checks and remaining acceptance criteria,
see [Windows 10 Portable trial guide](docs/TRIAL_WINDOWS10.md).

## Multi-client kernel PCM mock scheduling checkpoint (2026-11-25)

The developer-only `VirtualKernelAudioCoordinator` combines independent
Portable-app microphone PCM contributors into one simulated kernel-facing
PCM32 writer, then fans PCM16 speaker data back out to independent listeners.
`VirtualKernelAudioWorker` adds explicit start/stop, endpoint format checks,
coalesced timer ticks and fail-stop error propagation. Tests for startup,
shutdown, missing endpoints, device loss and multi-client mixing are wired
into the Windows preview workflow.

**GitHub Actions run #22 passed for this checkpoint** (Native build,
PCM tests, and Studio-only portable artifact). This is
user-mode reference code, **not** an elevated broker, signed driver,
auto-starting service or working Windows audio endpoint. Production timing,
client authorization, runtime kernel validation and driver signing remain
outstanding. See [coordinator checkpoint](docs/KERNEL_PCM_COORDINATOR_CHECKPOINT.md).

## Windows Portable Studio v0.3.0 (2026-11-25)

Studio v0.3.0 has ordered native filters, portable Filter Presets, the
read-only Virtual Driver status page and safe user-mode multi-client PCM
recovery. A source PCM owner killed without Dispose is detected through a
Windows per-mapping lifetime HANDLE, and stale source/listener channels
are removed while surviving channels continue. Windows 10 diagnostics and
a Studio-only ZIP build passed locally; GitHub Actions verification of this
new checkpoint is tracked separately. **No signed Virtual Audio Driver is
included or installed; INSTALL remains disabled.** Windows 11 remains
runtime-unverified. See [release notes](docs/releases/v0.3.0.md) and
[owner crash recovery](docs/PCM_OWNER_LIFETIME_RECOVERY.md).

## Separate local Mock Broker prototype (2026-11-25)

An explicit, **non-elevated** `AuralWeave.Broker --mock` host now offers
a current-Windows-user-only Named Pipe control endpoint. Independent Native
PCM session owners register mic contributors or output listeners, with
one bounded bus mixing microphone PCM and broadcasting it to independent
listeners. The managed `LocalBrokerControlClient` has a 4-second heartbeat,
registration/status methods, and awaited disconnect; the server rejects
oversized/invalid commands, expired sessions, unsupported mappings and
duplicate broker instances. Real process-to-process tests pass on Windows 10.

The mock process **never** opens the privileged kernel device, installs a
service, changes Windows PnP or creates a usable Windows virtual mic/speaker.
The regular Studio ZIP does not include this testing host. A *separate*
temporary Actions artifact publishes a standalone, relocatable **Mock Broker
for testing only** with its own explicit startup script. It must not be
confused with a signed production Virtual Driver. See
[Mock Broker design and trial instructions](docs/LOCAL_BROKER_MOCK.md).

## Mock Broker v0.2.0: versioned restart recovery

The non-elevated Mock Broker now negotiates **HELLO 2** on a separate
`AuralWeave.MockBroker.Control.v2` pipe, reports a unique process instance
identifier and strictly typed telemetry, and explicitly refuses to reuse
old PCM FIFO state after broker crash/restart. The managed endpoint session
helper creates fresh user-mode PCM rings and rejects a fake restart.
Windows 10 cross-process tests cover hard process kill, old-pipe failure,
correct new PCM and idle heartbeat. This is **not** a production service,
a Windows audio device, or a working/signed Virtual Driver.
See [restart design and tests](docs/BROKER_CRASH_RESTART_CHECKPOINT.md).

## Mock Broker v0.3.0 — Native PCM origin validation

The separate, non-elevated mock control pipe is now **v3** and the Native
PCM shared-memory header is **v2**. A client registration is accepted only
when the process ID reported by the connected Windows Named Pipe matches
the advisory creator PID of the native PCM mapping. Independent Windows
process tests prove that unrelated processes cannot casually claim an
unregistered mapping created by another process, while the true creator
can register, deliver PCM, and safely disconnect. **This is not trusted
identity or security authorization**: a malicious same-user process may
modify shared memory. The real privileged broker and signed driver remain
separate, unimplemented milestones. See
[origin checking scope and tests](docs/BROKER_PROCESS_ORIGIN_CHECKPOINT.md).

