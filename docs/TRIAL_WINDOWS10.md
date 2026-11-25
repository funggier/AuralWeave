# ทดลอง AuralWeave Portable บน Windows 10 (2026-11-25)

> ขอบเขต: ทดลอง **AuralWeave Studio (Native C++ / WASAPI)**, Filter Presets และ
> หน้าจอ Virtual Driver แบบอ่านสถานะเท่านั้น ไม่ใช่การทดลองติดตั้ง Kernel Virtual Driver

## สถานะที่ยืนยันแล้ว / ยังไม่ได้ยืนยัน

- ใน Repository มี Native C++ audio route, Filters, Filter Presets และ Virtual Driver Manager
- มี Shared PCM user-mode bus, WaveRT/IOCTL kernel source prototype และ mock tests
- ไดรเวอร์ **ยังไม่ได้ลงลายเซ็น, ยังไม่ได้ติดตั้งจริง, ยังไม่ผ่าน kernel-runtime/HLK**
- Windows 10 เคยผ่านชุดทดสอบ user-mode จากการพัฒนาก่อนหน้า
- Windows 11 เป็นเป้าหมายด้าน source/INF compatibility เท่านั้น; ยังไม่ได้ทดสอบ runtime
- GitHub Actions สร้าง ZIP สำหรับ **Studio เท่านั้น** หลังผ่าน build และ smoke tests
  โดยไม่ติดตั้งหรือเผยแพร่ SYS/INF/CAT และไม่ปล่อย driver setup developer helper

## รับไฟล์ทดลองอัตโนมัติ

1. เปิดแท็บ **Actions** ใน Repository `funggier/AuralWeave` แล้วเลือก
   **Windows portable preview (no kernel driver)**.
2. เลือก workflow run ที่มีสถานะ **Success**. ถ้ายังไม่มี run ที่ผ่าน
   ให้ถือว่ายังไม่มีไฟล์ใหม่ที่ผ่านการตรวจสอบ; อย่าใช้ไฟล์จาก run ที่ล้มเหลว
3. ดาวน์โหลด artifact ชื่อ **AuralWeave-Windows-x64-portable-preview**.
4. แตก artifact ZIP แล้วแตกไฟล์ `AuralWeave-v0.3.0-Windows-x64.zip`
   ที่อยู่ภายในไปยังโฟลเดอร์ที่เขียนไฟล์ได้ เช่น `T:\AuralWeave-Test`.
5. เปิด `AuralWeave.exe`. โปรแกรมเป็น Portable ไม่ต้องติดตั้ง
   และไม่ต้องเปลี่ยนระบบลายเซ็นไดรเวอร์ของ Windows

Artifact ของ workflow มีอายุเก็บ 14 วัน (เมื่อ Actions และ artifact upload
ทำงานสำเร็จ). Workflow ไม่ได้สร้าง GitHub Release และไม่ได้รับรองว่า
รุ่นทดลองผ่านการทดสอบเสียงจริงกับฮาร์ดแวร์ของผู้ใช้แล้ว

## ลองใช้ Studio อย่างปลอดภัย

1. ต่อหูฟังก่อนเพื่อหลีกเลี่ยงเสียงหวีดจากไมโครโฟนกับลำโพง
2. เลือก Sound Input เป็น Mic หรือ Line In และเลือก Sound Output เป็นหูฟัง
3. ลดระดับเสียงก่อนกด **START**; Output ที่เพิ่มใหม่อาจเริ่มแบบไม่ Mute
4. ทดสอบ Filter เพิ่ม/ลบ/จัดลำดับ: Bypass, RNNoise, Noise Gate, Limiter
5. ตรวจสอบ **SAVE / LOAD / IMPORT / EXPORT** ของ Filter Presets
6. กด **STOP**, เปลี่ยนอุปกรณ์ แล้วลองเริ่มใหม่
7. คัดลอกโฟลเดอร์ทดลองเป็นอีกโฟลเดอร์หนึ่ง แล้วเปิด Studio สองชุด
   เพื่อเช็กว่าการตั้งค่าแต่ละชุดยังแยกกัน (ไม่ใช่การทดสอบไดรเวอร์)

## สิ่งที่คาดว่าจะเห็นใน Virtual Driver

- เห็นการ์ด **Virtual Input** กับ **Virtual Output** แยกกัน
- ถ้าไม่มีไดรเวอร์ที่ผ่านการรับรอง ระบบอาจแสดงว่าไม่มีอุปกรณ์ และ
  **INSTALL ต้องไม่เปิดใช้งาน**
- หน้าจอไม่มีปุ่ม Uninstall; การถอนอุปกรณ์ในอนาคตเป็นหน้าที่
  Windows Device Manager
- **ห้าม** ติดตั้ง `AuralWeaveVirtualAudio.sys` ต้นแบบที่ยัง unsigned,
  เปิด Test Signing, ปิด Secure Boot หรือบังคับติดตั้งด้วย PnPUtil เพื่อ
  ข้ามข้อจำกัดของรุ่นนี้

## ทดสอบซอร์สโดยไม่ติดตั้ง Driver

ใน PowerShell ที่โฟลเดอร์ Repository บน Windows ที่มี VS C++ และ .NET 8:

```powershell
.\scripts\test-kernel-transport.ps1
.\scripts\build-windows.ps1 -PublishPortable
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-kernel-pcm-client
# เวิร์กเกอร์และตัวผสมเสียงหลายโปรแกรม (ใช้ Mock ไม่ติดตั้ง Driver)
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-kernel-pcm-worker
# ทดสอบการบังคับปิดเจ้าของ Shared PCM โดยช่องอื่นยังทำงานต่อ
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-virtual-bus-recovery
.\scripts\test-multiple-portable-instances.ps1 -SkipBuild
```

ชุดทดสอบนี้พิสูจน์ source/user-mode mock เท่านั้น **ไม่ใช่**
การทดสอบไดรเวอร์ใน Kernel หรือ Windows 11

## ก่อนเปิดให้ทดลอง Virtual Driver จริง

ต้องมีหลักฐานอย่างน้อยดังนี้:

- Windows 10 แยกเครื่อง/VM ผ่านติดตั้งและถอนติดตั้งโดยไม่แก้ค่านโยบายระบบ
- Audio Engine มองเห็น Virtual Input และ Virtual Output เป็นอุปกรณ์จริง
- สตรีม PCM ผ่าน WaveRT + secure kernel control + broker อย่างต่อเนื่อง
- ทดสอบ crash/restart, suspend/resume, hotplug, DRM restrictions และ latency
- ตรวจสอบความปลอดภัยของ broker และขอบเขตสิทธิ์ต่อผู้ใช้
- ได้รับ Microsoft production signing/HLK ตามข้อกำหนด Windows
- Windows 11 ต้องทดสอบจริงแยกต่างหากก่อนอ้างว่ารองรับอย่างสมบูรณ์

เมื่อรายงานปัญหา กรุณาระบุ Windows build, Input/Output ที่ใช้,
Filter ที่เปิด, ขั้นตอนทำให้เกิดปัญหา และข้อความผิดพลาด (ไม่ต้องส่งข้อมูลส่วนตัว)

รายละเอียดของการทดสอบ Worker/Coordinator และข้อจำกัดด้านการตั้งเวลาเสียง
ดู [Kernel PCM Coordinator Checkpoint](KERNEL_PCM_COORDINATOR_CHECKPOINT.md).
