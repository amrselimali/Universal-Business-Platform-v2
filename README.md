# Universal Business Platform — منصة الأعمال السحابية

> مشروع مستقل بالكامل، مبني من الصفر بـ **C# / .NET 10** و**PostgreSQL**،
> ولا يعتمد على تطبيق React القديم في أي شيء. قاعدة البيانات هي المصدر الحقيقي والوحيد للبيانات،
> وكل الشاشات والصلاحيات تُفحص على السيرفر.
>
> 📘 **دليل المستخدم الشامل (كل شاشة وزر: ماذا يسجل وإلى أين ينتقل):** [`docs/USER-MANUAL-AR.md`](docs/USER-MANUAL-AR.md)
> — أو من داخل التطبيق بعد منح صلاحية `USER_MANUAL/VIEW`: **النظام → دليل المستخدم** (`/system/user-manual`)، ويُولَّد آليًا من الكود والجداول عند الضغط على **تحديث الدليل**.

---

## 1. الهوية والستاك

| البند | القيمة |
|---|---|
| الاسم | Universal Business Platform |
| الواجهة | Blazor Server (Interactive Server) + MudBlazor 9 |
| اللغة | C# 13 / .NET 10 |
| قاعدة البيانات | PostgreSQL 17 (متوافق مع 15+) |
| الوصول للبيانات | Dapper (استعلامات SQL صريحة) |
| الترحيلات | DbUp — سكريبتات SQL مرقمة داخل المشروع |
| المصادقة | Cookie للواجهة + JWT قصير للـ API + Refresh Token في HttpOnly Cookie |
| السجلات | Serilog (Console + ملفات يومية) مع Trace ID |
| اللغات | عربي/إنجليزي مع RTL/LTR تلقائي |
| العملات | متعددة مع عملة أساسية واحدة |
| الفروع/المخازن | متعدد + ربط مرن + عزل على السيرفر |

```
Universal-Business-Platform-v2/
├── src/
│   ├── Ubp.Core/            النماذج، الأوامر، الأدوار، ترقيم الصفحات
│   ├── Ubp.Infrastructure/  Dapper + الترحيلات + الأمان + التخزين
│   ├── Ubp.Application/     الخدمات وقواعد العمل والصلاحيات
│   └── Ubp.Web/             Blazor + MudBlazor + الـ endpoints + Serilog
├── tests/Ubp.Tests/         اختبارات آلية متكاملة على PostgreSQL
├── db/                      مساحة توثيق/سكريبتات مساعدة
├── docs/                    التوثيق العربي
├── Dockerfile
└── docker-compose.yml
```

---

## 2. التشغيل السريع (محلياً)

### المتطلبات
- .NET 10 SDK
- PostgreSQL 15+ (أو Docker)
- لا يلزم Node.js

### الخطوات
```bash
# 1) قاعدة البيانات
docker compose up -d postgres
# أو استخدم PostgreSQL محلياً وأنشئ قواعد: ubp و ubp_qa

# 2) الإعدادات
#    قيم التطوير المحلي موجودة في src/Ubp.Web/appsettings.Development.json
#    (appsettings.json نفسه لا يحمل أي سر أو قيمة اتصال).
#    Database:ConnectionString            → مستخدم التطبيق (بدون DDL)
#    Database:MigrationConnectionString   → مستخدم DDL للترحيلات (اتصال مباشر لا pooler)
#    Database:QaConnectionString          → قاعدة QA مستقلة
#    Jwt:Key                              → 32 حرفاً على الأقل (64+ في الإنتاج)
#    Bootstrap:SuperAdminPassword         → إلزامية في الإنتاج (لا قيمة افتراضية)

# 3) التشغيل
dotnet run --project src/Ubp.Web
```

الرابط: **http://localhost:5080**

بيانات الدخول الأولى (بيئة التطوير فقط):
| البند | القيمة |
|---|---|
| اسم المستخدم | `admin` |
| كلمة المرور | `Dev-Admin!2026` (من `Bootstrap:SuperAdminPassword` في `appsettings.Development.json`) |

> ⚠️ **لا تُشحن أي كلمة مرور افتراضية**: في بيئة `Production` يفشل الإقلاع إن كانت
> `Bootstrap:SuperAdminPassword` أو `Jwt:Key` أو روابط قاعدة البيانات ناقصة أو ضعيفة
> (`IValidateOptions` + `ValidateOnStart`). غيّر كلمة مرور المدير بعد أول دخول.
> عند أول تشغيل تُطبَّق الترحيلات تلقائياً في التطوير فقط (يمكن تعطيل ذلك بـ `Database:AutoMigrate=false`).
> فحص الصحة: `/health/live` (بلا قاعدة بيانات) و`/api/v1/system/health` (يرجع 503 عند تعذر الاتصال
> ولا يكشف أي تفاصيل عن السيرفر أو القاعدة).

### متغيرات البيئة في الإنتاج
```bash
Database__ConnectionString="Host=...;Database=ubp;Username=ubp_app;Password=..."
Database__MigrationConnectionString="Host=...;Database=ubp;Username=postgres;Password=..."
Database__QaConnectionString="Host=...;Database=ubp_qa;Username=postgres;Password=..."
Database__AutoMigrate=false
Jwt__Key="<64+ random chars>"
Bootstrap__SuperAdminPassword="<strong password>"
DataProtection__KeyRingPath="/app/keys"
```

> للتطوير المحلي بدل متغيرات البيئة يمكن استخدام `dotnet user-secrets --project src/Ubp.Web set Jwt:Key "..."`،
> فمشروع `Ubp.Web` يحمل `UserSecretsId` ولا يُقرأ أي سر من ملفات المستودع.

---

## 3. قاعدة البيانات والترحيلات

### عند فشل البناء: `MSB3027 / MSB3021 … file is locked by Ubp.Web`
هذا يعني أن **التطبيق ما زال يعمل** (أو أن Visual Studio يحتجز ملفات `bin\Debug`)، فمجلّد
`src/Ubp.Web/bin/Debug/net10.0` غير قابل للكتابة. ليس خطأ في الكود.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\stop-app.ps1 -Rebuild
```
أو يدوياً:
- في Visual Studio: **Shift+F5** (Stop Debugging) — أو أغلق VS تماماً.
- في الطرفية: `Get-Process Ubp.Web | Stop-Process -Force` ثم `dotnet build-server shutdown`.
- وأعد البناء: `dotnet build UniversalBusinessPlatform.slnx`.
- لتشغيل أوامر CLI (`--check-db` / `--migrate`) بينما VS مغلق: استخدم `dotnet run --no-build`
  إن كنت قد بنيت للتو، وإلا فسيحاول البناء ويكتشف القفل.

### عند ظهور `SocketException: actively refused`
معناها: **لا يوجد PostgreSQL يستمع على العنوان المطلوب**. أسرع حل جاهز:

```powershell
# Windows
powershell -ExecutionPolicy Bypass -File scripts\dev-db.ps1
```
```bash
# Linux / macOS
bash scripts/dev-db.sh
```
السكربت يفحص المنفذ، ويشغّل حاوية `postgres:17` على `127.0.0.1:5432` (أو خدمة PostgreSQL محلية)،
ثم ينشئ قاعدة `ubp` و`ubp_qa` — ويعطيك الأمر التالي مباشرة.

وإن أردت يدوياً، الخطوات بالترتيب:

1. هل الحاوية تعمل؟ `docker compose ps` ثم `docker compose up -d postgres`.
   ⚠️ **قبل أول أمر compose** يجب أن يوجد ملف `.env` (انسخه: `cp .env.example .env`
   أو `Copy-Item .env.example .env` في PowerShell)، لأن `docker-compose.yml` يشترط
   `JWT_KEY` و`SUPER_ADMIN_PASSWORD` لخدمة التطبيق — وغيابهما يوقف **كل** أوامر compose حتى
   `up -d postgres`.
2. هل Docker Desktop يعمل أصلاً؟ (على Windows: أيقونة الحوت في شريط المهام يجب ألا تكون متوقفة).
3. هل المنفذ مفتوح على جهازك؟
   - Linux/macOS: `nc -vz 127.0.0.1 5432`
   - Windows: `Test-NetConnection 127.0.0.1 -Port 5432`
4. **استخدم `Host=127.0.0.1` لا `localhost`**: الـ compose ينشر `127.0.0.1:5432` (IPv4 فقط)،
   و`localhost` عند .NET على Windows قد يُحلّ إلى `::1` فيُرفض الاتصال.
5. اسم المضيف `postgres` يعمل **داخل** شبكة compose فقط، لا من Visual Studio على الجهاز المضيف.
6. لو كان عندك PostgreSQL مثبّت على الجهاز ويحتل المنفذ 5432، ستفشل الحاوية بـ
   `port is already allocated` — أوقف الخدمة (`Get-Service *postgres*` → `Stop-Service`) أو غيّر منفذ النشر.

**بديل سريع بلا `.env` ولا compose** (حاوية قاعدة فقط):
```bash
docker run -d --name ubp-postgres \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=ubp \
  -p 127.0.0.1:5432:5432 postgres:17
docker exec ubp-postgres createdb -U postgres ubp_qa
```

عند فشل الاتصال يرجع الـ runner رسالة واضحة بالشكل:
`MIGRATION_CONNECTION_FAILED target=127.0.0.1:5432/ubp hint=… detail=…` (بلا أي كلمة مرور).

- كل تغيير في الهيكل = **سكريبت جديد مرقّم** داخل
  `src/Ubp.Infrastructure/Migrations/Scripts/` (مثل `0011_add_x.sql`). لا يُعدَّل سكريبت بعد تنفيذه.
- التنفيذ عبر **DbUp** داخل معاملة لكل سكريبت، مع قفل Advisory يمنع التنفيذ المتوازي.
- نفس الـ runner يُستخدم في:
  1. بدء التطبيق (حسب الإعداد).
  2. زر «تحديث / تهيئة قاعدة البيانات» في شاشة قاعدة البيانات (Super Admin).
  3. CI/CD.
- مستخدم التطبيق `ubp_app` لا يملك DDL؛ الترحيلات تستخدم اتصالاً منفصلاً بصلاحية أعلى.
- **مسار النشر الموصى به**: `dotnet Ubp.Web.dll --migrate` يطبّق الترحيلات ثم يخرج برمز حالة
  (`MIGRATIONS_OK applied=N total=N` → 0، أو `MIGRATIONS_FAILED error=…` → 1). استخدمه في خط النشر
  وأبقِ `Database:AutoMigrate=false` في الإنتاج.
- شاشة `/admin/system-health` تعرض: حالة القاعدة، الترحيلات **المعلّقة**، الاستعلامات الأطول من 30
  ثانية، وسجل النسخ الاحتياطي (يُكتب من وظيفة cron خارجية عبر `select fn_record_backup(...)`؛
  الواجهة لا تنفّذ أوامر نظام).
- Data Seeding يضمن: صف الإعدادات الوحيد، العملة الأساسية، أول Super Admin، فرع/مخزن رئيسي.
- **Audit log** غير قابل للتعديل أو الحذف (Trigger)، ويستخدم Keyset Pagination.
- **عملة أساسية واحدة** مضمونة بفهرس Unique جزئي.

### الجداول

**المرحلة 1:** `app_settings` · `branches` · `warehouses` · `warehouse_branches` · `currencies` ·
`custom_field_definitions` · `users` · `refresh_tokens` · `user_sessions` ·
`user_branches` · `user_warehouses` · `roles` · `role_permissions` · `user_roles` ·
`user_permissions` · `audit_log` · جداول QA · `document_sequences`.

**المرحلة 2:** `units` · `item_categories` · `items` · `item_units` · `item_barcodes` ·
`customer_categories` · `customers` · `supplier_categories` · `suppliers` ·
`fiscal_periods` · `period_scope_closures` · `period_close_checks` · `period_events` ·
`stock_documents` · `stock_document_lines` · `stock_movements` · `stock_balances` ·
`stock_transfer_receipts` · `stock_transfer_receipt_lines` · `stocktakes` · `stocktake_lines`.
**المرحلة 3:** `accounts` · `cost_centers` · `journals` · `posting_defaults` ·
`item_category_accounts` · `journal_entries` · `journal_entry_lines` ·
`gl_balances` · `party_balances`.
إجمالي 52 جدولًا بعد الترحيلات 0001–0023.

---

### أسرع طريق للعودة للعمل (بيانات Neon موجودة)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\set-db.ps1
```
يقرأ رابط القاعدة السابق من تاريخ git، يحوّل الترحيل إلى العنوان المباشر (بدون `-pooler`)، يحفظ
الأسرار في `user-secrets` (خارج المستودع)، يفحص الاتصال بـ `--check-db`، ثم يطبّق الترحيلات.
لبذرة قاعدة محلية نظيفة بدل Neon: `scripts\dev-db.ps1`.

## 4. الترقية من نسخة كانت متصلة بـ Neon (سبب «كان شغّالاً وتوقّف»)

النسخة القديمة كانت تحمل رابط Neon مباشرة داخل `src/Ubp.Web/appsettings.json`، فكان التطبيق
يتصل بالقاعدة السحابية تلقائياً. في **المرحلة 0** أُزيل هذا الرابط (وأُضيف `appsettings.Development.json`
يشير إلى قاعدة محلية) لأنه كان **سرّاً مكشوفاً في المستودع** — لذلك صار التطبيق يحتاج قاعدة محلية
أو تمرير أسرار Neon من خارج المستودع.

**لا يوجد أي فقد بيانات**: كل الترحيلات الجديدة (`0036`–`0045`) إضافية فقط
(جداول/أعمدة/فهارس/عروض) بلا `DROP` أو `DELETE` أو تغيير أنواع.

### إعادة الاتصال بـ Neon بشكل صحيح (بلا أسرار في git)

```powershell
# 1) دوّر كلمة مرور Neon من لوحة التحكم أولاً (القديمة مسرّبة في تاريخ git)
# 2) خزّن الروابط في user-secrets (تُقرأ في Development فقط، ولا تدخل المستودع)
dotnet user-secrets --project src/Ubp.Web set "Database:ConnectionString" `
  "postgresql://<user>:<password>@ep-xxxx-pooler.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require"
dotnet user-secrets --project src/Ubp.Web set "Database:MigrationConnectionString" `
  "postgresql://<user>:<password>@ep-xxxx.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require"
dotnet user-secrets --project src/Ubp.Web set "Jwt:Key" "<64 حرفاً عشوائياً>"
dotnet user-secrets --project src/Ubp.Web set "Bootstrap:SuperAdminPassword" "<كلمة مرور قوية>"
```
- `MigrationConnectionString` يجب أن يكون **العنوان المباشر** (بدون `-pooler`) لأن الترحيل يعتمد على
  قفل جلسي `pg_advisory_lock`، والـ pooler قد يفصل الجلسة.
- أول ترحيل بعد الترقية سيطبّق `0036`–`0045` (تراكمي وآمن على البيانات القائمة).
- **تغيّر متوقع**: الحساب `admin` سيُطلب منه تغيير كلمة المرور عند أول دخول؛ وإن كانت كلمته ما زالت
  `Admin@123` فسيستبدلها النظام بـ `Bootstrap:SuperAdminPassword` المكوَّنة.
- للتشغيل المحلي بدل Neon: `scripts/dev-db.ps1` ثم `dotnet run --project src/Ubp.Web -- --migrate`.

## 5. الأمان والصلاحيات

- **Default Deny**: أي شاشة/زر غير مسجّل في `AppRegistry` أو غير ممنوح = ممنوع.
- الصلاحية الفعلية = صلاحيات الأدوار، ثم `user_permissions` الفردية تتغلب عليها (سماحاً أو منعاً).
- نطاق الصلاحية: `0 = سجلاتي` · `1 = فروعي` · `2 = الكل`.
- الأدمن يتجاوز كل الصلاحيات، وSuper Admin وحده يعدّل أعلام المديرين ويظهر لهم.
- فحص الصلاحية والفرع داخل **الخدمات على السيرفر**؛ إخفاء الأزرار في الواجهة للشكل فقط.
- الجلسة محفوظة على السيرفر (`user_sessions`) وتنتهي بالخمول حسب `app_settings`.
- قفل الحساب بعد `max_failed_attempts` لمدة `lockout_minutes`، والرسالة موحّدة لا تكشف حالة القفل.
- Refresh Token يُحفظ كـ **Hash فقط** ويُدوَّر عند التجديد.
- `password_hash` و`token_hash` مستبعدان من Audit.
- كل خطأ له **Trace ID** يظهر للمستخدم في صفحة الخطأ، مع تفاصيل كاملة في Serilog فقط.
- عزل الفروع مطبّق في كل استعلام، مع Triggers تمنع ربط مخزن لا يتبع فروع المستخدم.

---

## 6. الأداء والترقيم

- كل الجداول: `LIMIT 25 OFFSET n` مع فرز ثابت `(sort column, id)` — الحد 25 مفروض في طبقة البيانات.
- `audit_log`: Keyset `WHERE id < @before ORDER BY id DESC LIMIT 25` بدل OFFSET.
- بحث عربي موحّد عبر `normalize_search` (همزات/تاء مربوطة/ياء/تشكيل) + فهارس GIN trigram.
- الفهارس على أعمدة الفرز/الفلترة والمفاتيح الأجنبية.
- كاش قصير على السيرفر للإعدادات والصلاحيات، ويُمسح عند التعديل (لا كاش بيانات على الجهاز).
- التواريخ UTC وتُعرض بتوقيت القاهرة.
- التقارير المحاسبية تقرأ من أرصدة شهرية مجمّعة (`gl_balances` / `party_balances`)
  ولا تجمع بنود القيود في كل طلب؛ وكشف الأستاذ يستخدم Keyset مع فهرس
  يغطي المدين/الدائن (INCLUDE) لجمع الفترة بدون رجوع للجدول.

---

## 7. الشاشات

| الشاشة | المسار | ملاحظات |
|---|---|---|
| تسجيل الدخول | `/login` | رسالة خطأ موحّدة، دعم اللغة |
| لوحة التحكم | `/` | اختصارات الشاشات المسموحة فقط |
| الإعدادات العامة | `/settings/general` | الشركة/الشعار/التنسيقات/سياسات الأمان |
| الفروع | `/settings/branches` | حذف المستخدم = تعطيل |
| المخازن | `/settings/warehouses` | ربط بأكثر من فرع |
| العملات | `/settings/currencies` | عملة أساسية واحدة |
| الحقول المخصصة | `/settings/custom-fields` | تظهر تلقائياً في النماذج |
| المستخدمون | `/users` | فروع/مخازن/أدوار/صلاحيات/فك قفل/إعادة تعيين |
| الأدوار | `/roles` | مصفوفة صلاحيات + نسخ |
| سجل الأنشطة | `/audit` | قراءة فقط + مقارنة قديم/جديد |
| مركز الجودة | `/qa` | مصفوفة اختبارات + تشغيل + نتائج |
| إدارة قاعدة البيانات | `/admin/database` | Super Admin فقط: حالة/جداول/ترحيلات/صفوف |
| وحدات القياس | `/inventory/units` | دقة عشرية + تعطيل بدل الحذف |
| تصنيفات الأصناف | `/inventory/item-categories` | تصنيفات هرمية |
| الأصناف | `/inventory/items` | وحدات متعددة + باركود + أسعار مرجعية |
| العملاء | `/partners/customers` | نطاق شركة أو فرع + تصنيفات + حد ائتمان |
| الموردون | `/partners/suppliers` | نفس نموذج العملاء |
| مستندات المخزون | `/inventory/documents` | استلام/صرف/تحويل/تسوية/رصيد افتتاحي + ترحيل وعكس |
| الجرد | `/inventory/stocktake` | جلسات جرد + تسوية آلية بالفروق |
| الفترات المالية | `/accounting/periods` | إغلاق فرع/وحدة/شركة + فحوصات + سجل |
| تقارير المخزون | `/reports/stock` | أرصدة المخزون + كارت الصنف |
| دليل الحسابات | `/accounting/accounts` | شجرة بمسار مادي + سياسة مركز تكلفة |
| مراكز التكلفة | `/accounting/cost-centers` | شجرة + ربط اختياري بفرع |
| دفاتر اليومية | `/accounting/journals` | 7 دفاتر نظامية + مخصصة |
| إعدادات الحسابات | `/accounting/settings` | حسابات الترحيل + فحص الاكتمال |
| القيود | `/accounting/entries` | مسودة/مرحّل/ملغي + عكس ونسخ |
| شاشنة القيد | `/accounting/entries/{id}` | طرف + توزيع مراكز + موازنة تلقائية |
| الأرصدة الافتتاحية | `/accounting/opening-balances` | استيراد CSV + موازنة على حقوق الملكية |
| مركز قيود المخزون | `/accounting/stock-posting` | معاينة وتوليد قيود المستندات |
| كشف الأستاذ | `/reports/accounting/ledger` | Keyset على (entry_date, id) |
| ميزان المراجعة | `/reports/accounting/trial-balance` | قراءة من `gl_balances` |
| كشف حساب طرف | `/reports/accounting/party-statement` | عميل/مورد من `party_balances` |
| تحليل مراكز التكلفة | `/reports/accounting/cost-centers` | مدين/دائن حسب المركز |
| مطابقة المخزون | `/reports/accounting/stock-vs-gl` | قيمة المخزون مقابل الأستاذ |

كل شاشة مقسّمة إلى **Component + Service + Repository**، وكل ملف كود ≤ 300 سطر.

---

## 8. مركز الجودة (QA)

- الاختبارات مسجّلة في الكود (`QaService.Registry`) وتظهر تلقائياً في المصفوفة.
- تشغيل الكل/المحدد، وتسجيل كل محاولة ونتيجة ورقم تتبع في `qa_test_*`.
- **فشل آمن**: لا يُسمح بالتشغيل إذا كانت قاعدة QA غير مُعدّة أو تساوي قاعدة التطبيق.
- الاختبارات الحالية: الصلاحيات (منع افتراضي/دور/استثناء/أدمن)، تطبيع العربية،
  صف الإعدادات، العملة الأساسية، عدم قابلية Audit للتعديل، والتزامن بالنُسخ.

---

## 9. الاختبارات

```bash
# إنشاء قواعد الاختبار
createdb -h localhost -U postgres ubp_test
createdb -h localhost -U postgres ubp_qa

# تشغيل الكل (52 اختباراً)
dotnet test
```
تغطي الاختبارات: الصلاحيات ونطاقها، قفل الحساب وفكه، عزل الفروع، تعارض `version`،
ترقيم 25 صفاً، البحث العربي، تغطية المخازن للفروع، عدم قابلية Audit للتعديل، عزل QA،
ترقيم المستندات تحت التزامن، المتوسط المرجح المتحرك، منع المخزون السالب، إغلاق الفترات
(مسودات/تحويلات معلقة/جرد غير مرحّل/تكلفة صفر)، التحويل والاستلام الجزئي، العكس ومنع تكراره،
الجرد والتسوية وسبب الفرق، الحقول المخصصة للأصناف والعملاء والموردين، وثبات الأرصدة مع الحركات.

في CI:
```bash
UBP_TEST_CONNECTION="Host=localhost;Database=ubp_test;Username=postgres;Password=postgres" \
UBP_QA_CONNECTION="Host=localhost;Database=ubp_qa;Username=postgres;Password=postgres" \
dotnet test
```

---

## 10. النشر

### Docker
```bash
cp .env.example .env   # عدّل الأسرار
docker compose up -d --build
```
- التطبيق على المنفذ `8080`، وPostgreSQL على `5432`.
- الصق التطبيق خلف Reverse Proxy (Nginx/Caddy) مع HTTPS وForwarded Headers.
- استخدم Secrets لإدارة Connection Strings وJWT Key، ولا تضعها في المستودع.

### قواعد الإنتاج
- `Database:AutoMigrate=false`، ونفّذ الترحيلات من CI/CD أو شاشة Super Admin.
- مستخدم DDL منفصل تماماً عن `ubp_app`.
- قاعدة QA منفصلة، وبيانات اختبار اصطناعية فقط.
- نسخ احتياطي دوري + اختبار استعادة فعلي.

---

## 11. الوضع الحالي والمراحل التالية

**منجز في المرحلة الأولى:** كل الجداول والشاشات أعلاه، المصادقة والجلسات، الصلاحيات،
الفروع/المخازن/العملات/الحقول المخصصة، سجل الأنشطة، إدارة قاعدة البيانات، مركز جودة أولي،
اختبارات آلية، توثيق، Docker، وقالب CI.

**منجز في المرحلة الثانية:** البيانات الأساسية للمخزون (وحدات/تصنيفات/أصناف/باركود)،
العملاء والموردون بنطاق شركة أو فرع، محرك فترات موحد، مستندات مخزون بترحيل ذري
ومتوسط تكلفة مرجح متحرك، تحويلات باستلام جزئي، جرد وتسويات، عكس مستندات،
تقارير أرصدة وكارت صنف، و9 شاشات جديدة. التفاصيل في `docs/PHASE2-AR.md`.

**منجز في المرحلة الثالثة:** دليل حسابات بشجرة ومسار مادي، مراكز تكلفة،
دفاتر يومية، قيود بترحيل ذري وعكس ممنوع مزدوج، أرصدة شهرية للحسابات والأطراف،
أرصدة افتتاحية مع استيراد CSV، ترحيل مخزون آلي، و5 تقارير مالية،
و13 شاشة جديدة. التفاصيل في `docs/PHASE3-AR.md`.

**المرحلة التالية (خطة معتمدة قيد التنفيذ):** المرحلة 4 (النواة التشغيلية: مشتريات، خزائن، حجوزات،
مبيعات، POS، متابعات) والمرحلة 6 (تقارير وإشعارات) والمرحلة 7 (تشغيل وإطلاق).
الحزمة الكاملة: `docs/PHASE4-AR.md` (الخطة والمقارنة بـOdoo) · `docs/PHASE4-DECISIONS-AR.md`
(القرارات D/N والقرارات المفتوحة) · `docs/PHASE4-SCHEMA-AR.md` (كل جدول وأعمدته)
· `docs/PHASE4-POSTING-AR.md` (مصفوفة الترحيل والصلاحيات والاختبارات).

**المرحلة التالية المقترحة:**
1. فواتير المبيعات والمشتريات وربطها بالمخزون والعملاء والموردين.
2. قواعد ترحيل الفواتير على محرك الفترات والأرصدة الحالي.
3. `tenant_id` + RLS إذا تقرر دعم عدة شركات.
4. RBAC على مستوى الحقول + تخصيص أعمدة/نماذج لكل مستخدم.
5. QA متقدم: Playwright، تقارير تغطية، بوابات إصدار، وبيانات اختبار معزولة.
6. بارتشنات لسجل الأنشطة وحركات المخزون عند تجاوز ملايين الصفوف.

> راجع `docs/PHASE1-AR.md` للتفاصيل و`docs/SECURITY-AR.md` لمراجعة الأمان.
