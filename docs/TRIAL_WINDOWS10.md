# ทดลอง AuralWeave Portable บน Windows 10

1. เปิด [Actions](https://github.com/funggier/AuralWeave/actions) แล้วเลือก **Windows portable preview** ที่ผ่านการตรวจ
2. ดาวน์โหลด artifact **AuralWeave-Windows-x64-portable-preview** แล้วแตก ZIP ข้างในไปยังโฟลเดอร์ที่เขียนได้ เช่น `T:\AuralWeave-Test`
3. เปิด `AuralWeave.exe` แล้วเลือก **Sound Input**, ตั้ง **Filters** และเพิ่ม **Sound Output**
4. ใช้หูฟังหรือระดับเสียงต่ำก่อนกด **START** เพราะเอาต์พุตที่เพิ่มใหม่เริ่มแบบไม่ Mute
5. ทดลอง **SAVE / LOAD / IMPORT / EXPORT** ของ Filter Presets แล้วกด **STOP** ก่อนเปลี่ยนอุปกรณ์

Artifact ของ workflow มีอายุเก็บ 14 วัน ส่วนแพ็กเกจที่เผยแพร่เป็นรุ่นอยู่หน้า [GitHub Releases](https://github.com/funggier/AuralWeave/releases)

## ส่งหรือรับเสียงจากโปรแกรมอื่น

ติดตั้ง VB-CABLE จาก VB-Audio แล้วเลือก `CABLE Input` เป็น **Sound Output** ของ AuralWeave และเลือก `CABLE Output` เป็นไมโครโฟนใน Discord หรือ OBS หากต้องการรับเสียงเข้า AuralWeave ให้เลือก `CABLE Output` เป็น **Sound Input** ดูวิธีต่อสองทิศทางและทดสอบเสียงใน [คู่มือ VB-CABLE](VB_CABLE.md)
