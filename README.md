# AuralWeave

**Modular Real-time Audio Routing & Processing for Windows**

AuralWeave เป็นโปรแกรม Portable สำหรับจัดเส้นทางเสียงบน Windows เปิดมาแล้วเลือก **Sound Input → Filters → Sound Output** ได้ในหน้าจอเดียว ใช้ Native C++ และ WASAPI Shared Mode สำหรับอุปกรณ์จริงและอุปกรณ์เสมือนที่ Windows มองเห็น

- รับเสียงจากไมโครโฟน, Line In, USB audio หรือ playback loopback
- เรียงและเพิ่ม Gain, RNNoise Standard, Noise Gate, Limiter, Compressor และ EQ แบบ 3, 5 หรือ 7 ย่าน ซ้ำได้
- ส่งเสียงพร้อมกันหลายเอาต์พุต พร้อมปรับ Gain และ Mute แยกกัน
- บันทึกและนำเข้า Filter Presets แบบ Portable

[ดาวน์โหลด Windows x64 Portable จาก GitHub Releases](https://github.com/funggier/AuralWeave/releases)

## ส่งเสียงไปยัง Discord หรือโปรแกรมอื่นด้วย VB-CABLE

แนะนำ **VB-CABLE ของ VB-Audio** สำหรับส่งเสียงระหว่างแอปบนเครื่องเดียวกัน ติดตั้งจากผู้ผลิต แล้วเลือกอุปกรณ์ของ VB-CABLE ใน Windows ตามตาราง [ดาวน์โหลด VB-CABLE](https://vb-audio.com/Cable/)

| ตั้งค่าที่ | เลือก |
| --- | --- |
| AuralWeave → Sound Input | ไมค์หรือ Line In |
| AuralWeave → Sound Output | `CABLE Input` |
| Discord → Voice & Video → Input Device | `CABLE Output` |
| Discord → Voice & Video → Output Device | หูฟัง |

กด **START** ใน AuralWeave แล้วใช้ **Mic Test** ใน Discord โปรแกรมอื่น เช่น OBS เลือก `CABLE Output` เป็นอุปกรณ์รับเสียงได้เช่นกัน ดูขั้นตอนติดตั้ง การรับเสียงจากแอปอื่น และการต่อสองทางใน [คู่มือ VB-CABLE](docs/VB_CABLE.md)

## Development

Windows 10 22H2 x64 เป็นระบบทดสอบหลัก ใช้ MSVC C++ และ .NET 8 SDK

```powershell
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
.\scripts\build-windows.ps1 -PublishPortable
```

- [Windows build and release workflow](docs/WINDOWS_DEVELOPMENT.md)
- [Windows 10 Portable trial](docs/TRIAL_WINDOWS10.md)
- [Implementation status](docs/IMPLEMENTATION_STATUS.md)
- [Audio routing](docs/AUDIO_ROUTING.md), [ordered filters](docs/ORDERED_FILTER_CHAIN.md) and [presets](docs/FILTER_PRESETS.md)
- [Architecture](docs/ARCHITECTURE.md), [Roadmap](docs/ROADMAP.md) and [Test plan](docs/TEST_PLAN.md)

## License

[MIT License](LICENSE) · [Third-party notices](docs/THIRD_PARTY_NOTICES.md)
