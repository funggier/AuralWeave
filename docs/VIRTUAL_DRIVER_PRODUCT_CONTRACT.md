# AuralWeave Virtual Driver — ข้อกำหนดผลิตภัณฑ์ที่ยืนยันร่วมกัน

**วันที่:** 2026-11-25  
**สถานะ:** ข้อกำหนดเป้าหมายที่ตกลงแล้ว (approved design); **ไม่ใช่ฟังก์ชันไดรเวอร์ที่ใช้งานได้จริงในรุ่นปัจจุบัน**  
**แหล่งหลักสำหรับการตัดสินใจถัดไป:** เอกสารนี้มีลำดับความสำคัญเหนือข้อความเชิงแผนหรือประวัติการทดลองที่ขัดกันในเอกสารเก่า

## 1. เป้าหมายหลัก

ติดตั้ง **AuralWeave Virtual Driver เพียงหนึ่งชุดต่อเครื่อง Windows** โดยชุดหนึ่งประกอบด้วยอุปกรณ์เสียงเสมือนสูงสุด **สอง Endpoints / สองบทบาท**:

| บทบาท | สิ่งที่ Windows แสดง | ผู้ใช้เสียง | จำนวนสูงสุดต่อเครื่อง |
| --- | --- | --- | --- |
| **AuralWeave Virtual Input** | Recording / virtual microphone | แอปใด ๆ ที่เปิดไมโครโฟน เช่น Discord, OBS | **1** |
| **AuralWeave Virtual Output** | Playback / virtual speaker | แอปใด ๆ ที่ส่งเสียงออก เช่น browser, media player, game | **1** |

*หนึ่งชุด* หมายถึง driver family/package ที่จัดการร่วมกัน; โครงสร้างปัจจุบันอาจใช้ **สอง PnP device nodes** (role ละหนึ่ง) ที่ใช้ kernel service / driver package ร่วมกันได้ **ไม่ใช่** เพิ่มหนึ่ง driver ตามจำนวน Studio หรือจำนวนโปรแกรมที่เปิด

### กฎบังคับ

1. **ทุกโปรแกรมของ Windows ใช้งานได้:** ไม่ต้องเป็น AuralWeave ไม่ต้องติดตั้ง extension/plugin หรือรู้จัก IPC ภายในของ AuralWeave แอปทั่วไปค้นหาและเลือก Endpoints ผ่าน **Windows Audio / WASAPI shared-mode** ตามปกติ
2. **เปิดพร้อมกันได้:** Discord และ OBS รับจาก Virtual Input ตัวเดียวกันได้; browser/เกม/media player ส่งเสียงไป Virtual Output ตัวเดียวกันได้พร้อมกัน โดย Windows Audio Engine เป็นผู้รับผิดชอบ session mixing ของ render endpoint และการกระจาย capture streams ตามความสามารถ Shared Mode
3. **หลาย AuralWeave เปิดได้:** Studio Portable จากคนละโฟลเดอร์เปิดและทำงานพร้อมกันได้ โดยแต่ละหน้าต่างคง Filters, routing และ settings ของตน ไม่สร้าง Windows PnP endpoint เพิ่ม; เมื่อมีเสียงส่งเข้าบัสส่วนกลางต้องมีแผนผสมเสียงและแยก listener ต่อราย ไม่แย่ง consumer เดียวกัน
4. **ติดตั้งระดับเครื่อง ไม่ใช่ตามโฟลเดอร์:** ย้าย/คัดลอกโฟลเดอร์ Portable ไม่ต้องติดตั้งใหม่ ทุกหน้าต่างอ่านสถานะเครื่องจาก Windows PnP จริง ไม่ดูแค่ไฟล์ในโฟลเดอร์
5. **ไม่จำเป็นต้องเปิดหน้าต่าง Studio เพื่อให้อุปกรณ์ปรากฏ:** PnP device/driver เป็นของ Windows; ส่วนเส้นทางเชื่อมเสียงสดกับ Studio ต้องมีบริการ/ส่วนประกอบเบื้องหลังที่มี lifecycle ชัดเจน ซึ่งไม่ควรผูกกับหน้าต่างใดหน้าต่างหนึ่ง (ยังต้องพัฒนาและทดสอบ)
6. **โหมด Shared เป็นเป้าหมายหลัก:** ไม่ออกแบบให้ Client รายแรกจับจองทั้งอุปกรณ์หรือห้ามรายอื่นใช้; แอปภายนอกบางตัวอาจขอ Exclusive Mode ตามนโยบาย Windows ได้ จึงต้องออกแบบ/ทดสอบพฤติกรรมนี้อย่างปลอดภัยและไม่อ้างว่าบังคับทุกแอปได้โดยไม่มีข้อยกเว้น
7. **จำนวนจำกัดเฉพาะ PnP endpoints:** ปริมาณ audio sessions / Studio instances เป็นอีกมิติหนึ่ง จำกัดตามทรัพยากรและความปลอดภัยเท่าที่จำเป็น แต่ต้องไม่สับสนกับ 1 Input + 1 Output

## 2. UX ที่ต้องทำจริง (Install-only)

หน้า **Virtual Driver** ของ AuralWeave มีสถานะ **Virtual Input** และ **Virtual Output** แยกให้เห็น แต่มี **การกระทำเกี่ยวกับ driver เพียงการติดตั้ง** เมื่อขาดอุปกรณ์ใดอุปกรณ์หนึ่งเท่านั้น:

- **ไม่พบทั้งคู่:** เสนอการติดตั้งชุด AuralWeave Virtual Driver สำหรับบทบาทที่ขาด; จะเลือกทำเป็นปุ่มหลักปุ่มเดียวที่ติดตั้งทั้งคู่ หรือปุ่มติดตั้งเฉพาะช่องที่ขาดก็ได้ แต่ห้ามทำซ้ำบทบาทที่มีอยู่
- **พบ Input อย่างเดียว:** ไม่เสนอให้ติดตั้ง Input ซ้ำ; เสนอ Install สำหรับ Output ที่ขาดเท่านั้นเมื่อมี signed package ครบ
- **พบ Output อย่างเดียว:** แบบกลับด้านกับ Input
- **พบบทบาททั้งคู่:** แสดงว่าติดตั้งแล้ว ไม่มีปุ่ม Install หรือปุ่มสร้างเพิ่ม
- **พบ legacy, device ที่ติดตั้งไม่สมบูรณ์, ผูก driver ผิด หรืออยู่ระหว่างติดตั้ง:** แสดงสถานะและคำอธิบาย; **ไม่สร้าง device ซ้ำ** และไม่พยายามซ่อม/ถอนติดตั้งอัตโนมัติ
- **ยังไม่มีแพ็กเกจ production ที่พร้อมใช้:** แสดงเหตุผลและ Install เป็น disabled; **ไม่ทำปุ่มหลอก**
- **เปิด AuralWeave สองตัวและกดพร้อมกัน:** installer ต้อง serialize ผ่าน mutex ของการติดตั้งและตรวจ PnP ใหม่ **หลังได้ lock และหลัง UAC**; มีผู้ติดตั้งได้เพียงหนึ่งครั้ง ไม่สร้างสำเนาแข่งกัน
- **หน้าจอเรียบง่าย:** status / present/absent / readiness / install เฉพาะที่จำเป็น; **ไม่มี Uninstall, Remove, Repair, Driver slot '+' หรือรายการสร้าง device จำนวนมาก**

### การถอนการติดตั้งทำผ่าน Windows เท่านั้น

AuralWeave Studio **ไม่มีคำสั่งถอน driver**. ผู้ใช้ลบอุปกรณ์ผ่าน **Windows Device Manager → Uninstall device** ได้ตามกลไกของ Windows ส่วนการถอน package จาก Driver Store เป็นเรื่องแยกต่างหาก

หากต้องการให้ปรากฏใน **Windows Settings → Apps / Installed apps (หรือ Apps & Features)** เพื่อกด Uninstall แบบโปรแกรมทั่วไป จะต้องพัฒนา **signed driver installer ที่จดทะเบียน Windows uninstall entry และมี uninstaller ของตนเองจริง**; การติดตั้งเพียง INF / SYS / CAT **ไม่ได้สร้างรายการใน Installed Apps อัตโนมัติ**. นี่เป็น **เป้าหมายด้านบรรจุภัณฑ์** และยังไม่ได้ทำ/ยังไม่ได้ทดสอบ ไม่สัญญาว่าปัจจุบันใช้ได้

## 3. Audio routing เป้าหมาย

```text
Windows application A ----\
Windows application B -----+--> Windows shared-mode render sessions
Windows application C ----/                 |
                               AuralWeave Virtual Output
                                          |
                              Windows virtual render/driver
                                          |
                         Trusted audio bridge / routing service
                                          |
                          AuralWeave Native routing + Filters
                                          |
                     real speakers / outputs / optional loopback

Studio instance A post-FX ----\
Studio instance B post-FX -----+--> shared virtual microphone mix
Other authorized source ------/                  |
                                       AuralWeave Virtual Input
                                                 |
                                   Windows recording endpoint
                                           /           \
                                  Discord mic        OBS mic
```

Windows render-session mixing/fanout และการรวม **AuralWeave Studio source หลายตัว** เป็นคนละชั้น ไม่ควรทำซ้ำซ้อนโดยไม่จำเป็น การเปิดไมโครโฟนจากหลายแอปต้องให้แต่ละแอปมี capture stream ของตัวเอง เสียงของแต่ละแอปต้องไม่ไปอ่าน SPSC queue เดียวกันแข่งกัน

เส้นทาง PCM ระดับ Kernel, ความปลอดภัยของ service, audio clock / drift / latency, hotplug, mute/silence และ shutdown ต้องกำหนดให้ชัดก่อนจัดจำหน่าย

## 4. PnP identity และ policy

- Current intended identities: `ROOT\AURALWEAVE_VINPUT_0001` และ `ROOT\AURALWEAVE_VOUTPUT_0001`
- อนุญาต **อย่างละหนึ่งต่อเครื่อง**; อาจใช้ kernel service และ driver package ร่วมกัน
- ตรวจ **PnP inventory จาก Windows ทั้งเครื่อง**: device instance / hardware ID / class / service / current + legacy + partially installed; ระวัง orphaned และ duplicate ก่อนเรียกติดตั้ง
- Installation mutex มีไว้ **เฉพาะขั้นตอน admin / PnP mutation** ไม่ล็อกค้างตลอดการใช้งานเสียง
- ห้ามใช้ Process ID, random mapping name หรือ advisory lease จาก Mock Broker เป็นสิทธิ์อนุญาตการใช้ Windows audio endpoint. ต้องแยก mock internal IPC จาก Windows audio clients ทั่วไป
- ไม่สั่งถอน/เปลี่ยน boot security, test signing, Secure Boot หรือบังคับติดตั้ง unsigned driver

## 5. สถานะทางวิศวกรรมที่ยืนยันแล้ว

**มีโค้ดและทดสอบแล้ว:** Native C++ WASAPI routing/Filters, Portable WPF Studio, SetupAPI read-only discovery, จำกัด PnP identity ต่อ role, test-only Shared PCM multi-client mixer/fanout, Mock Broker (non-elevated same-user), cross-folder Windows 10 tests, kernel WaveRT/PCM/IOCTL prototype แบบ compile/mock.

**ยังไม่เสร็จ:** Endpoints ที่ Windows เห็นและเปิดใช้จริงจาก signed driver, connected production kernel↔service↔Native core audio transport, signed installer, Windows uninstall entry, actual multi-application shared-mode device tests, Windows 11 runtime QA.

**สำคัญ:** การเพิ่ม owner-process PID check ใน Mock Broker v0.3.0 ป้องกันการลงทะเบียน shared-memory mapping ของคนอื่นภายใน broker ทดลองเท่านั้น **ไม่ใช่** ปฏิเสธ Windows apps อื่นใช้ driver. อย่าใช้ check นี้เป็นเกณฑ์อนุญาตให้อุปกรณ์เสียงเปิดได้; และไม่ใช่ security boundary ที่ใช้ได้กับ privileged service

## 6. เกณฑ์รับงานที่ต้องพิสูจน์ก่อนเปิด Install จริง

1. เปิด Windows Sound settings เห็น **Input และ Output อย่างละตัวเดียว** หลัง install; reboot แล้วยังอยู่; เปิดจาก Studio คนละโฟลเดอร์ก็แสดงสถานะเหมือนกัน
2. เปิด Discord + OBS พร้อมกัน: ทั้งสองอ่าน **Virtual Input ตัวเดียวกัน** ได้โดยไม่แย่ง stream; การปิดแอปหนึ่งไม่ทำให้อีกแอปหลุด
3. เปิด browser + เกม/media player พร้อมกัน: ทั้งสองส่งไป **Virtual Output ตัวเดียวกัน** ได้; เสียงผสมตาม Windows Shared Mode ไม่มี exclusive capture จาก Studio
4. เปิด Studio 2 ชุดจากต่างโฟลเดอร์: routing/filters เป็นของตัวเอง ไม่เกิด device node ใหม่ สามารถร่วมส่งเสียงเข้า virtual mic (ตาม mixing policy ที่ตั้งไว้) และรับ output ได้พร้อมกัน
5. กด Install จาก Studio 2 ชุดแข่งกัน: หลัง UAC/recheck สร้าง **สูงสุดหนึ่ง device ต่อ role**; ถ้ามีอยู่แล้วไม่ทำซ้ำ
6. ติดตั้งแล้ว: ไม่เสนอ Install ซ้ำและไม่มี Uninstall ใน Studio; Windows Device Manager ลบได้ และ Windows Installed Apps ลบได้ **เฉพาะเมื่อมี registered uninstaller ที่ผ่านการทดสอบแล้ว**
7. บริการส่วนกลางคงอยู่ตาม lifecycle ที่กำหนด ไม่ต้องเปิด Studio เพื่อ **มองเห็น** endpoint; source stream หยุด/แอปปิด/driver restart → กลับสภาพปลอดภัย ไม่เสียงค้าง/ลูป feedback
8. Test signed packaging/WDK/HLK ตามข้อกำหนด Windows 10 x64 จริง; Windows 11 ตรวจแยกจากสมมติฐาน INF
9. ระหว่างยังไม่มี signed production package ต้อง **คง Install disabled** และ Studio routing ที่ไม่ใช้ driver ต้องทำงานปกติ

## 7. เอกสารที่เกี่ยวข้อง

- [Roadmap](ROADMAP.md)
- [Virtual Driver overview](VIRTUAL_AUDIO_DRIVER.md)
- [Install-only workflow](VIRTUAL_DRIVER_INSTALLATION.md)
- [Multiple Portable copies](PORTABLE_MULTI_INSTANCE_CONTROL.md)
- [Windows 10/11 support matrix](VIRTUAL_DRIVER_WINDOWS10_WINDOWS11.md)
- [Mock Broker origin-PID checkpoint](BROKER_PROCESS_ORIGIN_CHECKPOINT.md)

ข้อกำหนดนี้สะท้อนการยืนยันของผู้ใช้โดยตรงหลังจากทบทวนความเข้าใจเรื่อง shared Windows Virtual Driver และแก้ไขความสับสนระหว่าง **device count**, **multi-client audio streams** และ **test-only owner binding**.
