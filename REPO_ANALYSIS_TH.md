# สรุปการวิเคราะห์รีโป FlintsLabs.D365.ODataClient

**ภาพรวม**
- ไลบรารี .NET สำหรับเรียก OData ของ Microsoft Dynamics 365 F&O และ Dataverse แบบ fluent + LINQ พร้อม CRUD, expand, orderby, cross-company และ multi-auth (Azure AD/ADFS)
- Target frameworks: `net8.0`, `net10.0`
- โค้ดหลักอยู่ใน `FlintsLabs.D365.ODataClient/Services/D365Query.cs`, `FlintsLabs.D365.ODataClient/Services/D365Service.cs`, `FlintsLabs.D365.ODataClient/Extensions/ServiceCollectionExtensions.cs`, `FlintsLabs.D365.ODataClient/Services/D365AccessTokenProvider.cs`

**ประเด็นความเสี่ยงสูง**
- Log header รวม `Authorization` ทำให้มีโอกาสรั่ว token ใน log
- ปิดการตรวจสอบ TLS certificate แบบยอมรับทุกใบ (เสี่ยง MITM) ทั้งตัว client และ ADFS token call
- `D365Service` retry 401 แบบ recursion แต่ไม่ได้ refresh token จริง อาจวนลูป

**ประเด็นคุณภาพระดับกลาง**
- `D365Service` สร้าง URL ไม่ใส่ `?` ให้ถูกต้อง อาจทำให้ query ผิดรูปแบบ
- Client-side `StartsWith` ทำงานผิด (เทียบเท่า equals) และ `EndsWith` อาจ throw เมื่อ null
- String ใน filter ไม่ escape `'` ทำให้ OData พังในบางค่า
- CI ใช้ .NET 8 แต่ target มี `net10.0` อาจ build/publish ล้มใน GitHub Actions

**ประเด็นระดับต่ำ**
- `D365ServiceFactory` สร้าง token provider ใหม่ทุกครั้ง ทำให้ cache token หาย
- `TryParseD365ErrorMessage` อาจตีความ error เป็นไม่ error
- `OrderBy(string,bool)` อาจสร้าง `$orderby` ซ้อน

**ข้อเสนอแนะลำดับถัดไป**
- ปิดการ log `Authorization` หรือ redact ค่าออก
- ทำให้ TLS bypass เป็น opt-in เท่านั้น
- เลือกให้ชัดว่าจะซ่อม `D365Service` หรือ deprecate และให้ใช้ `D365Query<T>` เป็นหลัก
- ปรับ expression evaluator/visitor ให้รองรับ edge case และ escape string
- ปรับ CI ให้รองรับ `net10.0` หรือปรับ target ให้สอดคล้องกับ tooling
