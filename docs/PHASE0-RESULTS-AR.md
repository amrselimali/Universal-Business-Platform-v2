# نتائج المرحلة 0 وحالة التوصيات

> مرجع مباشر: «ماذا نُفِّذ فعلاً» vs «ما ثبت كقرار» vs «ما تأجَّل» vs «ما يحتاج صلاحية المالك».
> تاريخ: 2026-10-31 · الفرع: `cline/kfnphrt4` · PR #12.

## 1) مصفوفة الحالة

| # | التوصية | الحالة | المرجع/الملف |
|---|---|---|---|
| 1 | إخراج أسرار Neon من المستودع | **منفَّذ** | `appsettings.json` |
| 2 | تدوير كلمة مرور `neondb_owner` فعلياً | **يحتاجك** (لوحة Neon) | `PROJECT_STATE.md` |
| 3 | ربط التشغيل بدور `ubp_app` على Neon | **يحتاجك** (الدور جاهز في الترحيل `0010`) | `docs/SECURITY-AR.md §4` |
| 4 | إلغاء `TrustServerCertificate=true` | **منفَّذ** | `DatabaseOptions.cs` |
| 5 | `AutoMigrate=false` افتراضياً | **منفَّذ** | `appsettings.json` |
| 6 | `IValidateOptions<T>` + `ValidateOnStart` قبل أي اتصال | **منفَّذ + مُختبَر** | `Startup/Validation/*`, `Program.cs` |
| 7 | إفشال الإقلاع بقيم افتراضية/ضعيفة في Production | **منفَّذ + مُختبَر حياً** | التشغيل أعطى 3 أخطاء وإقلاعاً مرفوضاً |
| 8 | إزالة كلمة مرور المدير الافتراضية من الكود | **منفَّذ** | `DataSeeder.cs` |
| 9 | معالجة `Admin@123` المزروع في الترحيل `0009` بلا تعديله | **منفَّذ + مُختبَر** | X-10 |
| 10 | تصغير مسار الصحة + 503 + كاش + `/health/live` | **منفَّذ + مُختبَر** | `SystemEndpoints.cs`, `HealthProbe.cs` |
| 11 | نقل الـ CI إلى `.github/workflows/` | **محتوى جاهز، التفعيل يحتاجك** | `docs/ci-workflow.yml` (صلاحية `workflows` غير متاحة) |
| 12 | بوابة 300 سطر + منع دفع الأسرار/مخرجات البناء | **منفَّذة** داخل الـ workflow | `docs/ci-workflow.yml` |
| 13 | Branch Protection على `main` | **يحتاجك** (إعداد مستودع) | — |
| 14 | `.gitattributes` + حذف ملف قفل Word | **منفَّذ** | `.gitattributes` |
| 15 | حذف فروع `cline/*` الميتة (11 فرعاً) | **لم يُنفَّذ** (حذف عن بُعد يحتاج موافقة صريحة) | — |
| 16 | key ring ثابت لـ Data Protection | **منفَّذ** (اختياري عبر الإعداد) | `DataProtectionRegistration.cs`, X-09 |
| 17 | دعم `dotnet user-secrets` | **منفَّذ** (`UserSecretsId` + توثيق) | `Ubp.Web.csproj`, README |
| 18 | تصحيح «C# 13» إلى C# 14 | **مسجَّل هنا** (لا يوجد كود متأثر) | هذه الوثيقة §4 |
| 19 | تصحيح مبرّر الاتصال المباشر (قفل جلسي لا xact) | **منفَّذ** كقاعدة تحقق + توثيق | `DatabaseOptionsValidator`, `SECURITY-AR §4` |
| 20 | `System.IO.Packaging` مرفوض؛ XLSX عبر `ZipArchive` | **قرار موثَّق** (الكود في المرحلة 6) | X-01 |
| 21 | إضافة `BANK` لـ `module_code` مؤجلة | **قرار موثَّق** | X-05 |
| 22 | إبقاء `auth.no_branch` منفصلة | **قرار موثَّق** (لا تغيير كود) | X-06 |
| 23 | Playwright عبر `npx` لا NuGet | **قرار مفتوح** | X-08 |
| 24 | إجبار تغيير كلمة مرور المدير | **منفَّذ** (`0036` + بوابة + توجيه Blazor) | `PasswordChangeGate`, `SessionValidationMiddleware` |
| 25 | `app_settings.time_zone` + `IDisplayFormatter` | **منفَّذ** (`0037` + استبدال كل `.ToLocalTime()`) | `DisplayFormatter.cs`, `SettingsService` |
| 26 | اختبار معماري يمنع `Ubp.Infrastructure` في الصفحات | **منفَّذ** (3 اختبارات؛ كشف ملفاً سابعاً) | `ArchitectureTests.cs` |
| 27 | `IsolatedDatabaseFactory` لاختبارات التزامن | **مؤجَّل** إلى المرحلة 3 | — |
| 28 | عدم إعادة بناء اختبار الترقيم المتزامن | **مطبَّق** (لا ملف جديد؛ الموجود يغطيه) | `DocumentSequenceTests.cs` |

## 2) جدول ترقيم الترحيلات المعتمد

آخر ترحيل منفَّذ: `0035_booking_time_slots.sql` (لا تعديل عليه — G1).

| الرقم | المحتوى | الحالة |
|---|---|---|
| `0036` | `users.must_change_password` + `password_changed_at` | **منفَّذ** (2026-10-31) |
| `0037` | `app_settings.time_zone` | **منفَّذ** (2026-10-31) |
| `0038` | `stock_documents.idempotency_key` + فهرس فريد جزئي + حقول تتبع ناقصة (بعد جرد `information_schema`) | لم يُنفَّذ |
| `0039` | `saved_report_filters` (المرحلة 6A) | لم يُنفَّذ |
| `0040+` | `bi` · `notifications` · `message_outbox` · `sales_daily_totals` | لم يُنفَّذ |

> ملاحظة: `0019` غير موجود في التسلسل (فجوة قديمة لا تُملأ، ولا يُعاد استخدام رقمها).

## 3) التقديرات المصحّحة (بدل أرقام الخطة المضغوطة)

| المجموعة | التقدير المعتمد |
|---|---|
| المرحلة 0 (كما نُفِّذت) | 3–4 أيام عمل بدل 1–2 |
| المرحلة 6 (6A→6E) | 12–15 يوماً بدل 6–8 |
| المرحلة 7 (بلا Control DB) | 15–18 يوماً بدل 8–12 |

## 4) تصحيحات على نص الخطة

- `C# 13` غير صحيح: `net10.0` + `LangVersion=latest` = **C# 14**.
- مبرّر «الاتصال المباشر ليعمل `pg_advisory_xact_lock`» غير صحيح: أقفال xact تعمل عبر
  الـ Transaction Pooler؛ المحتاج للاتصال المباشر هو **القفل الجلسي** في `MigrationRunner`.
- `System.IO.Packaging` حزمة NuGet وليست جزءاً من الإطار المشترك.

## 5) المتطلبات التالية (بترتيبها)

1. **يدوياً الآن**: تدوير كلمة مرور Neon + نسخ `docs/ci-workflow.yml` إلى `.github/workflows/ci.yml`
   + Branch Protection + تمرير الأسرار من Secrets.
2. **المرحلة 1**: `0037` (`must_change_password`) + مسار الإجبار في الدخول والواجهة.
3. **المرحلة 2.1**: `LookupService` وإزالة استيراد المستودعات من 6 صفحات، ثم **اختبار معماري**.
4. **المرحلة 2.2**: `0038` + `IDisplayFormatter` + إزالة `.ToLocalTime()` المتناثرة.
5. **المرحلة 3**: `IsolatedDatabaseFactory` ثم سيناريوهات التزامن الثمانية.
