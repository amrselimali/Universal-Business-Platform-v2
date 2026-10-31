# خطة المرحلة 8 — الأصول الثابتة والعدادات والصيانة + الحجوزات والدفعات + الفروع والصلاحيات

> مرجع تنفيذي. يلتزم بالثوابت: C# 14 · .NET 10 · Blazor Server · Dapper · DbUp · PostgreSQL ·
> **≤ 300 سطر لكل ملف** · **صفر حزم NuGet** · **لا تعديل ترحيل منفَّذ** · **D1 (شركة واحدة بلا tenant_id)**.
> آخر ترحيل منفَّذ: `0045` ⇒ الخطة تبدأ من **`0046`**.

## 0) ما هو موجود فعلاً (لا نُعيد بناءه)

| المطلوب | الحالة الحقيقية في الكود/القاعدة |
|---|---|
| كميات الحجز لكل سطر | ✅ `booking_lines`: `quantity · reserved_qty · delivered_qty · invoiced_qty · cancelled_qty` |
| الدفعة المقدمة للحجز | ✅ `bookings.deposit_amount` + `expires_at` |
| أرصدة الأصناف | ✅ `stock_balances` (on_hand) |
| كشف حساب عميل/مورد · دفتر أستاذ · ميزان مراجعة | ✅ `/reports/accounting/party-statement` · `/ledger` · `/trial-balance` — **تدعم فرعاً واحداً فقط** |
| أعمار الذمم | ✅ `/reports/aging` + `SalesFinanceService` |
| خزائن مرتبطة بالفرع | ✅ `treasuries.branch_id` + `treasury_type` (بلا مفهوم «رئيسية») |
| صلاحيات لكل مستخدم بنطاق | ✅ `user_permissions(user_id, screen_code, action_code, is_allowed, scope)` — **بلا شاشة إدارة** |
| فحص نطاق الفرع في الكتابة | ✅ `BranchScope.Ensure` على 11 مساراً |
| قيود وسيطة بين الفروع | ✅ `inter_branch_clearing` |

## 1) الفجوات المطلوبة (ملخص)

| # | المجال | ناقص | الأولوية |
|---|---|---|---|
| 1 | الأصول الثابتة | جدول أصول + إهلاك + استبعاد/بيع + تبويب عدادات | P1 |
|   | عدادات الأصول | قراءات عدّاد، ربط باستهلاك العميل، تصفير عند الصيانة | P1 |
|   | الصيانة | خطط صيانة (زمنية/بعدّاد) + أوامر صيانة + تكلفة محاسبية | P1 |
| 2 | الحجوزات | شاشة «كميات محجوزة/مستهلكة/متبقية» لكل عميل + الرصيد البيعي | **P0** |
|   | الدفعات | زر «تحويل المتبقي غير المستخدم إلى دفعة مقدمة» + كشف حجوزات ودفعات | **P0** |
| 3 | الفروع | خزينة **رئيسية** + تحويل فرع↔رئيسية | P1 |
|   | التقارير | اختيار **فرع/عدة فروع/كل الفروع** بدل فرع واحد | **P0** |
|   | حسابات الفروع | «ربط الشجرة بالفروع» = قرار تصميم (انظر 5) | قرار |
| 4 | الصلاحيات | شاشة إدارة `user_permissions` (مصفوفة مع النطاق) | P1 |
| 5 | احترافي عام | أوامر شراء/استلام · بنوك وكشوف · ضرائب متقدمة · عملات على المستندات · باركود · FIFO | P2 |



## 2) المرحلة 8أ — الأصول الثابتة والعدادات والصيانة (`0046`)

### 2.1 الجداول
| الجدول | الغرض | أهم الأعمدة |
|---|---|---|
| `asset_categories` | تصنيف الأصل + حساباته | `code, name_ar, depreciation_method(LINEAR/UNITS), useful_life_months, asset_account_id, accum_account_id, expense_account_id, maintenance_expense_account_id, is_metered` |
| `fixed_assets` | سجل الأصل | `code, name_ar, category_id, branch_id, vendor_id, purchase_date, cost, salvage_value, status(ACTIVE/IDLE/DISPOSED), in_service_date, is_metered, meter_type(PAGES/COPIES/HOURS/UNITS), current_meter_value, meter_resets_on_maintenance, disposed_at, disposal_amount, journal_entry_id` |
| `asset_meter_readings` | قراءات العدّاد | `asset_id, reading_at, value, delta, source(MANUAL/SALES/MAINTENANCE_RESET), booking_id, note` |
| `asset_maintenance_plans` | خطة لكل جهاز | `asset_id, kind(TIME/METER), interval_days, interval_units, next_due_date, next_due_meter, is_active` |
| `asset_maintenance_orders` | أمر صيانة | `asset_id, plan_id, maintenance_date, cost, vendor_id, action(PREVENTIVE/CORRECTIVE), status(OPEN/DONE/CANCELLED), reset_meter_value, journal_entry_id, notes, closed_at` |
| `asset_depreciation_runs` | ترحيل إهلاك شهري | `period_month, branch_id, total_amount, journal_entry_id` + فريد `(period_month, branch_id)` |
| `asset_depreciation_lines` | سطر إهلاك | `run_id, asset_id, amount, accumulated_after` + فريد `(asset_id, period_month)` = **منع إهلاك مزدوج** |

كل جدول: `id uuid PK` + `created_at/created_by/updated_at/updated_by` + `version` وtrigger `fn_touch_version` حيث ينطبق،
و`CHECK` على كل `status/kind/method` (نفس نمط المشروع).

### 2.2 قواعد العمل
1. **أصل بعدّاد** (`is_metered = true`): كل استهلاك يُسجَّل في `asset_meter_readings`؛ ومصدر `SALES` عندما يُستهلك أثناء البيع
   (ربط اختياري على مستوى الصنف/الوصفة: `items.metered_asset_id` أو اختيار «الجهاز» في سطر البيع).
2. **أصل بلا عدّاد**: لا قراءات ولا تبويب عدّاد؛ يُهلَك زمنياً فقط.
3. **الصيانة قد تُصفّر العدّاد**: عند `CLOSE_ORDER` على أصل `meter_resets_on_maintenance = true` ⇒ قراءة `MAINTENANCE_RESET = 0`
   وتُحدَّث `current_meter_value = 0`؛ وإلا تُسجَّل الصيانة دون تصفير.
4. **الإهلاك**: شهري و**idempotent** بالقيد الفريد، داخل معاملة واحدة (قيد مصروف/مجمع لكل أصل)، ويتوقف عند بلوغ المتبقي القابل للإهلاك.
5. **الاستبعاد/البيع**: قيد (نقدية/مجمع إهلاك/الأصل + ربح أو خسارة) ولا يُسمح به إلا لأصل `ACTIVE`.
6. كل ترحيل يتحقق `fn_assert_period_open(date, branch_id, 'ACCOUNTING')` مثل بقية النظام.

### 2.3 الشاشات والأزرار (كل زر بصلاحية في `AppRegistry`)
| الشاشة | المسار | الأزرار (Permissions) |
|---|---|---|
| الأصول | `/assets/management` | `VIEW · ADD · EDIT · DISPOSE · EXPORT_CSV` |
| العدادات | `/assets/meters` | `VIEW · ADD_READING` (للأصول المعدودة فقط) |
| الصيانة | `/assets/maintenance` | `VIEW · ADD_PLAN · OPEN_ORDER · CLOSE_ORDER · CANCEL_ORDER` |
| الإهلاك | `/assets/depreciation` | `VIEW · RUN` |

- `SecurePageBase` ⇒ صلاحية + فلترة `Actor.AllowedBranchIds`، فلا يرى المستخدم إلا أصول فرعه.
- تبويب العدادات **يستبعد** الأصول غير المعدودة؛ ولها تبويب منفصل للزمنية.

### 2.4 الاختبارات
`depreciation_is_idempotent_per_month` · `metered_consumption_creates_reading` ·
`maintenance_resets_meter_only_when_configured` · `disposed_asset_stops_depreciating` ·

## 3) المرحلة 8ب — الكميات المحجوزة والاستهلاك والدفعات المقدمة (`0047`)

### 3.1 ما يُضاف (لا نكرّر الجداول الموجودة)
| الكائن | الغرض |
|---|---|
| `v_customer_reservations` (VIEW) | لكل (عميل، صنف، فرع): `reserved_qty · consumed_qty (delivered+invoiced) · remaining_qty · reserved_value · deposit_amount · unapplied_deposit` |
| `v_item_sellable` (VIEW) | لكل (صنف، مخزن): `on_hand · reserved · sellable = on_hand - reserved` |
| `reservation_adjustments` (جدول) | سجل تحويلات: `booking_id, booking_line_id, kind(RELEASE/EXTEND/TO_ADVANCE), quantity, amount, voucher_id, journal_entry_id, created_by` + فريد `(booking_line_id, kind)` لتفادي التكرار |

### 3.2 الشاشة `/sales/reservations` (هي مطلبك الأساسي)
| العمود | المصدر |
|---|---|
| العميل / الفرع / الصنف | `bookings + booking_lines` |
| محجوز | `booking_lines.reserved_qty` |
| مُستهلك (مُسلَّم/مفوتر) | `delivered_qty + invoiced_qty` |
| **المتبقي غير المستخدم** | `reserved_qty - (delivered + invoiced)` |
| قيمته | `remaining_qty × unit_price` |
| الدفعة المقدمة / المتاح كمقدمة | `bookings.deposit_amount` − المخصوم فعلياً |
| الحالة/الانتهاء | `bookings.status`, `expires_at` |

**الأزرار (صلاحيات):**
`VIEW` · `RELEASE` (تحرير حجز) · `EXTEND` (تمديد صلاحية) · **`TO_ADVANCE`** (تحويل المتبقي غير المستخدم إلى دفعة مقدمة) · `EXPORT_CSV` · `PRINT`.

**قاعدة التحويل إلى مقدمة:** يُنشئ سند قبض (أو قيد) بقيمة `remaining_qty × unit_price` على حساب «دفعات العملاء المقدمة»
(`posting_defaults.customer_advances`) مرتبطاً بالحجز، **بمعاملة واحدة** مع تحديث `reservation_adjustments`؛ ويعيد استخدام
`delivery_note`/`commercial_documents` القائمة إن أمكن لتفادي ازدواج القيود. لا تحويل مرتين لنفس السطر (فهرس فريد).

### 3.3 الشاشات المساندة
- `/sales/reservations/detail` — كشف حجز واحد: سطور، دفعات، تحويلات، وقيد كل تحويل.
- إضافة عمودين للبيع: «متاح للبيع» و«محجوز» في شاشة الأصناف/المخزون (`v_item_sellable`).
- تنبيه: عند `expires_at` تُنشأ إشعارات (`notifications`) وتظهر في تبويب «حجوزات منتهية».

### 3.4 الاختبارات
`reservation_view_matches_booking_lines` · `to_advance_posts_once_and_updates_totals` ·
`sellable_equals_on_hand_minus_reserved` · `expired_reservation_is_flagged_not_deleted` ·
`branch_user_sees_only_own_branch_reservations`.


## 4) المرحلة 8ج — الفروع: خزينة رئيسية + تقارير متعددة الفروع (`0048`)

### 4.1 خزينة رئيسية
| التغيير | التفصيل |
|---|---|
| `treasuries.is_main boolean` | فريد جزئي: `UNIQUE (is_main) WHERE is_main` ⇒ خزينة رئيسية واحدة فقط |
| تحويل فرع ↔ رئيسية | نفس مسار السندات الحالي (تحويل) مع قيد وسيط `inter_branch_clearing` (موجود) |
| الشاشة | `/treasury/management` تُضاف أعمدة «رئيسية؟» + زر `SET_MAIN` (صلاحية جديدة) |
| قاعدة | لا حذف/تعطيل للرئيسية قبل تعيين بديلة (Trigger) |

### 4.2 تقارير فرع/عدة فروع/كل الفروع (P0)
- **تغيير التوقيع**: `Guid? branchId` ⇒ `IReadOnlyList<Guid>? branchIds` في:
  `TrialBalanceAsync · LedgerAsync · PartyStatementAsync · CostCenterAsync · StockVsGlAsync · Aging · StockBalances`
  مع فلتر SQL `(@branchIds IS NULL OR <col> = ANY(@branchIds))`.
- **شاشة**: مكوّن مشترك `BranchScopePicker` (MudSelect متعدد + «كل الفروع» للمسموح له) يُستخدم في كل التقارير.
- **الصلاحية**: مدير الفرع يرى فروعه فقط (تُقصّ القائمة من `Actor.AllowedBranchIds` server-side، لا في الواجهة فقط).

### 4.3 «كل حسابات الشجرة مرتبطة بالفروع» — قرار تصميم (يحتاج حسمك)
| الخيار | الأثر | التوصية |
|---|---|---|
| **أ) الشجرة عاملة عامة + الفرع بُعد على القيود** (الوضع الحالي) | تقارير دقيقة بالفرع عبر `journal_entries.branch_id`؛ صيانة واحدة للشجرة؛ متوافق مع D1 | ✅ **موصى به** |
| ب) شجرة مستقلة لكل فرع (`accounts.branch_id`) | تضاعف الشجرة والصيانة والتسوية، ويكسر تقارير المجموعة | ❌ لا أنصح |
| ج) وسيط `branch_account_defaults` (حسابات افتراضية لكل فرع: خزينة/مخزون/إيراد/دفعات) | يمنح كل فرع حساباته الافتراضية دون تكرار الشجرة | ✅ يُضاف مع (أ) |

**المقترح:** (أ) + (ج): جدول `branch_account_defaults(branch_id, key, account_id)` + شاشة في `/settings/branches`
+ استخدامه في `posting_defaults` عند الترحيل (الفرع أولاً ثم العام).

## 5) المرحلة 8د — الصلاحيات وإظهار الشاشات/البيانات حسب المستخدم والفرع (`0049`)

| الشاشة | المسار | التفصيل |
|---|---|---|
| صلاحيات المستخدم | `/users/permissions` | مصفوفة **الشاشات × الأزرار** لكل مستخدم مع نطاق (`OWN/BRANCHES/ALL`) تُكتب في `user_permissions` (الموجودة) |
| صلاحيات الدور | `/roles/permissions` | نفس المصفوفة لدور عبر `role_permissions` |
| قواعد العرض | — | القائمة تُبنى من `AppRegistry` **مقصوصة على صلاحيات المُدخِل** (مدير الفرع لا يرى شاشات لا يملكها) |

**إلزامات لا تُخترق (تُختبر آلياً):**
1. كل زر في الواجهة يمر عبر `Can(actionCode)` — اختبار يمسح `.razor` ويتأكد من وجود الصلاحية في `AppRegistry`.
2. كل استعلام قوائم يمر بفلتر فرع (مراجعة + اختبار لكل شاشة رئيسية).
3. لا تُرسل بيانات فرع غير مسموح إلى الواجهة إطلاقاً (القص في الخدمة لا في المكوّن).

## 6) الناقص الاحترافي المتبقي (P2 — بعد 8أ–8د)

| المجال | المطلوب | الملاحظة |
|---|---|---|
| المشتريات | طلب عرض → أمر شراء → استلام → فاتورة | إعادة استخدام `stock_documents` + `commercial_documents` |
| البنوك | حسابات بنكية + كشف + مطابقة | حينها يُضاف `BANK` لوحدات الفترات (`PeriodModules`) |
| الضرائب | ضريبة شاملة/غير شاملة + حجز + تقرير ضريبي | `taxes` موجودة؛ يلزم محرك احتساب وتقرير |
| العملات | `currency_id` + سعر صرف + إعادة تقييم | العمود `currency` نصي حالياً بلا سعر صرف |
| المخزون | FIFO + باركود + إعادة طلب | الباركود موجود (`item_barcodes`) بلا شاشة |
| المنصة | API موثّق + مرفقات + سجل نشاط + تصدير شامل | بلا حزم جديدة |

## 7) الترتيب والجهد (مطوّر واحد)

| الحزمة | المحتوى | الترحيل | التقدير |
|---|---|---|---|
| 8ب | الحجوزات + الدفعات المقدمة + الرصيد البيعي | `0047` | **4–5 أيام** |
| 8ج | تعدد الفروع في التقارير + الخزينة الرئيسية + حسابات الفرع الافتراضية | `0048` | 3–4 أيام |
| 8د | شاشتا صلاحيات + اختبارات الإظهار/الفرع | `0049` | 2–3 أيام |
| 8أ | الأصول + العدادات + الصيانة + الإهلاك | `0046` | **8–10 أيام** |
| P2 | مشتريات/بنوك/ضرائب/عملات | `0050+` | 20+ يوماً |
| **الإجمالي 8ب+8ج+8د** | متطلبك الأساسي | `0047–0049` | **9–12 يوم عمل** |

**DoD لكل حزمة:** ترحيل جديد فقط · ≤300 سطر · صفر حزم · كل زر في `AppRegistry` · اختبارات القبول المذكورة أعلاه خضراء +
عدم انحدار الـ 229 · تحديث `CHANGELOG`/`PROJECT_STATE`/`PHASE0-RESULTS`.

## 8) قرارات تحتاج حسمك قبل البدء
1. **الدفعة المقدمة**: تُحوَّل تلقائياً عند انتهاء الحجز، أم بزر يدوي فقط؟ (التوصية: يدوي + تنبيه آلي)
2. **عداد الأصل**: يُصفَّر مع كل صيانة، أم لكل نوع صيانة إعداد مستقل؟ (التوصية: إعداد على الأصل)
3. **حسابات الفروع**: (أ)+(ج) كما في 4.3؟
4. **الأصول**: تخصيص لكل فرع أم مشتركة بين الفروع؟ (التوصية: لكل فرع مع إمكانية «مشترك» بلا فرع)
5. **الإهلاك**: شهري فقط أم ربع سنوي أيضاً؟ (التوصية: شهري، مع تشغيل يدوي للفترة)

`branch_user_sees_only_their_assets`.
