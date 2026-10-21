# مراجعة مطابقة التخطيط (ملفات الورد) مع الكود المنفذ

**المشروع:** Universal Business Platform v2
**تاريخ المراجعة:** 2026-10-21 · **الفرع:** `main` · **آخر commit:** `3ce499c`
**الملفات المرجعية:** `0- Plan/1-Foundation.docx` · `0- Plan/2-allow_negative_stock.docx` · `0- Plan/3-Customers,Vendors,Stock.docx`

---

## 0. المنهجية وحدود المراجعة

| البند | ما تم |
|---|---|
| مصادر الخطة | استخراج النص الكامل من الثلاثة docx (جداول + نصوص) |
| مصادر الكود | كل ملفات `src/` (Core · Infrastructure · Application · Web) + `tests/Ubp.Tests` + `ci/ci.yml` |
| قاعدة البيانات | 11 سكريبت DbUp مقروءة سكريبتًا سكريبتًا (جداول، فهارس، triggers، صلاحيات) |
| **لم يتم** | `dotnet build` / `dotnet test` — الـ SDK غير موجود في بيئة المراجعة. أي حكم على البناء أو الاختبارات هنا هو **مراجعة كود فقط، وليس تشغيلًا فعليًا** |
| **لم يتم** | الاتصال بقاعدة البيانات (Neon أو محلية) — لا استعلامات تحقق حقيقية |

### نسب التطابق الإجمالية

| النطاق | التطابق | الحكم المختصر |
|---|---:|---|
| المرحلة 1 (ملف 1 — أوله) | **≈ 88%** | منفذة فعليًا وبجودة عالية، مع 5 انحرافات مهمة |
| QA & Test Control Center v2.2 (آخر ملف 1) | **≈ 15%** | «أساس» فقط: Registry ثابت + 9 اختبارات داخل العملية |
| المرحلة 2أ (ملف 2) | **≈ 90%** | كل التغييرات المذكورة موجودة، لكن بها 3 عيوب حقيقية |
| المرحلة 2 الكاملة (ملف 3) | **≈ 3%** | جدول واحد من 14، وبمخطط مختلف، ولا يوجد محرك فترات |

---

## 1. أهم النتائج (اقرأ هذا أولًا)

| # | النتيجة | الخطورة | الدليل |
|---|---|---|---|
| 1 | **بيانات اتصال Neon حقيقية (مستخدم + كلمة مرور) مرفوعة داخل `appsettings.json`**، والتوثيق يقول «لا توجد أسرار في المستودع» | **P0 — تسريب سري** | `src/Ubp.Web/appsettings.json:29-30` · `docs/SECURITY-AR.md` (إدارة الأسرار) |
| 2 | **تعارض الـ `version` في شاشة الإعدادات العامة معطّل فعليًا**: الخدمة تستبدل نسخة المستخدم بنسخة حديثة قبل الحفظ، فيحدث «آخر كاتب يفوز» بدل رفض التعارض | **P0 — فقدان تعديل** | `src/Ubp.Application/Services/SettingsService.cs:49-51` |
| 3 | **CI لا يعمل إطلاقًا**: الـ workflow في `ci/ci.yml` وليس في `.github/workflows/`، ولا يوجد مجلد `.github` | **P0 — جودة** | `ci/ci.yml` · غياب `.github/workflows` |
| 4 | **`RefreshAsync` ينشئ توكنَي تحديث**: واحد يُعاد للعميل وواحد يبقى نشطًا 30 يومًا بلا استخدام، مع إنشاء جلسة جديدة في كل تجديد دون إلغاء القديمة | P1 — أمن/تضخم بيانات | `AuthService.Session.cs:14-19, 110-129` |
| 5 | **انتهاء الجلسة بالخمول لا يُطبَّق داخل دائرة Blazor**: `UserContext` يخزّن نتيجة التحقق مدى عمر الـ circuit، والميدلوير يتخطى `/_blazor` | P1 — أمن | `Services/UserContext.cs:15,23` · `SessionValidationMiddleware.cs:54-67` |
| 6 | **`document_sequences` المنفذ (0011) لا يطابق مواصفة المرحلة 2** (لا `tenant_id`، لا `fiscal_period_id`، لا `next_number/number_padding/reset_policy`، و`branch_id NOT NULL` يمنع الترقيم على مستوى الشركة) | P1 — سدّاد معماري | `0011_…sql:14-27` مقابل `3-Customers…docx §12` |
| 7 | **المرحلة 2 تبدأ على أساس غير محسوم**: الملف 3 يطلب `tenant_id` و«دعم عدة شركات»، والمرحلة 1 شركة واحدة بلا `tenant_id` | P1 — قرار مالك المنتج | `3-Customers…docx:75,117,484` |

---

## 2. مطابقة المرحلة 1 — تفصيلًا

### 2.1 الستاك والبنية

| البند في الخطة | الحالة | الدليل / ملاحظة |
|---|---|---|
| C# / .NET على سيرفر، قاعدة سحابية مصدر وحيد | ✅ | `Directory.Build.props` (`net10.0`) · لا كاش على العميل |
| Dapper + SQL صريح | ✅ | كل `Repositories` |
| DbUp بسكريبتات مرقمة لا تُعدَّل | ✅ | `Migrations/Scripts/0001..0011` · `MigrationRunner` |
| كل سكريبت داخل Transaction + منع تنفيذ متوازٍ | ✅ | `.WithTransactionPerScript()` + `pg_try_advisory_lock` |
| حد 300 سطر | ✅ | أكبر ملف 135 سطرًا (`WarehouseRepository.Write.cs`) |
| فصل Component / Service / Repository | ✅ | `Pages/*` + خدمات + مستودعات (تقسيم `partial`) |
| 100 مستخدم متزامن | ⚠️ غير مُقاس | لا اختبار حمل؛ التصميم يسمح نظريًا (Dapper بلا ORM) |
| عملات متعددة + عملة أساسية واحدة | ✅ | `currencies` + `ux_currencies_single_base` |
| فروع/مخازن متعددة وربط مرن | ✅ | `warehouse_branches` · `user_branches` · `user_warehouses` |
| تخصيص من الشاشات | ✅ جزئيًا | الحقول المخصصة للفروع والمخازن فقط (`CustomFieldService.Validation.cs:13-14`) |

### 2.2 الجداول (المرحلة 1)

| الجدول | الحالة | ملاحظات دقيقة |
|---|---|---|
| `app_settings` | ✅ | كل الأعمدة مطابقة + `allow_negative_stock` (0011) + `ux_app_settings_singleton` |
| `branches` / `warehouses` | ✅ | كل الأعمدة + `search_text` مولّد + GIN trgm + `version` trigger |
| `warehouse_branches` | ✅ | UNIQUE + CASCADE + فهرس `branch_id` |
| `users` | ✅ | كل الأعمدة + `ux_users_username_lower` + `CHECK (NOT is_super_admin OR is_admin)` + trigger حماية آخر Super Admin |
| `refresh_tokens` | ✅ | hash فقط + `replaced_by` + CASCADE |
| `user_sessions` | ✅ إضافة على الخطة | ضرورية لتنفيذ «الفرع الحالي على السيرفر» و«انتهاء الخمول» |
| `user_branches` / `user_warehouses` | ✅ | partial unique على `is_default` |
| `roles` / `role_permissions` / `user_roles` / `user_permissions` | ✅ | `scope CHECK IN (0,1,2)` |
| `currencies` | ✅ | `character(3)` + unique للأسماء + فهرس أساسية واحدة |
| `custom_field_definitions` | ✅ | regex لمفتاح الحقل + CHECK للنوع |
| `audit_log` | ✅ | identity + `trace_id` + فهارس Keyset + trigger يمنع UPDATE/DELETE |
| `tenant_id` | ❌ | غير موجود في أي جدول (الخطة 1 لا تطلبه، والخطة 3 تطلبه) |

### 2.3 الصلاحيات والأمان

| البند | الحالة | الدليل / الملاحظة |
|---|---|---|
| Default-Deny | ✅ | `PermissionEvaluator.Evaluate` يبدأ بـ `Allowed=false` |
| الاستثناء الفردي يتغلب على الدور | ✅ | `PermissionEvaluator.cs:48-58` |
| الأدمن يتجاوز | ✅ | `PermissionEvaluator.cs:25-31` |
| فحص على السيرفر في كل خدمة | ✅ | `PermissionService.RequireAsync` في كل عملية تقريبًا |
| فحص انتماء الفرع | ✅ | `ActorContext.CanAccessBranch` + `AllowedBranchIds` في الاستعلامات |
| Super Admin مخفي عن أدمن العميل | ✅ | `UserRepository.cs:59` + `EnsureTargetVisible` |
| آخر Super Admin محمي | ✅ | trigger + فحص خدمة |
| قفل الحساب + رسالة موحّدة | ✅ | `AuthService.cs:44-58` · `AuthenticationTests` |
| فك القفل | ✅ | `UserService.UnlockAsync` + زر في الشاشة |
| إلغاء كل توكنات المستخدم عند التعطيل/تغيير كلمة المرور | ✅ | `UserService.Mutations.cs` · `AuthService.Account.cs:22-24` |
| Refresh Token: hash فقط + HttpOnly + تدوير | ⚠️ | hash وHttpOnly ✅، لكن التدوير ينشئ توكنًا إضافيًا يتيمًا (P1 #4) |
| منع إزالة فرع يترك مخزنًا معلقًا | ✅ | `0007_warehouse_consistency.sql` + `validation.warehouse_not_in_branches` |
| استثناء CASCADE عند حذف المستخدم | ✅ | `fn_user_branch_guard` يتحقق من وجود المستخدم أولًا |
| استبعاد `password_hash` / `token_hash` من Audit | ✅ | `AuditSanitizer` + اختبار |
| Trace ID في صفحة الخطأ بلا تفاصيل تقنية | ✅ | `ExceptionHandlingMiddleware` → `/error?traceId=` |
| صفحة 403 / 404 | ✅ | `/forbidden` · `/not-found` + `ForbiddenView` |


### 2.4 الشاشات والمكونات

| البند | الحالة | ملاحظة |
|---|---|---|
| تسجيل دخول برسالة موحّدة + لوجو + تبديل لغة | ✅ | `Login.razor` |
| Sidebar بالأزرار المسموحة فقط + Responsive | ✅ | `NavMenu.razor` + `MudDrawer Variant=Responsive Breakpoint=Md` |
| Header: الفرع الحالي/تبديله + اللغة + قائمة المستخدم | ✅ | `HeaderBar.razor` |
| Dashboard: الشركة + الفرع + المستخدم + اختصارات | ⚠️ | **اللوجو غير معروض في الـ Dashboard** (في الـ Header فقط) · `Home.razor:19-45` |
| تغيير كلمة المرور (حالي/جديد/تأكيد + سياسة الطول) | ✅ | `ChangePassword.razor` |
| المستخدمون: فروع/مخازن/أدوار/صلاحيات/إعادة تعيين/فك قفل | ✅ | `Users/*` |
| مصفوفة صلاحيات ديناميكية + تحديد/إلغاء الكل + نسخ من مستخدم | ✅ | `UserPermissions.razor` + `PermissionService.GetMatrixAsync` |
| النطاق لكل زر + إظهار صلاحية الدور بجانب الاستثناء | ✅ | `UserPermissions.razor:50-67` |
| الأدوار وصلاحياتها + نسخ + تحديد الكل | ✅ | `RoleDialog.razor` |
| الإعدادات العامة (شركة/لوجو/لغة/تنسيقات/جلسة/قفل/رصيد سالب) | ✅ | `General.razor` |
| الفروع / المخازن + ربط بالفروع | ✅ | `Branches.razor` · `Warehouses.razor` |
| العملات إضافة/تعديل/حذف | ✅ | `Currencies.razor` + منع حذف الأساسية |
| تعريف الحقول المخصصة + ظهورها تلقائيًا في الفورم | ✅ | `BranchDialog.razor:20-48` (والمخازن كذلك) |
| سجل الأنشطة: Keyset + فلاتر + فرق old/new + Trace | ✅ | `Audit/Index.razor(.cs)` + `AuditDetailDialog` |
| شاشة قاعدة البيانات: الاتصال/الجداول/العدد/الترحيلات | ✅ | `Database.razor.cs` + `SchemaAdminRepository` |
| تعديل/حذف صفوف بصلاحية مستقلة + تأكيد مشدد + Audit | ✅ | `RowActionDialog` (تأكيد `UPDATE table` / `DELETE table`) |
| **إضافة صف جديد من شاشة الداتا بيز** | ❌ | لا يوجد `InsertRowAsync` ولا زر إضافة — الخطة تطلب «تعديل وإضافة وحذف» |
| عدم عرض Connection String كاملًا | ✅ | `DatabaseStatus` يعرض Server/Database/User فقط |
| شاشة QA: مصفوفة + تشغيل فردي/محدد/الكل + سجل نتائج | ✅ | `Pages/Qa/*` |

### 2.5 الترقيم والبحث والتاريخ

| البند | الحالة | الدليل |
|---|---|---|
| 25 سجل كحد أقصى على مستوى قاعدة البيانات | ✅ | `PageRequest.MaxPageSize=25` + `LIMIT @take OFFSET @offset` |
| فرز ثابت (عمود الفرز + id) | ✅ | `SqlSort.Build` → `"{column} {dir}, id {dir}"` |
| Keyset في `audit_log` | ✅ | `AuditRepository.cs:53, 77-78` |
| تطبيع البحث العربي | ✅ | `normalize_ar` في `0002_functions.sql:5-24` |
| البحث عبر `search_text` المولّد | ✅ | `LIKE '%' || normalize_search(@search) || '%'` |
| التواريخ UTC | ✅ | كل الأعمدة `timestamptz` والقيم `now()` |
| العرض بتوقيت القاهرة | ⚠️ | `Audit/Index.razor.cs:CairoTime` فقط، وباقي الشاشات لا تحوّل |
| تطبيق `date_format` / `time_format` / `number_format` | ❌ | لا يوجد أي استخدام لها خارج شاشة الإعدادات (grep = صفر) |
| جلسة تنتهي بالخمول حسب الإعداد | ⚠️ | على مستوى HTTP ✅ / داخل دائرة Blazor ❌ (P1 #5) |
| كاش سيرفر قصير للصلاحيات/الإعدادات مع إبطال | ✅ | `PermissionService` 5 دقائق + `SettingsService` دقيقتان |

### 2.6 الاختبارات

| البند | الحالة | التفصيل |
|---|---|---|
| اختبارات الصلاحيات | ✅ | 6 اختبارات `PermissionEvaluatorTests` |
| عزل الفروع | ✅ | 3 `BranchIsolationTests` |
| قفل الحساب وفكه | ✅ | `AuthenticationTests` |
| تعارض النسخ | ⚠️ | على المستودع مباشرة؛ `SettingsValidationTests:64-67` **تتجاوز الخدمة** |
| رفض مخزن غير تابع | ✅ | `WarehouseConsistencyTests` |
| عدد الاختبارات | ⚠️ | **32** `[Fact]` فعليًا. ملف 2 يتوقع «25 + 8 = 33»، والمرحلة 1 الحقيقية كانت 24 لا 25 |
| اختبارات تُدمّر الـ schema | ⚠️ | كل `IClassFixture<TestDb>` يعمل `DROP SCHEMA … CASCADE` — آمن فقط مع تعطيل التوازي |
| اختبارات المتصفح / التغطية / بوابات الجودة | ❌ | غير موجودة |

---


## 3. المرحلة 2أ (ملف 2) — هل كل ما قيل موجود فعلًا؟

| ما ذكره ملف 2 | موجود؟ | التحقق |
|---|---|---|
| `0011_phase2a_settings_and_sequences.sql` + `allow_negative_stock` + `document_sequences` + `next_document_number` | ✅ | الملف موجود (53 سطرًا) |
| `DocumentSequenceRepository.cs` يأخذ connection + transaction من المستدعي | ✅ | `NextNumberAsync(IDbConnection, IDbTransaction, …)` |
| 5 اختبارات للتسلسل (شكل/فصل/50 متزامن/rollback/نوع غلط) | ✅ | `DocumentSequenceTests.cs` — 5 `[Fact]` |
| `AppSettings.AllowNegativeStock` + `Clone()` | ✅ | `Ubp.Core/Models/AppSettings.cs:16,22` |
| `SettingsRepository` يشمل الحقل في SELECT/UPDATE/RETURNING | ✅ | `SettingsRepository.cs:29, 62, 71` |
| مفتاح «السماح بالرصيد السالب» + نص توضيحي في `General.razor` | ✅ | `General.razor:45-50` |
| مفتاحان في `ar.json` و`en.json` | ✅ | الملفان متطابقان: 348 مفتاحًا لكل منهما |
| `document_sequences` للقراءة فقط في شاشة الداتا بيز | ✅ | `SchemaAdminRepository.Write.cs:23` (في `ReadableTables` فقط، ليست في `EditableTables`) |
| تسجيل الـ repository في DI | ✅ | `ServiceRegistration.cs:112` |
| اختبارات الإعدادات الجديدة + تحقق من تنفيذ 0011 | ✅ | `SettingsValidationTests.cs` |
| `CHANGELOG.md` محدَّث | ✅ | قسم 2026-10-21 |
| إصلاح تعديل نسخة الكاش مباشرة عبر `Clone()` | ✅ | `General.razor:75` |

### 3.1 العيوب الثلاثة في تسليم 2أ

1. **تعارض النسخ غير قابل للحدوث من الواجهة (P0):**
   `SettingsService.UpdateAsync` يقرأ السجل ثم يكتب `input.Version = old.Version` قبل التحديث.
   النتيجة: لو فتح أدمن الشاشة وغيّر أدمن آخر القيمة، الحفظ الثاني **يمسح** الأول بهدوء ولا يظهر
   «تم تعديل السجل بواسطة مستخدم آخر». والاختبار `Stale_settings_version_is_rejected`
   يعترف بذلك في تعليقه ويلجأ إلى `SettingsRepo.UpdateAsync` مباشرة، أي أنه **لا يحمي مسار الشاشة**.
   **الإصلاح:** حذف السطرين 50-51، والاعتماد على `input.Version` القادمة من الفورم،
   وإضافة اختبار يمر عبر `SettingsService` ويؤكد `ConflictException`.

2. **`document_sequences` لا يصلح لتكملة المرحلة 2:** التفصيل في §4.2.

3. **الاختبارات لم تُشغَّل والعدد أقل من المتوقع:** 32 لا 33، والمرحلة 1 كانت 24 لا 25.
   (لم أستطع البناء في هذه البيئة؛ هذا استنتاج من عدّ الاختبارات لا من تشغيلها.)

---

## 4. المرحلة 2 الكاملة (ملف 3) — خريطة الفجوات

الملف 3 يصف 14 جدولًا أساسيًا + جداول إضافية (فترات، تحويلات، استلام جزئي، جرد) + محرك فترات
+ دورة ترحيل ذرية + متوسط تكلفة مرجح + تقارير + شاشات + اختبارات.
**المنفذ حاليًا: جدول واحد فقط (وهو ليس مطابقًا)، ولا خدمة ولا شاشة ولا اختبار واحد.**

### 4.1 الجداول

| # | الجدول المطلوب | الحالة | ملاحظة |
|---|---|---|---|
| 1 | `customer_categories` | ❌ | غير موجود |
| 2 | `customers` | ❌ | غير موجود (ولا `scope_type` شركة/فرع) |
| 3 | `supplier_categories` | ❌ | غير موجود |
| 4 | `suppliers` | ❌ | غير موجود |
| 5 | `units` | ❌ | غير موجود |
| 6 | `item_categories` | ❌ | غير موجود |
| 7 | `items` | ❌ | غير موجود |
| 8 | `item_units` | ❌ | غير موجود |
| 9 | `item_barcodes` | ❌ | غير موجود |
| 10 | `document_sequences` | ⚠️ | موجود بمخطط مختلف (انظر 4.2) |
| 11 | `stock_documents` | ❌ | غير موجود |
| 12 | `stock_document_lines` | ❌ | غير موجود |
| 13 | `stock_movements` | ❌ | غير موجود |
| 14 | `stock_balances` | ❌ | غير موجود |
| + | `fiscal_periods` · `period_scope_closures` · `period_events` · `period_close_checks` | ❌ | محرك الفترات الموحد غير موجود |
| + | `stock_transfer_details` · `stock_transfer_receipts` · `stock_transfer_receipt_lines` | ❌ | التحويل والاستلام الجزئي غير موجود |
| + | جداول الجرد/التسويات (إن لزم) | ❌ | غير موجود |

### 4.2 تعارض `document_sequences`

| الحقل المطلوب في ملف 3 (§12) | الموجود في 0011 | الأثر |
|---|---|---|
| `tenant_id` | ❌ | لا عزل شركات |
| `fiscal_period_id` | ❌ (`year` فقط) | لا ترقيم لكل فترة مالية |
| `next_number` | `last_number` | تسمية مختلفة (تُحل بـ migration) |
| `number_padding` | ❌ (مثبت 6 خانات داخل الدالة) | لا يمكن للأدمن ضبط الشكل |
| `reset_policy` | ❌ (إعادة سنوية ضمنية) | سياسة إعادة الترقيم غير قابلة للضبط |
| `version` / `updated_by` | ❌ | لا تزامن ولا تتبع |
| `branch_id NULL` (ترقيم على مستوى الشركة) | `NOT NULL` | يمنع ترقيمًا مركزيًا |
| `prefix` | ✅ | موجود |
| قيد فريد على نطاق الترقيم | `(doc_type, branch_id, year)` | لا يشمل الفترة |

**التوصية:** لا تعدّل 0011 (قاعدة المشروع). أضف `0012_document_sequences_v2.sql` يضيف
`tenant_id` (عند اعتماد التعدد) و`fiscal_period_id` و`number_padding` و`reset_policy` و`version`،
ويحوّل `branch_id` إلى NULLable، ويعيد بناء الدالة `next_document_number` بمعاملات جديدة،
مع Backfill آمن (لا توجد مستندات بعد ⇒ النافذة مثالية الآن).

### 4.3 ما هو مطلوب في المرحلة 2 وغير موجود إطلاقًا

- محرك فترات موحد + منع الترحيل داخل فترة مغلقة + إغلاق/إعادة فتح بسبب إلزامي + `period_events`.
- دورة مستند مخزون: مسودة → تحقق → ترحيل ذري → عكس (بلا تعديل حركة مرحّلة).
- متوسط التكلفة المرجح المتحرك لكل (صنف، مخزن) + حفظ التكلفة التاريخية.
- تحويلات بين المخازن/الفروع + بضاعة بالطريق + استلام جزئي متعدد.
- جرد وتسويات + فروق معتمدة.
- تقارير: أرصدة المخزون وكارت الصنف.
- شاشات: عملاء/موردون/وحدات/أصناف/مستندات مخزون/تحويلات/جرد/فترات/تقارير.
- اختبارات: عزل الفروع والشركات، تزامن الترحيل، منع الازدواج، مطابقة الأرصدة بالحركات، الفترات المغلقة.

---


## 5. العيوب والمخاطر — مرتبة بالأولوية

### P0 — تُغلق قبل أي شغل جديد

| # | العيب | الملف | الإصلاح المطلوب |
|---|---|---|---|
| 1 | بيانات اتصال Neon (مالك القاعدة) مكتوبة في `appsettings.json` ومرفوعة في commit `3ce499c` | `src/Ubp.Web/appsettings.json:29-30` | (أ) **دوّر كلمة مرور Neon فورًا** واعتبر القديمة مخترقة. (ب) أزل السطرين وضع القيم في Secrets/ENV. (ج) امسح السر من تاريخ git (`git filter-repo`) أو اعتبر المستودع مكشوفًا. (د) أضف فحصًا في `Program.cs` يفشل الإقلاع لو كانت القيمة تحتوي `neondb_owner` أو كانت مفتاح التطوير مع `ASPNETCORE_ENVIRONMENT=Production`. (هـ) صحّح `docs/SECURITY-AR.md` لأنه ينفي وجود أسرار |
| 2 | تعارض النسخ معطّل في الإعدادات | `SettingsService.cs:49-51` | احذف `input.Id = old.Id; input.Version = old.Version;` واستخدم نسخة الفورم. أبقِ `old` للـ Audit فقط. أضف اختبارًا عبر الخدمة يثبت `ConflictException` عند نسخة قديمة |
| 3 | لا CI فعّال | `ci/ci.yml` | انقل الملف إلى `.github/workflows/ci.yml` (أو أضف Workflow يستدعيه)، وتأكد من `dotnet 10` وخدمة Postgres واسم قاعدة `ubp_test` و`ubp_qa` |
| 4 | `AutoMigrate=true` + اتصال المالك في `appsettings.json` | `appsettings.json:28-33` · `DatabaseBootstrapper` | في الإنتاج: `AutoMigrate=false`، اتصال التطبيق = `ubp_app` (بلا DDL)، والاتصال DDL في Secrets فقط وعبر شاشة/CI |

### P1 — أمن/صحة بيانات

| # | العيب | التفصيل | الإصلاح |
|---|---|---|---|
| 5 | تدوير Refresh Token ينشئ توكنين | `AuthService.Session.cs:118-124` ينشئ توكنًا ثم `IssueAsync` ينشئ آخر ويُنشئ جلسة جديدة؛ الأول يبقى نشطًا 30 يومًا ولا يُعاد للعميل | اجعل `IssueAsync` تقبل `refreshId/refreshRaw` جاهزين، أو أنشئ التوكن داخل `IssueAsync` فقط واستخدم ناتجه في `RevokeAsync(existing, newId)`. وألغِ الجلسة القديمة عند التجديد |
| 6 | الجلسة لا تنتهي داخل دائرة Blazor | `UserContext` Scoped لكل circuit + `_loadTask` مخزّن + الميدلوير يتخطى `/_blazor` | أعد التحقق دوريًا (Timer كل 60 ثانية) أو خزّن `LastValidatedAt` وأعد التحقق عند كل تنقل/عملية، ونفّذ خروجًا قسريًا عند الانتهاء |
| 7 | `/api/v1/system/health` مفتوح للجميع ويكشف اسم السيرفر وقاعدة البيانات وعدد الجداول | `SystemEndpoints.cs:10-23` + `.AllowAnonymous()` | اجعله يعيد `{status}` فقط للمجهول، والتفاصيل لـ Super Admin، أو احمه بمفتاح صحة داخلي |
| 8 | Refresh Token يُعاد في جسم استجابة الـ API | `AuthEndpoints.cs:59, 75` | لا تُعِد التوكن في JSON؛ اكتفِ بكوكي HttpOnly. من يحتاج API يستخدم الكوكي أو تدفقًا منفصلًا |
| 9 | التوقيت والتنسيقات | `date_format`/`time_format`/`number_format` غير مستخدمة؛ التحويل للقاهرة في شاشة السجل فقط؛ فلتر التاريخ يُبنى بـ UTC بينما العرض بالقاهرة ⇒ إزاحة 2-3 ساعات | أنشئ `UiFormat` مركزيًا يقرأ الإعدادات ويحوّل من UTC إلى `Africa/Cairo`، واستخدمه في كل الشاشات، وابنِ حدود فلتر التاريخ بتوقيت القاهرة ثم حوّلها إلى UTC |
| 10 | كلمة مرور دور قاعدة البيانات داخل migration | `0010_app_role.sql:6` (`ubp_app_dev_password`) | اجعل السكريبت ينشئ الدور بلا كلمة مرور، ومرّرها من Secrets عبر سكريبت تشغيلي/CI، أو غيّرها فورًا في الإنتاج |
| 11 | شاشة قاعدة البيانات بلا «إضافة صف» | `DatabaseAdminService.Rows.cs` (Update/Delete فقط) | أضف `InsertRowAsync` بنفس نمط التأكيد (`INSERT table`) + Audit + قائمة بيضاء للجداول |
| 12 | QA لا يطابق المواصفة | 4 جداول من 14، Registry ثابت داخل `QaService.cs:21-32`، النتائج تُخزَّن في قاعدة التطبيق، لا متصفح/تغطية/بوابات/محاولات | انظر خطة التطوير في §7.4 |

### P2 — جودة/اتساق

| # | العيب | الأثر | الإصلاح |
|---|---|---|---|
| 13 | بعد انتهاء القفل يبقى `failed_attempts` عند الحد | خطأ واحد بعد انتهاء القفل يعيد القفل 15 دقيقة | صفّر العداد عندما يكون `locked_until < now()` قبل احتساب المحاولة |
| 14 | `SchemaAdminRepository.GetRowAsync` يفترض عمود `id` | `schemaversions` للقراءة فقط لكن فتح صفها يفشل | استخدم المفتاح الأساسي الفعلي من `information_schema` |
| 15 | أسماء الفروع بالعربي دائمًا | `HeaderBar.razor:35,42` · `Home.razor:73` · `Audit` | استخدم `name_en` عند `Language == "en"` (دالة عرض موحدة) |
| 16 | اللوجو غير معروض في الـ Dashboard | مخالف للخطة | أضف `<img src="@settings.LogoUrl" />` |
| 17 | `SqlSort.Build` يضيف `id` غير مؤهَّل | سيتعارض عند أول استعلام بـ JOIN فيه `id` مشترك | استخدم `{alias}.id` |
| 18 | اختبارات تهدم الـ schema | بطيئة وهشّة | قاعدة اختبار واحدة تُهيَّأ مرة، أو Testcontainers + معاملة لكل اختبار |
| 19 | اسم اختبار `Settings_update_bumps_version_and_is_audited` لا يتحقق من Audit | تغطية مُعلنة غير موجودة | أضف استعلامًا على `audit_log` |
| 20 | `ChangePassword` يلغي الجلسة الحالية بلا تحويل فوري | المستخدم يرى «تم» ثم يُطرد عند أول تنقل | وجّه إلى `/login` بعد النجاح أو أبقِ الجلسة الحالية وألغِ الباقي |

---


## 6. قرارات مطلوبة قبل كتابة أي سطر من المرحلة 2

| # | القرار | لماذا الآن؟ | توصيتي |
|---|---|---|---|
| D1 | **شركة واحدة أم عدة شركات (`tenant_id` + RLS)؟** | ملف 1 يفترض شركة واحدة، وملف 3 يكتب `tenant_id NOT NULL` في كل جدول ويقول «دعم عدة شركات». الاختيار يغيّر كل جدول وكل استعلام وكل اختبار عزل | إن كان البيع لعملاء كخدمة (SaaS): أضف `tenant_id` **الآن** في migration 0012 لكل الجداول القائمة + RLS + `app.tenant_id` في الجلسة. إن كانت شركة واحدة: احذف `tenant_id` من ملف 3 نهائيًا واعتمد `branch_id` فقط |
| D2 | **الترقيم: سنة ميلادية أم فترة مالية؟ ومن يملك النطاق: شركة أم فرع؟** | 0011 يثبت سنة/فرع، وملف 3 يريد فترة/سياسة إعادة | فترة مالية + نطاق قابل للضبط (Company/Branch) + `number_padding` + `reset_policy`؛ يُنفَّذ في 0012 |
| D3 | **محرك الفترات: يُبنى قبل المخزون أم معه؟** | كل ترحيل مخزون يعتمد عليه؛ بناؤه لاحقًا يعني إعادة كتابة الترحيل | ابنِه أولًا: `fiscal_periods` + `period_scope_closures` + `period_close_checks` + `period_events` + خدمة `PeriodGuard` تُستدعى داخل معاملة الترحيل |
| D4 | **معنى «الفرع» للعميل/المورد** (`scope_type` = COMPANY/BRANCH) | يحدد مفاتيح فريدة وصلاحيات العرض | `scope_type` + `branch_id NULL` مع CHECK يمنع فرعًا لنطاق شركة، وقيد فريد `(tenant_id, scope_type, branch_id, code)` |
| D5 | **الأسعار والتكلفة:** ملف 3 يمنع أسعار الصرف، ويطلب متوسطًا مرجحًا | يحدد أعمدة `items` و`item_units` | احفظ `sale_price/purchase_price` كمرجعية فقط، والتكلفة في `stock_balances.average_cost` بدقة `numeric(18,4)` أو أدق |
| D6 | **الترحيل: هل المسودة تحجز رقمًا؟** | يحدد موضع استدعاء `next_document_number` | احجز الرقم **عند الترحيل** لا عند المسودة (يمنع فجوات ويسهّل الإلغاء) |
| D7 | **العكس والجرد: جداول مستقلة أم مستندات بأنواع؟** | يحدد عدد الجداول | استخدم `stock_documents` بأنواع (`RECEIPT/ISSUE/TRANSFER/ADJUSTMENT/OPENING/REVERSAL`) + `reversal_of_document_id`، وأضف جداول التحويل فقط عند الحاجة الفعلية |
| D8 | **حجم كل شريحة تسليم** | «دورة كاملة» في ملف 3 يصعب اختبارها دفعة واحدة | شرائح لا تزيد عن 4-6 جداول، ولكل شريحة شاشة + خدمة + مستودع + اختبارات قبل الانتقال |

---

## 7. خطة التنفيذ المقترحة

### 7.1 Sprint 0 — إصلاحات فورية (قبل المرحلة 2)

- [ ] تدوير سر Neon + إزالة الأسرار من الكود والتاريخ + فحص إقلاع يرفض أسرار الإنتاج.
- [ ] إصلاح تعارض نسخة الإعدادات + اختبار عبر الخدمة.
- [ ] نقل CI إلى `.github/workflows/` وتشغيل build + tests فعليًا لأول مرة.
- [ ] إصلاح تدوير Refresh Token + إلغاء الجلسة القديمة.
- [ ] إعادة التحقق الدوري للجلسة داخل Blazor.
- [ ] تقييد `/api/v1/system/health` وإزالة `refreshToken` من جسم الاستجابة.
- [ ] `UiFormat` مركزي (توقيت القاهرة + تنسيقات الإعدادات) واستخدامه في السجل ولوحة التحكم.
- [ ] إضافة `InsertRowAsync` لشاشة الداتا بيز.
- [ ] `AutoMigrate=false` + فصل مستخدم التطبيق عن مستخدم الترحيل في الإنتاج.

**معيار الخروج:** CI أخضر على GitHub، واختبار يثبت رفض النسخة القديمة، ولا سر في المستودع، وتشغيل `dotnet test` يظهر 33+ اختبارًا.

### 7.2 شرائح المرحلة 2 (مقترح ترقيم migrations)

| الشريحة | المحتوى | Migration | معيار القبول |
|---|---|---|---|
| **2B** — البيانات الأساسية | `units` · `item_categories` · `items` · `item_units` · `item_barcodes` + شاشات + بحث/ترقيم | `0012_items_and_units.sql` | CRUD + منع حذف مستخدم + بحث عربي + 25/صفحة + اختبارات عزل |
| **2C** — الأطراف | `customer_categories` · `customers` · `supplier_categories` · `suppliers` مع `scope_type` | `0013_parties.sql` | عميل شركة/فرع + منع تكرار الكود داخل النطاق + فلاتر |
| **2D** — الفترات | `fiscal_periods` · `period_scope_closures` · `period_close_checks` · `period_events` + `PeriodGuard` + شاشة | `0014_period_engine.sql` | رفض الترحيل في فترة مغلقة + سجل إغلاق/فتح + اختبار سباق |
| **2E** — الترقيم v2 | توسيع `document_sequences` + `next_document_number` الجديدة | `0015_document_sequences_v2.sql` | نطاق شركة/فرع + فترة + padding + إعادة ترقيم + 50 طلبًا متزامنًا |
| **2F** — المستندات والحركات | `stock_documents` · `stock_document_lines` · `stock_movements` · `stock_balances` + الترحيل الذري + العكس | `0016_stock_core.sql` | ترحيل ذري + منع السالب (إلا بالإعداد) + تكلفة مرجحة + منع الازدواج + مطابقة الأرصدة |
| **2G** — التحويلات | `stock_transfer_*` + بضاعة بالطريق + استلام جزئي | `0017_transfers.sql` | لا دخول قبل الاستلام + جمع الكميات = المطلوب + منع التكرار |
| **2H** — الجرد والتقارير | جرد/تسويات + أرصدة + كارت صنف | `0018_stocktake_reports.sql` | فرق معتمد + أثر تكلفة + تقارير مُصفّحة من القاعدة |

### 7.3 قاعدة العمل لكل شريحة (Definition of Done)

1. Migration جديد فقط (لا تعديل لسابق) + فهارس على كل عمود فلترة/فرز.
2. Repository (Dapper) + Service (صلاحية + فرع + تحقق + Audit) + Component (25/صفحة).
3. اختبارات: صلاحية، عزل فرع، تزامن/تعارض نسخة، قاعدة عمل سلبية، وحالة ذهبية.
4. تحديث `CHANGELOG.md` و`PROJECT_STATE.md` و`README` (الجداول/الشاشات).
5. الصفحة تعمل على الموبايل + RTL/LTR + مفاتيح ar/en متطابقة العدد.

### 7.4 تطوير مركز الجودة (لمطابقة QA v2.2)

| المرحلة | المطلوب | ملاحظة على الواقع |
|---|---|---|
| Phase 1 (موجود جزئيًا) | Registry ديناميكي للم screens/actions/tests + مصفوفة + تشغيل فردي/محدد | Registry الحالي قائمة ثابتة؛ اربطه بـ `AppRegistry` تلقائيًا لتغطية كل شاشة/زر |
| Phase 2 | قاعدة اختبار معزولة + بيانات اختبار + اختبارات تكامل | موجود: `QaConnectionString` + فشل آمن عند التطابق. ناقص: بيانات/تنظيف/اختبارات أعمال |
| Phase 3 | Playwright + CI + بوابات إصدار | غير موجود؛ الأنسب: Playwright على مسار حرج واحد أولًا |
| Phase 4 | إعادة المحاولات + Flaky + أداء + اتجاهات | يحتاج جدول `qa_test_attempts` و`qa_quality_gates` (غير موجودين) |
| Phase 5 | توزيع/توسع | أجّلها حتى يوجد داعٍ مقاس |


---

## 8. تعارضات داخل ملفات الورد نفسها (تحتاج تصحيحًا قبل التنفيذ)

| # | التعارض | الموضع | التوصية |
|---|---|---|---|
| 1 | «شركة واحدة» مقابل «دعم عدة شركات» | ملف 1 (app_settings بلا شركة) ↔ ملف 3 (`tenant_id` + سطر 75) | احسم D1 واكتب القرار في رأس ملف 3 |
| 2 | `tenant_id` «إذا كان متوافقًا مع نموذج المرحلة الأولى» | ملف 3 §5.1/5.2 | صياغة غير قابلة للتنفيذ؛ استبدلها بـ «إلزامي/غير مطلوب» نهائيًا |
| 3 | QA v2.2 يفترض React 19 + Node 22 + Vite 6 ويطلب فحص «existing React routes» | ملف 1 (قسم 2 و3) | المواصفة قديمة؛ أعد كتابة هذا الجزء على Blazor/.NET أو ألغِ ما لا ينطبق |
| 4 | QA يطلب قاعدة نتائج منفصلة | ملف 1 §3.1-E | التنفيذ يضع `qa_*` داخل قاعدة التطبيق؛ إما اعتمد ذلك رسميًا أو انقلها لـ schema منفصل |
| 5 | «25 سجلًا/صفحة» لكل الجداول | ملف 1 | استثنِ بوضوح شاشة الداتا بيز (تعرض 25 صفًا لكن مع Pagination مختلفة) — متسق حاليًا |
| 6 | عدد الاختبارات المتوقع 33 | ملف 2 | صحّحه إلى العدد الفعلي بعد تشغيل `dotnet test` |
| 7 | «لا توجد أسرار في المستودع» | `docs/SECURITY-AR.md` | غير صحيح حاليًا؛ صحّحه بعد التدوير |
| 8 | ملف قفل Word مرفوع: `0- Plan/~$Foundation.docx` | المستودع | احذفه وأضف `~$*` إلى `.gitignore` |

---

## 9. ما لم أستطع تأكيده (وكيف تتأكد منه)

| البند | السبب | أمر التحقق |
|---|---|---|
| البناء والاختبارات | لا يوجد .NET SDK في بيئة المراجعة | `dotnet build UniversalBusinessPlatform.slnx -c Release` ثم `dotnet test` (يحتاج Postgres + `UBP_TEST_CONNECTION`) |
| سلوك قاعدة البيانات فعليًا | لا اتصال | شغّل الـ migrations على قاعدة فارغة وراجع `schemaversions` |
| الأداء لـ 100 مستخدم | لا اختبار حمل | سيناريو k6/Playwright على 3 شاشات + قياس p95 على استعلامات القوائم |
| أن السر لم يُستخدم | لا سجلات وصول | راجع Neon Activity / تدوير فوريًا على أي حال |

---

## 10. الخلاصة في خمس نقاط

1. **المرحلة 1 منفذة بجودة هندسية عالية فعلًا** وليست «ورقًا»: الجداول، الصلاحيات، العزل، القفل،
   Keyset، التطبيع العربي، الترحيلات، و32 اختبارًا كلها موجودة في الكود.
2. **المرحلة 2 (العملاء/الموردون/المخزون) لم تبدأ إلا بجدول ترقيم واحد**، وبمخطط لا يطابق مواصفتها؛
   البدء فيها قبل حسم `tenant_id` والفترات سيؤدي لإعادة كتابة.
3. **ثلاثة أخطاء يجب إغلاقها قبل أي تطوير:** تسريب سر Neon، تعارض النسخة المعطّل، وCI غير الفعّال.
4. **أكبر فجوة وظيفية بعد المرحلة 2 هي محرك الفترات والتكلفة والترحيل الذري**؛ هي أساس المحاسبة
   والمبيعات لاحقًا، وتأجيلها يعني تكرار العمل.
5. **مركز الجودة الحالي «أساس» لا «مركز تحكم»**: 9 اختبارات داخل العملية، ولا متصفح/تغطية/بوابات.
   اربطه بـ `AppRegistry` أولًا فتصبح كل شاشة وزر مرئيًا في المصفوفة تلقائيًا.

---

*هذا التقرير مبني على قراءة الكود فقط. أي بند وُصف بـ«✅» يعني «موجود في الكود» وليس «تم التحقق منه بالتشغيل».*

