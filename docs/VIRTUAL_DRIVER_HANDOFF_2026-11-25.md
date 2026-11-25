# AuralWeave — Handoff: Shared System Virtual Driver (2026-11-25)

## ความคืบหน้าหลังยืนยันข้อกำหนด (2026-11-25)

- ตรวจ live `main` บน Windows 10 22H2 แล้วพบว่าเริ่มจาก `2b920b4` ตรงกับ GitHub และ worktree ว่าง ดู commit ล่าสุดจาก Git ก่อนทำงานต่อ
- เสร็จ P0 ใน Roadmap. [ADR 0004](adr/0004-shared-windows-virtual-audio.md) กำหนด Windows Audio Engine, WaveRT, Native Studio และ trusted bridge รวมถึง clock, consent และ service identity
- Mutex การติดตั้งมีผู้ใช้เฉพาะ Driver Setup ที่ผ่าน elevation แล้ว Setup จะตรวจ PnP ภายใต้ mutex ก่อนตัดสินใจสร้างอุปกรณ์ Studio แยกสถานะ duplicate, unbound, stopped และ Windows problem code ออกจากสถานะติดตั้งพร้อมใช้งาน
- บน Windows 10 ผ่าน build/diagnostics รวม Core 32 tests, read-only SetupAPI (พบ 0 AuralWeave nodes), mutex และ PCM ข้ามโฟลเดอร์, Mock Broker และ Native WASAPI แบบ mute ไปยังสอง output. WDK build, InfVerif และ Inf2Cat signability ผ่านแบบ compile-only
- ขั้นถัดไปตาม Roadmap คือ P1 ทดสอบการเปิด/ปิดแต่ละบทบาทแยกกันและเตรียม timing/cancellation สำหรับ signed VM. ยังไม่มีแพ็กเกจที่ลงลายเซ็นและการตรวจ Hyper-V จาก session นี้ถูกปฏิเสธสิทธิ์. ปุ่ม Install คง disabled และระบบทดสอบหลักยังไม่มี AuralWeave PnP nodes

รายละเอียดด้านล่างเก็บเป็นบันทึกจากรอบเอกสารก่อนหน้า


## เป้าหมายที่ยืนยันแล้ว

**หนึ่งชุดของ AuralWeave Virtual Driver ต่อเครื่อง** มี **Virtual Input (recording / virtual mic) ไม่เกินหนึ่ง endpoint และ Virtual Output (render / virtual speaker) ไม่เกินหนึ่ง endpoint**. โปรแกรมทั่วไปของ Windows ทุกตัวที่ใช้ Shared Mode ต้องสามารถเลือก endpoint เหล่านี้จากระบบเสียง Windows ได้พร้อมกัน ไม่ต้องเป็น AuralWeave. เปิด AuralWeave Portable จากหลายโฟลเดอร์พร้อมกันได้ โดยไม่มีการเพิ่มไดรเวอร์หรือแย่งสิทธิ์กัน

**หน้าจอ Virtual Driver ใน Studio: ตรวจ PnP ทั้งเครื่อง แสดงสถานะและเสนอเฉพาะ Install ของบทบาทที่ยังไม่มีเท่านั้น; ไม่มี Uninstall/Remove/Repair.** Windows Device Manager เป็นทางถอนพื้นฐาน และหากต้องการถอนจาก Apps & Features / Installed Apps ต้องมีการลงทะเบียน uninstaller อย่างถูกต้องในภายหลัง (INF เฉย ๆ ไม่สร้างให้). อย่าพัฒนาโค้ดต่อโดยยึดข้อสมมติว่าโปรแกรมหนึ่งต้องเป็นเจ้าของแต่เพียงผู้เดียว

รายละเอียดบังคับอยู่ที่ [VIRTUAL_DRIVER_PRODUCT_CONTRACT.md](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md). อ่านก่อนแก้โค้ด

## สถานะ Repository ณ เวลารับช่วง

- เครื่องพัฒนา: Windows 10 Pro 22H2 x64 (Windows 11 **ยังไม่ได้ทดสอบจริง**)
- Local: `T:\Space\Projects\ProjectsProgram\AuralWeave`
- GitHub: https://github.com/funggier/AuralWeave (public, branch `main`)
- Before this docs-only clarification, source HEAD: `db75cfef70487e0c832294c0d26caf325816fb18` (Mock Broker v0.3.0 process-origin PID check). ตรวจ HEAD/branch/remote/working tree **ใหม่ทุกครั้ง** เพราะหลังเอกสารอัปเดตจะมี Commit ใหม่
- Licence: MIT for original AuralWeave code; kernel sample portions derived from Microsoft MS-PL must retain their separate licence/attribution
- Studio release tag: `v0.3.0`; Studio ZIP **ไม่มี unsigned kernel driver** และไม่เปิดปุ่ม Install
- CI (ก่อน docs-only change): https://github.com/funggier/AuralWeave/actions/runs/45631421586 — Success, มี Studio Portable preview และ separate Mock Broker testing artifact
- Current documentation pass: **แก้ไขเฉพาะเอกสารและแผน ไม่ได้พัฒนาตัว Kernel Driver/Installer เพิ่ม**. อย่าอ้างว่าพร้อมติดตั้ง

## สิ่งที่มีอยู่จริงจากการทดสอบก่อนหน้า

1. Native C++ low-latency WASAPI Studio + Filters, physical multi-output, WPF control, Portable ZIP
2. One-role-one-PnP-ID design, Windows SetupAPI read-only detection and fail-closed Install เมื่อ package ไม่ครบ
3. SharedVirtualAudioBus test-only: mixing multiple independent contributor SPSC rings and independent listener fanout; Windows 10 two-Portable-folder/cross-process tests ผ่าน
4. Windows WDK compile-only unsigned AuralWeaveVirtualAudio.sys prototype: WaveRT internal PCM staging and WDM control IOCTL code; **ไม่ใช่ endpoint ที่ติดตั้งใช้งานจริง**
5. Non-elevated, same-user Mock Broker v0.3.0 with protocol-v3 pipe, native PCM mapping-v2, heartbeat, hard-restart and process-PID admission tests passed; **แยกจาก Windows audio endpoint policy**
6. DriverSetup helper compile/read-only; signature/certificate pin ยังไม่พร้อม; ไม่มี signed production installer/registered Windows uninstaller

## สิ่งที่ยังไม่ได้พิสูจน์ / อย่าเหมารวมว่าเสร็จ

- Virtual Input และ Output ที่ Windows แสดงหลัง **signed driver install** และใช้งานผ่าน WASAPI จริง
- Windows Audio Engine shared-mode ในอุปกรณ์ใหม่พร้อมกันจาก **Discord/OBS/browser/เกม**; multi-Studio bridge ไปยัง endpoints จริง
- ระบบ broker/service ที่ควบคุมสิทธิ์อย่างถูกต้องและทำงานได้โดยไม่ต้องเปิด Studio (ห้ามนำ mock PID comparison มาใช้เป็น production authorization)
- PCM transfer kernel↔native, latency/drift, restart, hotplug, silence-on-loss, security isolation
- การติดตั้ง/ถอนด้วย signed package และ Windows Apps uninstaller entry
- Windows 11 runtime compatibility, HLK/signing, release gates

## ลำดับพัฒนาถัดไป

**P0 — ตรวจโค้ดจริงและสัญญาระบบ:** `git status` / origin; อ่าน contract, Driver UI, PnP discovery, current setup helper และ virtual audio kernel. ตรวจว่ามีข้อห้าม shared clients หรือ slot lock ค้างใน active path หรือไม่. แยก owner-PID check ของ Mock Broker ออกจาก capability ของ Windows endpoint จริง

**P1 — กำหนด production architecture:** per-machine 1 input + 1 output; Windows shared-mode sessions; external Windows apps ใช้ endpoint ได้อิสระ; service lifecycle แยกจาก Studio; multi-Studio contributions/mix/fanout; secure kernel boundary; audio clock. เขียน ADR เส้นทาง PCM/การกำหนดสิทธิ์ก่อนลงมือส่วน privileged

**P2 — เชื่อม test-only WaveRT ↔ trusted user-mode bridge ใน VM:** ตรวจ stream format/clock/buffers/IOCTL, two-role concurrent device instances, cancellation, crash cleanup, zero stale PCM; ห้ามติดตั้ง unsigned prototype บน host จริง

**P3 — ทำชุด Signed Driver และ Install-only:** one package, two role IDs, ตรวจ PnP race หลัง elevation, installer mutex, no duplicate. หากต้องการ uninstall ใน Installed Apps ให้สร้าง installer-registered uninstaller ที่แยกจาก Studio, validate Windows Device Manager and package cleanup

**P4 — QA ของอุปกรณ์จริง:** 2 Windows apps input, 2 Windows apps output, 2 Portable Studios cross-folder, install races, reboot/device restore, Driver signing/HLK, latency and hotplug; ตรวจ Win10 ก่อน Win11

**P5 — เปิด Install ใน Studio เฉพาะหลัง P2–P4 ผ่าน:** ไม่มี Remove/Repair ใน Studio, ไม่มี auto-install เมื่อเปิดโปรแกรม, no downgrade/security bypass, Studio physical routing ยังเปิดได้โดยไม่ต้องมี virtual driver

## คำสั่งอ่านสถานะและทดสอบเมื่อกลับมาทำโค้ด

```powershell
Set-Location 'T:\Space\Projects\ProjectsProgram\AuralWeave'
git status -sb
git log -1 --oneline
git ls-remote origin refs/heads/main
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
# test-only broker and Native PCM; ไม่ใช่ signed driver test
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-local-broker-ownership
```

**ข้อห้าม:** ไม่ติดตั้ง unsigned `.sys` บน Windows 10 เครื่องหลัก, ไม่เปิด Test Signing/ปิด Secure Boot, ไม่แก้/ลบ PnP device อื่น, ไม่สร้าง device เพิ่มตามจำนวนโปรแกรม, ไม่เพิ่มปุ่ม Uninstall กลับเข้า Studio และไม่กล่าวอ้าง Windows 11 tested หรือ kernel driver production-ready โดยไม่มีผลทดสอบจริง

## เงื่อนไข Done สำหรับ docs-only handoff รอบนี้

- Canonical contract, UI/install and shared multi-client design, Roadmap, test acceptance and handoff ถูกปรับให้สอดคล้อง
- เอกสารเก่าที่มีข้อความเรื่อง removal UI / multi-instance exclusive ถูกแทนหรือทำเครื่องหมาย historical
- การเปลี่ยนแปลงเป็น Markdown เท่านั้น ไม่มีสิทธิ์ kernel/driver/PnP mutation
- Commit/Push main สำเร็จ และรายงาน SHA กับสถานะ repository (ให้ตรวจจากเครื่องหลังบันทึก)
