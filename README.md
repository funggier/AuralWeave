# AuralWeave

**Modular Real-time Audio Routing & Processing for Windows**

AuralWeave คือโปรเจกต์โปรแกรมจัดเส้นทางเสียงแบบ Real-time สำหรับ Windows รองรับการรับเสียงจากอุปกรณ์จริงหรืออุปกรณ์เสมือน ประมวลผลตามลำดับ และกระจายเสียงออกไปหลายอุปกรณ์พร้อมกัน

> **Project status:** Initial repository and specifications only. ยังไม่มี Audio Engine, UI ที่เชื่อมกับอุปกรณ์จริง หรือ Virtual Audio Driver ที่ใช้งานได้

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

Directories under `src/` are module placeholders, **not working implementations**. Recommended initial target: native C++ audio core using Windows WASAPI, decoupled desktop UI (candidate: .NET/WPF), RNNoise as optional DSP adapter, and an isolated future virtual-driver project.

## License

[MIT License](LICENSE) — Copyright (c) 2026 funggier. Third-party code/models/drivers retain their own licenses.
