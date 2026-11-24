# AuralWeave

**Modular Real-time Audio Routing & Processing for Windows**

AuralWeave คือโปรเจกต์โปรแกรมจัดเส้นทางเสียงแบบ Real-time สำหรับ Windows รองรับการรับเสียงจากอุปกรณ์จริงหรืออุปกรณ์เสมือน ประมวลผลตามลำดับ และกระจายเสียงออกไปหลายอุปกรณ์พร้อมกัน

> **Project status:** Experimental Windows desktop and WASAPI audio route implemented; **not yet a product release**. Capture/render routing and a repeatable Gain effect have passed preliminary checks. RNNoise, independent clock correction, integrated native streaming and Virtual Audio Driver are not implemented.

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

Current implementation consists of a WPF desktop frontend and .NET/WASAPI prototype router (via NAudio), plus a separate native C++20 configuration/endpoint enumeration prototype compiled with MSVC. Native streaming is **not yet connected to the desktop**. See [implementation evidence and open gaps](docs/IMPLEMENTATION_STATUS.md). Do not confuse a successful muted test with a verified multi-device audible release.

Quick-start development:

```powershell
.\scripts\build-windows.ps1
.\scripts\build-native.ps1 -ListDevices
```

Running either script is optional; no automatic startup or virtual audio driver install occurs.

## License

[MIT License](LICENSE) — Copyright (c) 2026 funggier. Third-party code/models/drivers retain their own licenses.