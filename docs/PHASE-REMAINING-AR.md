# خطة الأجزاء المتبقية — من 4C-ب إلى 7H

> **مرجع ملزم:** مُشتقّة حرفياً من `PHASE4-AR.md` (خريطة المراحل) و`PHASE4-SCHEMA-AR.md`
> (كتالوج المخطط) و`PHASE4-POSTING-AR.md` (مصفوفة الترحيل والصلاحيات) و`PHASE4-DECISIONS-AR.md`
> (D/N/O)، بعد تنفيذ **4C-أ** (الترحيل `0027` و`0028`).
> **لا تُغيّر** بنية الكود القائمة (Repository partial / Service / Screen / AppRegistry) ولا أي قيد
> قائم؛ كل جزء يضيف ملفات جديدة بنفس الأنماط، وأي إعداد يُضاف يجب أن يكون له **مستهلك فوري**
> (قاعدة الإعداد الميت في سجل القرارات).

---

## 0) لقطة الحالة قبل البدء

| البند | الحالة |
|---|---|
| المراحل 1 · 2 · 3 | مكتملة ومُختبرة |
| 4A-1 · 4A-2 · 4B | مكتملة (`0024` · `0025` · `0026`) |
| **4C-أ** | مكتملة: خزائن + طرق دفع + سندات + تخصيص + ورديات + 3 شاشات + 6 اختبارات (`0027` · `0028`) |
| **4C-ب** | تسوية الحسابات الوسيطة · إلغاء/طبع السند · إعادة فتح الوردية · زر التخصيص |
| 4D → 7H | لم يبدأ (وهذه الوثيقة تحدّده) |
| الاختبارات | 96/96 على PostgreSQL حقيقي · 27 سكريبت ترحيل · صفر NuGet |

**الترتيب الإلزامي للتبعيات:** 4C-ب → 4D → 4E → 4F → 4G (و4H بالتوازي مع 4E) → 6A → 6B/6C/6D → 6E → 7A-0/7A → 7B → 7C → 7D → 7E → 7F → 7G → 7H.

---

## 1) تصحيح ترقيم الترحيلات (إلزامي قبل أي كود)

الخطة القديمة كانت تحجز `0028` لـ4D، لكن `0028` استُهلك في ترحيل قيود الخزينة. الترقيم المعتمد:

| الجزء | الرقم القديم | **الرقم المعتمد** | محتوى السكريبت |
|---|---|---|---|
| 4C-ب | — | `0029_phase4c_reconciliation.sql` | فهارس/مفاتيح تسوية الحسابات الوسيطة (لا جداول جديدة) |
| 4D | `0028` | `0030_phase4d_bookings.sql` | `btree_gist` · bookings · booking_lines · booking_assignments · appointments · delivery_notes · delivery_note_lines · service_consumptions |
| 4E | `0029` | `0031_phase4e_sales.sql` | أعمدة `booking_id`/`booking_line_id`/`delivered_at` · `sales_return_links` · حارسا D3 وN24 |
| 4F | `0030` | `0032_phase4f_settlements.sql` | `commission_basis` · `booking_settlements` · `employee_commissions` · مفتاح `booking_forfeit_income` |
| 4G | `0031` | `0033_phase4g_pos.sql` | فهرس POS فقط (لا جداول) |
| 4H | `0032` | `0034_phase4h_followups.sql` | `follow_ups` |
| 6 | `0033` | `0035_phase6_reports_notifications.sql` | saved_report_filters · notifications · message_outbox · sales_daily_totals · مخطط `bi` |
| 7 | `0034` | `0036_phase7_operations.sql` | backup_log · import_jobs (وControl DB منفصل) |

> سبب التصحيح: G1 (لا تعديل على ترحيل منفَّذ) + N26 (أسماء المستندات ≤ 30 حرفاً) + منع تعارض رقمين لنفس الملف.

---

## 2) 4C-ب — إغلاق الخزينة ✅ (مُنجَز)

### 2.1 الشاشات والأزرار

| المسار | الشاشة | الأزرار/الصلاحيات | الحالة |
|---|---|---|---|
| `/treasury/reconciliation` | تسوية الحسابات الوسيطة (بطاقات/محافظ/شيكات) | `treasury.reconciliation.view` · `treasury.reconciliation.post` | جديد |
| `/treasury/vouchers` | سند قبض/صرف/تحويل (قائم) | `+ treasury.vouchers.cancel` · `+ treasury.vouchers.allocate` | تعديل شاشة قائمة |
| `/treasury/shifts` | ورديات الكاشير (قائم) | `+ treasury.shifts.reopen` | تعديل شاشة قائمة |

### 2.2 المتطلبات

- **التسوية:** قائمة الحسابات الوسيطة (`treasury_methods.clearing_account_id`) مع رصيدها من
  `gl_balances` (مدين/دائن) لكل فرع؛ زر «تسوية» ينشئ قيداً واحداً: `Dr البنك/الخزينة · Cr الوسيط`
  (أو العكس عند رصيد سالب) بتاريخ التسوية ومرجعها، مع `fn_assert_period_open` + `fn_gl_post_entry`
  في معاملة واحدة. لا جداول جديدة؛ `source_type='voucher'` و`source_id=NULL`.
- **إلغاء السند:** للمسودة فقط (`UPDATE ... SET status='cancelled' WHERE status='draft'`)، والملغى
  لا يُرحّل ولا يُحذف. إلغاء سند مرحّل **غير مسموح** ويحتاج قيد عكس (مؤجل بقرار صريح).
- **الطبع:** طباعة المتصفح الحالية (`PRINT` مسجَّلة) — لا PDF داخل التطبيق (D23).
- **إعادة فتح الوردية:** للمغلقة فقط، بسبب إلزامي، عكس قيد الفرق بـ`fn_gl_reverse_entry` ثم
  `status='reopened'`، وإعادة حساب المتوقع لاحقاً؛ الوردية المعاد فتحها تُغلق مرة أخرى.
- **زر التخصيص:** داخل شاشة السندات لسند قبض/صرف **مرحّل**؛ يعرض فواتير الطرف المفتوحة
  (`total - allocated_total > 0`) ويستدعي `TreasuryService.AllocateAsync` الموجود.

### 2.3 دورة العمل (مطابقة Odoo)

```
Odoo: POS Session → Cash Control → Bank/Card settlement
نحن : وردية مفتوحة → سندات نقدية/بطاقات → إغلاق (فرق العهدة) → تسوية الوسيط إلى البنك
```
— الوسيط لا يبقى مفتوحاً: كل تسوية تُصفِّر حساب البطاقة/المحفظة مقابل البنك، وهو ما يقابل
`Bank Reconciliation` في Odoo لكن بقيود يدوية مُتحقَّق منها.

### 2.4 الاختبارات
- [ ] `Clearing_settlement_posts_balanced_entry_and_zeroes_account`
- [ ] `Draft_voucher_can_be_cancelled_and_posted_voucher_cannot`
- [ ] `Reopen_shift_reverses_variance_and_allows_reclose`
- [ ] `Allocation_action_lists_only_open_invoices`

---

## 3) 4D — الحجوزات والمواعيد وإذون التنفيذ (`0030`) ✅ (مُنجَز، عدا استهلاك الوصفة والفاتورة التلقائية)

### 3.1 الجداول

| الجدول | الغرض | قيود أساسية |
|---|---|---|
| `bookings` | الالتزام (بديل Sales Order) | `status ∈ draft/confirmed/partially_delivered/completed/cancelled/expired` · `source ∈ manual/pos` · `related_booking_id` للزيادة · `deposit_amount` |
| `booking_lines` | بنود الحجز | `quantity > 0` · `delivered_qty / invoiced_qty / cancelled_qty / reserved_qty` · `UNIQUE(booking_id, line_no)` |
| `booking_assignments` | سجل إسناد تاريخي (D8) | `role ∈ owner/executor` · `unassigned_at` |
| `appointments` | جدول المواعيد | `EXCLUDE USING gist (employee_id, tstzrange)` حيث `status <> 'cancelled'` (D10) |
| `delivery_notes` + `delivery_note_lines` | إذن الصرف/التنفيذ | `stock_document_id` (N7) · `invoice_id` (D4) · `unit_cost` snapshot |
| `service_consumptions` | استهلاك وصفة الخدمة (D14/D15) | `consumption_mode` + بدائل من `recipe_substitutes` |

**إعدادات مستهلكة في نفس الجزء:** `reserve_stock_on_booking` · `appointment_slot_minutes` ·
`auto_invoice_on_delivery` · `invoice_grouping_mode` (فوري/عند الإغلاق/يدوي).

### 3.2 الشاشات والأزرار

| المسار | الشاشة | الأزرار/الصلاحيات |
|---|---|---|
| `/sales/bookings` | الحجوزات | `sales.bookings.view/create/confirm/cancel/assign_others` |
| `/sales/appointments` | الجدول اليومي | `sales.appointments.view/manage` |
| `/sales/deliveries` | إذون الصرف والتنفيذ | `sales.deliveries.view/execute/post` |

### 3.3 دورة العمل (مقابلة Odoo)

```
Odoo : Quotation → Sales Order → Delivery (Picking) → Invoice
نحن  : مسودة حجز → تأكيد (حجز المتاح + فحص حد المبيعات) → مواعيد → إذن صرف
        → تنفيذ (FEFO + استهلاك وصفة) → ترحيل (مخزون + COGS) → [فاتورة تلقائية أو يدوية]
```
- التأكيد **لا** ينشئ قيداً (D2)؛ الإيراد عند الفاتورة، والتكلفة عند التنفيذ (N4).
- الإلغاء قبل التنفيذ فقط؛ بعده يحتاج تسوية في 4F.

### 3.4 المتطلبات التنفيذية

- المتاح = `on_hand - reserved_qty` (N11) وليس جدولاً جديداً؛ الحجز على التأكيد فقط وعند تفعيل الإعداد.
- `confirmed` → `partially_delivered`/`completed` تلقائياً من مجموع `delivered_qty` مقابل `quantity`.
- إذن الصرف يُنشئ `stock_documents` داخلياً (نمط `CreateStockDocumentAsync` القائم) ثم يستهلكه
  بمحرك المخزون (`StockPostingRepository`) داخل نفس المعاملة.
- الخدمات: لا تُصرف من المخزن؛ تُستهلك مكوّناتها من **مخزن المستهلكات** بالمتوسط، والأنماط ثلاثة
  (ثابت/قابل للتعديل/يدوي) مع بدائل (D15/O5).
- منع تداخل المواعيد على مستوى القاعدة (`EXCLUDE`) + استثناء الملغي.
- فحص الفترة `fn_assert_period_open` داخل كل ترحيل (N25) · عدم قابلية التعديل بعد الترحيل (G8).
- الترقيم: `BOOK` و`DN` عبر `next_document_number` عند الترحيل/التأكيد.

### 3.5 الاختبارات
- [ ] `Booking_reserves_available_quantity`
- [ ] `Booking_over_available_requires_new_booking`
- [ ] `Appointment_overlap_is_rejected` + `Cancelled_appointment_frees_slot`
- [ ] `Delivery_consumes_FEFO_and_posts_cogs`
- [ ] `Service_recipe_consumption_three_modes`
- [ ] `Assignment_history_is_recorded`

**التقدير:** 6–8 أيام (سكيبتان Repository/Service على نمط 4B + 3 شاشات + 6 اختبارات).

---

## 4) 4E — فواتير المبيعات والمرتجعات (`0031`) ✅ (مُنجَز)

### 4.1 الجداول/التغييرات

| العنصر | الغرض |
|---|---|
| `commercial_documents.booking_id` + `delivered_at` | ربط الفاتورة بإذن التنفيذ (D4) |
| `commercial_document_lines.booking_line_id` | إلزامي لبنود المبيعات (D3) |
| `sales_return_links` | تتبّع الكمية المرتجعة لكل بند أصلي (N24) |
| `fn_sales_line_booking_guard` | يرفض بند مبيعات بلا `booking_line_id` |
| حارس المرتجع | `Σ returned_qty ≤ original.qty` + تحديث `returned_qty` |

### 4.2 الشاشات والأزرار

| المسار | الشاشة | الأزرار/الصلاحيات |
|---|---|---|
| `/sales/invoices` | فواتير المبيعات | `sales.invoices.view/create/edit/post/cancel/print` |
| `/sales/returns` | مرتجعات المبيعات | `sales.returns.view/create/post/print` |
| حوار | عميل سريع (D7) | `sales.customers.quick_create` |

### 4.3 دورة العمل وقواعد الترحيل

```
إنشاء (يدوي أو تلقائي من إذن) → تخصيص الدفعة المقدمة → ترحيل:
Dr customer_receivable [الطرف]   / Cr sales_revenue + Cr vat_output
تخصيص مقدم: Dr customer_advances / Cr customer_receivable
مرتجع: Dr sales_revenue + Dr vat_output / Cr customer_receivable
        + إدخال مخزون بتكلفة الأصل (D22) وتحديث returned_qty
```
- الترحيل يستهلك `customer_advances` عبر `document_allocations` القائم (D5/N12).
- فرق تقريب الضريبة إلى `rounding_difference` (N5) وقيد متوازن إلزامي.
- حد المبيعات (`credit_limit` + `customer_branch_limits`) يُفحص عند الإنشاء لا عند الترحيل.

### 4.4 الاختبارات
- [ ] `Sales_invoice_requires_booking_line` (Trigger D3)
- [ ] `Invoice_consumes_customer_advance` + `Aging_customer_reflects_allocation`
- [ ] `Sales_return_uses_original_cost` + `Return_qty_guard`
- [ ] `Auto_invoice_on_delivery_creates_linked_invoice` (D4)
- [ ] `Tax_rounding_keeps_entry_balanced` (N5)

**التقدير:** 5–6 أيام (يعيد استخدام `CommercialDocumentRepository` و`PurchaseInvoiceService` بنمطه).

---

## 5) 4F — التسويات والعمولات والأعمار ومركز الترحيل (`0032`) — التالي

### 5.1 الجداول/التغييرات

| العنصر | الغرض |
|---|---|
| `app_settings.commission_basis` | 1=صافي · 2=إجمالي · 3=المُحصّل (O3) |
| `booking_settlements` | خمسة أنواع: `refund · cancel_qty · refund_and_cancel · transfer · forfeit` + `approved_by/at` (D17) |
| `employee_commissions` | `base_amount · rate · amount · period_month` + `UNIQUE(employee, line, month)` (D20) |
| مفتاح `booking_forfeit_income` | ترحيل المصادرة إلى إيراد |

### 5.2 الشاشات والأزرار

| المسار | الشاشة | الأزرار/الصلاحيات |
|---|---|---|
| `/sales/settlements` | التسويات | `sales.settlements.view/create/approve` |
| `/sales/commissions` | عمولات الموظفين (كشف فقط) | `sales.commissions.view/calculate` |
| `/reports/aging` | الأعمار الأربعة | `reports.aging.view/export` |
| `/accounting/posting-center` | سجل الترحيل والمطابقات | `accounting.posting.view/view_unposted/reconcile` |

### 5.3 دورة العمل والقيود

```
استرداد        : Dr customer_advances / Cr treasury (سند صرف مرتبط)
إلغاء كمية     : تحرير reserved_qty + عكس التخصيص إن وُجد
استرداد+إلغاء  : الاثنان في معاملة واحدة
تحويل لحجز آخر : تسوية الرصيد بين الحجزين (related_booking_id)
مصادرة         : Dr customer_advances / Cr booking_forfeit_income
```
- الموافقة إلزامية (`approved_by/at`) قبل الترحيل، والصلاحية `sales.settlements.approve` منفصلة.
- العمولة تُحسب شهرياً حسب `commission_basis` وتُعرض كشفاً فقط، بلا مرتبات (D20).
- مركز الترحيل: `v_unposted_documents` (UNION على `commercial_documents` · `vouchers` ·
  `stock_documents` · `delivery_notes`) + ثلاث مطابقات (مخزون/عملاء/موردون) مع فرق واضح.

### 5.4 الاختبارات
- [ ] `Settlement_five_types_post_correct_accounts`
- [ ] `Forfeit_posts_to_booking_forfeit_income`
- [ ] `Commission_basis_three_modes`
- [ ] `Four_aging_reports_totals_reconcile`
- [ ] `Unposted_documents_view_lists_all_types`

**التقدير:** 4–5 أيام.

---

## 6) 4G — نقطة البيع (`0033`)

لا جداول جديدة: `bookings(source='pos')` + `commercial_documents(source_type='pos')` + `vouchers`.

| المسار | الشاشة | الأزرار/الصلاحيات |
|---|---|---|
| `/pos/terminal` | نقطة البيع | `pos.terminal.view/checkout` |
| `/pos/receipts` | إيصالات POS | `pos.receipts.view/print` |

**دورة العمل (معاملة واحدة):** اختيار عميل (أو عميل سريع) → أصناف/خدمات → طرق دفع (نفس
`treasury_methods`) → `checkout` = حجز مؤكد `source='pos'` + إذن تنفيذ فوري + فاتورة مرحّلة +
سند قبض مرحّل + تخصيص تلقائي. أي فشل = تراجع كامل (اختبار `Pos_failure_rolls_back_everything`).
**الاختبارات:** `Pos_checkout_creates_booking_invoice_and_payment_in_one_tx` · `Pos_failure_rolls_back_everything`.
**التقدير:** 4–5 أيام.

---

## 7) 4H — المتابعات (`0034`)

| المسار | الشاشة | الأزرار/الصلاحيات |
|---|---|---|
| `/sales/follow-ups` | المتابعات | `sales.followups.view/create/complete` |

`follow_ups` مرتبطة بالعميل/الحجز/الموعد مع قناة (`call/whatsapp/sms/visit`) وحالة
(`pending/done/cancelled`) و`due_at`، والإعداد `auto_follow_up_overdue` يولّد متابعة تلقائية عند
تجاوز الموعد (مستهلكه: خدمة يومية خفيفة عند 6D أو فحص عند فتح الشاشة).
**التقدير:** 2–3 أيام.

---

## 8) المرحلة 6 — التقارير والإشعارات (`0035`)

| الجزء | الشاشة/المخرج | الأزرار/الصلاحيات | المتطلبات |
|---|---|---|---|
| 6A | `/reports/center` | `reports.center.view/save_filter/export_csv/export_xlsx/print_pdf` | مصدّر XLSX يدوي داخل ZIP (D23/N17) + CSV + طباعة + `saved_report_filters` (N23) |
| 6B | مخطط `bi` | صلاحية قراءة فقط `bi_reader` | Materialized Views + `REFRESH CONCURRENTLY` + Default Privileges |
| 6C | `/dashboard` | `dashboard.view/kpi_financials/kpi_inventory` | `sales_daily_totals` + استعلامات < 300ms |
| 6D | `/notifications/center` | `notifications.view/mark_as_read/clear_all` | جرس + عدّاد غير المقروء (N22) |
| 6E | خدمة خلفية | — | `message_outbox` + Backoff + `dedupe_key` + مزوّد تجريبي (N21) |

**الاختبارات:** `Xlsx_export_opens_and_matches_grid` · `Dashboard_under_300ms` ·
`Notification_bell_counts_unread` · `Outbox_retries_with_backoff_and_no_duplicates` ·
`Bi_views_refresh_and_reader_has_select_only`.
**التقدير:** 15–16 يوماً (6A 4 · 6B 2–3 · 6C 3 · 6D 2 · 6E 3–4).

---

## 9) المرحلة 7 — التشغيل (`0036` + Control DB)

| الجزء | الشاشة | الأزرار/الصلاحيات | المتطلبات |
|---|---|---|---|
| 7A-0/7A | `/control/tenants` | `control.tenants.view/create_db/toggle_status/run_migrations` | قاعدة تحكم + `tenants` + توجيه Subdomain (O2) + migrations لكل عميل — **معلّق على O1** |
| 7B | — | — | CI/CD + `schemaversions` + نسخة قبل الترقية |
| 7C | `/admin/system-health` | `admin.system.health_view/run_backup` | `backup_log` + job خارجي + استرجاع مُتحقَّق (N15/O7) |
| 7D | `/admin/system-health` | — | Health Checks + Serilog + بطيء الاستعلامات |
| 7E | `/admin/import-tool` | `admin.import.view/preview/execute` | `import_jobs` + معاينة بلا كتابة + تنفيذ بمعاملة (يعيد استخدام `OpeningBalanceService`) |
| 7F | — | — | حمل 1M بند + 100 مستخدم عبر harness .NET داخل المستودع (O8) |
| 7G | — | — | أمان: تدوير أسرار Neon الحالية في `appsettings.json` (R10) + مراجعة صلاحيات + Parameterized Dapper |
| 7H | — | — | تشغيل تجريبي وموازٍ + أدلة + خطة تراجع |

**التقدير:** 20–24 يوماً (7A-0 3 · 7A 3–4 · 7B 2–3 · 7C 2 · 7D 2–3 · 7E 4 · 7F 2–3 · 7G 2 · 7H 3–4).

---

## 10) خريطة إعادة الاستخدام (بلا تغيير هيكل)

| الحاجة | يُعاد استخدامه كما هو |
|---|---|
| مستند تجاري (بيع/شراء/مرتجع) | `CommercialDocumentRepository` partials + `CommercialDocumentService` |
| ترحيل الأستاذ | `fn_gl_post_entry` · `fn_assert_period_open` · `CreateEntryAsync` بنمطه |
| المخزون وFEFO | `StockPostingRepository` · `StockDocumentRepository` |
| الخزينة والدفع | `TreasuryRepository` (Vouchers/Post/Shifts) · `TreasuryService` |
| الصلاحيات والشاشات | `SecurePageBase` · `AppRegistry` · `PermissionService` · `AccountingRegistryTests` |
| الترقيم | `next_document_number` (N26) |
| الإعدادات | `app_settings` صف واحد + `posting_default_types` (G2/G3) |
| التقارير | `AccountingReportRepository` + نمط Keyset |
| الاستيراد | `OpeningBalanceService` + `CustomFieldService.Validation` |


---

## 11) ما لا يُمسّ أثناء تنفيذ أي جزء (قيود ملزمة)

| القيد | المرجع |
|---|---|
| لا تعديل على ترحيل منفَّذ — كل تغيير سكريبت جديد مرقّم | G1 |
| لا `tenant_id` ولا RLS قبل قرار O1 | G7 / D1 |
| `app_settings` صف واحد بأعمدة مُصنّفة | G2 / N1 |
| `posting_defaults` توسيع فقط + نوع جديد في `posting_default_types` | G3 |
| جاهزية الترحيل تُفحص وقت الترحيل ومخبأة وتُبطل عند الحفظ | G4 |
| سقف 300 سطر لكل ملف `.cs`/`.razor` | G5 |
| صفر حزم NuGet جديدة (XLSX يدوي) | G6 / D23 / N17 |
| كل ترحيل في معاملة واحدة + منع تعديل المرحّل | G8 |
| `version` في كل جدول قابل للتعديل + `WHERE version=@v` | G9 |
| `Guid.CreateVersion7()` من التطبيق | G10 |
| كل شاشة/زر في `AppRegistry` + اختبار تطابق | G11 |
| `fn_assert_period_open(..., 'ACCOUNTING')` داخل كل ترحيل | N25 |
| أسماء مستندات `^[A-Z][A-Z0-9_]*$` وطول ≤ 30 | N26 |
| لا `SELECT *` ولا استعلام بلا حد أقصى | G13 |

---

## 12) بوابة الإنجاز لكل جزء (DoD)

1. Migration جديد رقمه من الجدول أعلاه + `MigrationRunner` يشغّله على قاعدة نظيفة.
2. Repository partial (≤ 300 سطر) + Service بالصلاحيات والتدقيق.
3. شاشات + حوارات + كل زر مسجَّل في `AppRegistry` + مفاتيح `ar.json`/`en.json` متطابقة.
4. اختبارات القبول المذكورة للجزء + نجاح **كل** الاختبارات القديمة.
5. تحديث `CHANGELOG.md` · `PROJECT_STATE.md` · الجدول المقابل في `PHASE4-POSTING-AR.md`.
6. `dotnet build` بصفر أخطاء · فحص 300 سطر · صفر NuGet · مراجعة `git diff` قبل الدفع.

---

## 13) القرارات المفتوحة (تُحسم قبل الجزء المرتبط)

| # | القرار | التوصية | الجزء المتأثر |
|---|---|---|---|
| O1 | تعدد العملاء الآن أم تجميده؟ | **تجميد** للإطلاق الأول + 7A-0 بعد الإطلاق | 7A |
| O2 | توجيه العميل Subdomain/Header | Subdomain | 7A |
| O3 | `commission_basis` | نعم (1/2/3) | 4F |
| O4 | حدود مبيعات فرعية | نعم `customer_branch_limits` | 4D/4E |
| O5 | D15 كامل (3 أنماط + بدائل) | نعم | 4D |
| O6 | تصميم الطرف في الجدول الموحد | N6 (عمودان + CHECK) — **مطبَّق فعلاً** | — |
| O7 | النسخ الاحتياطي | job خارجي + `backup_log` | 7C |
| O8 | أداة الحمل | harness .NET داخل المستودع | 7F |

> عند عدم الرد تُطبَّق التوصية وتُوثَّق «اعتُمد افتراضياً في …»، ولا يبدأ جزء يعتمد على قرار معلّق.

---

## 14) الترتيب والجهد الإجمالي

| المجموعة | الأجزاء | التقدير |
|---|---|---|
| إغلاق 4C | 4C-ب | 2 أيام |
| دورة البيع | 4D · 4E · 4F · 4G · 4H | 21–27 يوماً |
| التقارير | 6A → 6E | 15–16 يوماً |
| التشغيل | 7A-0 → 7H | 20–24 يوماً |
| **الإجمالي المتبقي** | | **58–69 يوم عمل لمطوّر واحد** (≈ 4–5 أسابيع بفريق 3 مع تداخل 6/7) |

**الدفعة التالية المقترحة (تبدأ فوراً):** `4C-ب` كاملة — سكريبت `0029` + `TreasuryRepository.Reconcile.cs`
+ `TreasuryService` (صلاحيتان) + شاشة `/treasury/reconciliation` + زر التخصيص في شاشة السندات +
4 اختبارات، ثم `4D` مقسّمة إلى: `BookingRepository.cs` · `BookingRepository.Lines.cs` ·
`DeliveryRepository.cs` · `AppointmentRepository.cs` + `BookingService` + `DeliveryService` +
3 شاشات + 6 اختبارات.


