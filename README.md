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
