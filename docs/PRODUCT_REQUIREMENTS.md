# Product Requirements — AuralWeave

**สถานะ:** Initial specification (2026-11-24)

## วัตถุประสงค์

โปรแกรม Audio Router สำหรับ Windows รับเสียงจาก Microphone, Line In, USB Audio, และ Virtual Device ในอนาคต ประมวลผลแบบ Real-time แล้วส่งออกหลายปลายทางได้พร้อมกัน โดยการตั้งค่าแต่ละ Route เป็นอิสระ

## In scope

1. สร้าง, ตั้งชื่อ, เปิด/ปิด, Start/Stop, ลบ Audio Route; วางแผนหลาย Route พร้อมกัน
2. แต่ละ Route เลือก Input Source หนึ่งรายการ (รายการอุปกรณ์ที่ตรวจพบจริงเท่านั้น)
3. เพิ่ม Output แบบ Dropdown + ปุ่ม `+` และลบแต่ละ Output ด้วย `−`
4. Output ที่ถูกเลือกใน Route นั้นถูกซ่อนจาก dropdown ตัวเลือกที่เหลือ; ไม่ซ้ำภายใน Route เดียว
5. หากไม่มีอุปกรณ์เหลือ ตัวเลือกเพิ่มว่างและปุ่ม `+` disabled; เมื่อลบให้คืนรายการทันที
6. Noise Reduction Dropdown: Bypass, RNNoise, และตัวเลือกใหม่ในอนาคต
7. Effects Chain เป็นลำดับของ **effect instances**; เพิ่มชนิดเดิมซ้ำได้ ไม่มีการ deduplicate ตามประเภท
8. แต่ละ effect instance มี ID, type, enabled, order, config เฉพาะตัว; รองรับย้ายขึ้น/ลง
9. Bypass ราย Effect, Bypass ของ Noise Reduction, Bypass ทั้ง chain แยกกัน
10. Output แต่ละตัวมี gain/mute/status ของตนเอง; Output fan-out ใช้ข้อมูลหลังประมวลผลร่วมกันก่อนการแปลง format เฉพาะปลายทาง
11. มีระดับเสียงและสถานะจริงของ Input/Processing/Output; Latency/Underrun แสดงจากข้อมูลจริง ไม่ใช่ค่าจำลอง
12. เลือกค่าที่เหมาะสมโดยอัตโนมัติเป็นค่าเริ่มต้น พร้อมโหมด manual override
13. Hotplug: เมื่ออุปกรณ์หาย ให้แจ้ง unavailable และคง identity/settings เดิม ไม่สลับอุปกรณ์อื่นเงียบๆ
14. บันทึก preset ภายในโฟลเดอร์ portable; รองรับย้ายโปรแกรมไปไดรฟ์อื่น

## Non-goals for initial milestone

- ยังไม่พัฒนา kernel virtual driver ในเฟสแรก
- ยังไม่รับประกัน true bit-perfect passthrough, exclusive mode หรือ zero latency
- ยังไม่บังคับว่าต้องใช้ AI processing; ให้โหลดเป็นโมดูลภายหลัง
- ยังไม่ระบุรูปแบบ commercial distribution ของ third-party model/plugin

## Quality attributes

- เสถียรภาพของ audio callback สำคัญกว่า UI refresh rate
- ไม่มี blocking disk/network/allocation หนักใน real-time callback
- การเปลี่ยน chain และ device จะต้องไม่ทำให้ Route อื่นค้าง
- จำกัด CPU/RAM, buffer growth และ queue sizes; ไม่ให้ memory โตไร้ขีดจำกัด
- การหยุดต้องหยุดจริง; restart route ได้; เสียงไม่ feedback โดยอัตโนมัติ

## Acceptance

ดู [TEST_PLAN.md](TEST_PLAN.md) สำหรับผลที่ต้องทดสอบจริง
