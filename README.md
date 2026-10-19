# Universal Business Platform — منصة الأعمال السحابية

> مشروع مستقل بالكامل، مبني من الصفر بـ **C# / .NET 10** و**PostgreSQL**،
> ولا يعتمد على تطبيق React القديم في أي شيء. قاعدة البيانات هي المصدر الحقيقي والوحيد للبيانات،
> وكل الشاشات والصلاحيات تُفحص على السيرفر.

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

# 2) الإعدادات (يمكن تعديلها في src/Ubp.Web/appsettings.json)
#    Database:ConnectionString            → مستخدم التطبيق (بدون DDL)
#    Database:MigrationConnectionString   → مستخدم DDL للترحيلات
#    Database:QaConnectionString          → قاعدة QA مستقلة
#    Jwt:Key                              → 32 حرفاً على الأقل

# 3) التشغيل
dotnet run --project src/Ubp.Web
```

الرابط: **http://localhost:5080**

بيانات الدخول الأولى:
| البند | القيمة |
|---|---|
| اسم المستخدم | `admin` |
| كلمة المرور | `Admin@123` |

> ⚠️ **غيّر كلمة المرور فوراً** من قائمة المستخدم أو `Bootstrap:SuperAdminPassword` قبل أول تشغيل.
> عند أول تشغيل تُطبَّق الترحيلات تلقائياً على `ubp` و`ubp_qa` (يمكن تعطيل ذلك بـ `Database:AutoMigrate=false`).

### متغيرات البيئة في الإنتاج
```bash
Database__ConnectionString="Host=...;Database=ubp;Username=ubp_app;Password=..."
Database__MigrationConnectionString="Host=...;Database=ubp;Username=postgres;Password=..."
Database__QaConnectionString="Host=...;Database=ubp_qa;Username=postgres;Password=..."
Database__AutoMigrate=false
Jwt__Key="<64+ random chars>"
Bootstrap__SuperAdminPassword="<strong password>"
```

---

## 3. قاعدة البيانات والترحيلات

- كل تغيير في الهيكل = **سكريبت جديد مرقّم** داخل
  `src/Ubp.Infrastructure/Migrations/Scripts/` (مثل `0011_add_x.sql`). لا يُعدَّل سكريبت بعد تنفيذه.
- التنفيذ عبر **DbUp** داخل معاملة لكل سكريبت، مع قفل Advisory يمنع التنفيذ المتوازي.
- نفس الـ runner يُستخدم في:
  1. بدء التطبيق (حسب الإعداد).
  2. زر «تحديث / تهيئة قاعدة البيانات» في شاشة قاعدة البيانات (Super Admin).
  3. CI/CD.
- مستخدم التطبيق `ubp_app` لا يملك DDL؛ الترحيلات تستخدم اتصالاً منفصلاً بصلاحية أعلى.
- Data Seeding يضمن: صف الإعدادات الوحيد، العملة الأساسية، أول Super Admin، فرع/مخزن رئيسي.
- **Audit log** غير قابل للتعديل أو الحذف (Trigger)، ويستخدم Keyset Pagination.
- **عملة أساسية واحدة** مضمونة بفهرس Unique جزئي.

### الجداول (المرحلة الأولى)
`app_settings` · `branches` · `warehouses` · `warehouse_branches` · `currencies` ·
`custom_field_definitions` · `users` · `refresh_tokens` · `user_sessions` ·
`user_branches` · `user_warehouses` · `roles` · `role_permissions` · `user_roles` ·
`user_permissions` · `audit_log` · جداول QA.

---

## 4. الأمان والصلاحيات

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

## 5. الأداء والترقيم

- كل الجداول: `LIMIT 25 OFFSET n` مع فرز ثابت `(sort column, id)` — الحد 25 مفروض في طبقة البيانات.
- `audit_log`: Keyset `WHERE id < @before ORDER BY id DESC LIMIT 25` بدل OFFSET.
- بحث عربي موحّد عبر `normalize_search` (همزات/تاء مربوطة/ياء/تشكيل) + فهارس GIN trigram.
- الفهارس على أعمدة الفرز/الفلترة والمفاتيح الأجنبية.
- كاش قصير على السيرفر للإعدادات والصلاحيات، ويُمسح عند التعديل (لا كاش بيانات على الجهاز).
- التواريخ UTC وتُعرض بتوقيت القاهرة.

---

## 6. الشاشات

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

كل شاشة مقسّمة إلى **Component + Service + Repository**، وكل ملف كود ≤ 300 سطر.

---

## 7. مركز الجودة (QA)

- الاختبارات مسجّلة في الكود (`QaService.Registry`) وتظهر تلقائياً في المصفوفة.
- تشغيل الكل/المحدد، وتسجيل كل محاولة ونتيجة ورقم تتبع في `qa_test_*`.
- **فشل آمن**: لا يُسمح بالتشغيل إذا كانت قاعدة QA غير مُعدّة أو تساوي قاعدة التطبيق.
- الاختبارات الحالية: الصلاحيات (منع افتراضي/دور/استثناء/أدمن)، تطبيع العربية،
  صف الإعدادات، العملة الأساسية، عدم قابلية Audit للتعديل، والتزامن بالنُسخ.

---

## 8. الاختبارات

```bash
# إنشاء قواعد الاختبار
createdb -h localhost -U postgres ubp_test
createdb -h localhost -U postgres ubp_qa

# تشغيل الكل (25 اختباراً)
dotnet test
```
تغطي الاختبارات: الصلاحيات ونطاقها، قفل الحساب وفكه، عزل الفروع، تعارض `version`،
ترقيم 25 صفاً، البحث العربي، تغطية المخازن للفروع، عدم قابلية Audit للتعديل، وعزل QA.

في CI:
```bash
UBP_TEST_CONNECTION="Host=localhost;Database=ubp_test;Username=postgres;Password=postgres" \
UBP_QA_CONNECTION="Host=localhost;Database=ubp_qa;Username=postgres;Password=postgres" \
dotnet test
```

---

## 9. النشر

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

## 10. الوضع الحالي والمراحل التالية

**منجز في المرحلة الأولى:** كل الجداول والشاشات أعلاه، المصادقة والجلسات، الصلاحيات،
الفروع/المخازن/العملات/الحقول المخصصة، سجل الأنشطة، إدارة قاعدة البيانات، مركز جودة أولي،
اختبارات آلية، توثيق، Docker، وقالب CI.

**المرحلة التالية المقترحة:**
1. شاشات ERP التشغيلية (منتجات، مخزون، فواتير، محاسبة، شركاء).
2. ترحيل محاسبي بقواعد Posting Rules + فترات مقفلة.
3. RBAC على مستوى الحقول + تخصيص أعمدة/نماذج لكل مستخدم.
4. QA متقدم: Playwright، تقارير تغطية، بوابات إصدار، وبيانات اختبار معزولة.
5. فهارس/بارتشنات لسجل الأنشطة عند تجاوز ملايين الصفوف.

> راجع `docs/PHASE1-AR.md` للتفاصيل و`docs/SECURITY-AR.md` لمراجعة الأمان.
