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


## Multiple Portable instances and virtual driver ownership

AuralWeave may run from several directories on the same Windows machine.
Studio instances keep their own portable presets and can use regular audio
devices independently. Windows owns a **single Virtual Input** and a
**single Virtual Output**, regardless of the folder used to launch the app.
Cooperating app copies now take separate machine-wide named-mutex leases
before controlling each virtual role; competing controllers are rejected,
while the other role remains available. A lease is returned on clean
shutdown and abandoned automatically after a process crash.

This is currently a **user-mode advisory guard**, not a signed kernel
driver authorization mechanism. The driver Install controls remain
disabled. On Windows 10, different-folder and different-process tests
passed. See [Portable multi-instance ownership](docs/PORTABLE_MULTI_INSTANCE_CONTROL.md)
and [Kernel PCM wire protocol](docs/KERNEL_PCM_PROTOCOL.md). Windows 11
remains a theoretical compatibility target; no Windows 11 runtime test
is claimed.
