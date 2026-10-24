# مصفوفة الترحيل والصلاحيات — المرحلة 4/6/7

> المرجع: `journal_entries` + `fn_gl_post_entry` + `posting_defaults` (المرحلة 3).
> كل ترحيل: معاملة واحدة (G8) + فحص فترة `ACCOUNTING` (N25) + فحص جاهزية الموديول (N3).

---

## 1) جاهزية الترحيل حسب الموديول (N3)

| الموديول | مفاتيح إلزامية | مفاتيح اختيارية (تحذير فقط) |
|---|---|---|
| `inventory` | `stock_inventory`, `stock_cogs` | `stock_adjustment_gain`, `stock_adjustment_loss`, `opening_equity`, `rounding` |
| `purchasing` | `supplier_payable` | `vat_input`, `purchase_variance`, `rounding_difference` |
| `sales` | `customer_receivable`, `sales_revenue` | `vat_output`, `rounding_difference` |
| `treasury` | `customer_advances`, `supplier_advances` | `payment_fees`, `cash_over_short`, `inter_branch_clearing` |
| `bookings` | — (لا قيد على الحجز، D2) | `booking_forfeit_income` (عند المصادرة) |

**السلوك:** `PostingReadinessService.RequireAsync(module)` → يقرأ المفاتيح من `posting_default_types` (لا مصفوفة صلبة) + يتحقق أن الحساب `is_active AND NOT is_group` + يخزّن النتيجة في `IMemoryCache` بمفتاح `posting-ready:{module}:{settingsVersion}` ويُمسح عند أي تعديل إعدادات. رسالة الفشل: **`validation.posting_account_missing` مع اسم المفتاح** (لا «إعداد غير مكتمل»).

---

## 2) مصفوفة القيود (مدين / دائن لكل مستند)

| المستند | مدين | دائن | التوقيت |
|---|---|---|---|
| **فاتورة شراء** | المخزون (حساب التصنيف) `net` · `vat_input` `tax` | `supplier_payable` `total` | الترحيل |
| **مرتجع شراء** | `supplier_payable` `total` | المخزون بـ**المتوسط** · `vat_input` · فرق بسعر الفاتورة → `purchase_variance` | الترحيل |
| **إذن صرف/تنفيذ** (N4) | `stock_cogs` `cost` | المخزون `cost` | الترحيل |
| **فاتورة مبيعات** | `customer_receivable` `total` | `sales_revenue` `net` · `vat_output` `tax` | الترحيل |
| **مرتجع مبيعات** | `sales_revenue` `net` · `vat_output` `tax` | `customer_receivable` `total` + `stock_cogs`/المخزون **بتكلفة الأصل** (عكس الإذن) | الترحيل |
| **سند قبض** | حساب طريقة الدفع `net` · `payment_fees` | `customer_advances` `gross` (D5) | الترحيل |
| **تخصيص قبض على فاتورة** | `customer_advances` | `customer_receivable` | التخصيص |
| **سند صرف** | `supplier_payable` (أو `supplier_advances` دفعة مقدمة) · `payment_fees` | حساب طريقة الدفع | الترحيل |
| **تحويل بين خزائن** | خزينة الوجهة | خزينة المصدر | الترحيل |
| **تحويل بين فرعين** | خزينة الوجهة · (`inter_branch_clearing` للفرع الآخر) | خزينة المصدر · `inter_branch_clearing` | الترحيل |
| **فرق وردية (عجز)** | `cash_over_short` | الخزينة | إغلاق الوردية |
| **فرق وردية (زيادة)** | الخزينة | `cash_over_short` | إغلاق الوردية |
| **مصادرة حجز** | `customer_advances` | `booking_forfeit_income` | التسوية |
| **إعدام منتهي الصلاحية** | `stock_adjustment_loss` | المخزون | ترحيل مستند الإعدام |
| **فرق تقريب** | `rounding_difference` | `rounding_difference` | ضمن أي قيد عند وجود فرق |

**الدفاتر:** شراء/مرتجع شراء → `PUR` · مبيعات/مرتجع → `SAL` · سندات → `CSH`/`BNK` حسب نوع الخزينة · مخزون → `STK` · افتتاحي → `OPN` · تسويات → `GEN`.

---

## 3) الضريبة والتقريب (N5)

```
لكل بند:  net        = round(qty × price × (1 - discount%), 2)
          tax        = round(net × rate / 100, 2)          -- لكل بند
          total      = net + tax
رأس:      subtotal   = Σ net
          tax_total  = Σ tax
          total      = subtotal + tax_total
          diff       = total - Σ(net + tax)  →  rounding_difference
```
- `price_includes_tax=true` → `net = round(total_incl / (1 + rate/100), 2)` ثم يُحسب الضريبة كفرق.
- لا يُسمح بتجاوز `absl(diff) > 0.05`؛ وإلا يُرفض الحفظ (`validation.tax_rounding`).
- اختبار إلزامي: فاتورة 3 بنود بقيمة 33.33 ونسبة 14% → القيد متوازن بالقرش.

---

## 4) التكامل مع المخزون (N7)

```
[فاتورة شراء] → stock_documents(RECEIPT, internal=true) → stock_lots(جديدة/تعبئة) → stock_movements
              ← commercial_documents.stock_document_id
[إذن صرف]     → stock_documents(ISSUE) → FEFO من stock_lots → stock_movements
[مرتجع بيع]   → stock_documents(RECEIPT, internal=true) بتكلفة الأصل
[مرتجع شراء]  → stock_documents(ISSUE) بالمتوسط
[إعدام]       → stock_documents(DISPOSAL)
```
**قاعدة:** لا تُكتب `stock_movements` من طبقة الفواتير مباشرة؛ الفاتورة تُنشئ مستند المخزون ثم يستدعي محرك المخزون المُختبر. بذلك يبقى `fn_stock_apply` هو المسؤول الوحيد عن الكميات والتكلفة.

---

## 5) الحجز والتخصيص (D3/D21/N11)

```
confirm(booking) → booking_lines.reserved_qty += qty
                 → stock_lots.quantity_reserved += qty (FEFO تخصيص اختياري)
                 → stock_balances.reserved_qty += qty  (كاش)
deliver(note)    → reserved_qty -= qty  ثم ISSUE من FEFO
cancel(booking)  → reserved_qty -= المتبقي
```
- `available = on_hand - reserved_qty` (لا يُحسب من جدول آخر).
- إذا `reserve_stock_on_booking=false` → لا تُحدَّث `reserved_qty`، ويُسمح بالبيع على المتاح فقط.
- كل تحديث في نفس معاملة ترحيل الحجز/الإذن (G8).

---

## 6) الدفعات المقدمة (D5/N12)

```
سند قبض (فاتورة/حجز) → Dr نقد / Cr customer_advances
فاتورة → تخصيص آلي (إن كان الإعداد) أو يدوي:
         Dr customer_advances / Cr customer_receivable
مرتجع/تسوية → عكس التخصيص ثم استرداد بسند صرف
```
`allocated_total` في الفاتورة = مجموع `document_allocations`؛ `balance = total - allocated_total` هو ما يظهر في أعمار العملاء.

---

## 7) سجل الترحيل والمطابقات (D19)

| العنصر | التنفيذ |
|---|---|
| زر القيد | `commercial_documents.journal_entry_id` → رابط `/accounting/entries/{id}` |
| سجل الترحيل | استعلام موحّد على `commercial_documents`, `vouchers`, `stock_documents`, `delivery_notes` |
| مستندات بلا قيد | View `v_unposted_documents` (UNION) + صلاحية `accounting.posting.view_unposted` |
| مطابقات الأستاذ | ثلاث مقارنات: مخزون (`stock_balances.inventory_value` ↔ `1120`) · عملاء (`Σ open invoices` ↔ `1110`) · موردون (`↔ 2100`) |

---

## 8) مصفوفة الشاشات والصلاحيات الكاملة (AppRegistry)

> الصيغة: `<module>.<screen>.<action>`. التخزين الداخلي يبقى `SCREEN/ACTION` كما في المرحلة 3 (N20).

### 4A-1
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/settings/employees` | الموظفون | `settings.employees.view/create/edit/toggle_active` |
| `/settings/taxes` | الضرائب | `settings.taxes.view/create/edit/toggle_active` |
| `/settings/payment-terms` | شروط الدفع | `settings.payment_terms.view/create/edit/toggle_active` |
| `/settings/price-lists` | قوائم الأسعار | `settings.price_lists.view/create/edit/toggle_active/manage_items` |
| `/inventory/recipes` | وصفات الخدمات | `inventory.recipes.view/edit` |
| `/settings/branches` | الفروع (موجودة) | + `settings.branches.set_consumables_warehouse` |
| `/accounting/settings` | إعدادات المحاسبة (موجودة) | `accounting.settings.view/save/check_completeness/enable_auto_post` |

### 4A-2
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/inventory/lots` | الدفعات والصلاحية | `inventory.lots.view/issue_expired/toggle_hold` |
| `/inventory/disposal` | إعدام المنتهي | `inventory.disposal.create/post/print` |

### 4B
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/purchasing/invoices` | فواتير الشراء | `purchasing.invoices.view/create/edit/post/cancel/print/export` |
| `/purchasing/returns` | مرتجعات الشراء | `purchasing.returns.view/create/post/print` |
| `/purchasing/aging` | أعمار الموردين | `purchasing.aging.view/export` |

### 4C
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/treasury/management` | الخزائن وطرق الدفع | `treasury.management.view/create/manage_methods` ✅ |
| `/treasury/vouchers` | سندات القبض/الصرف/التحويل | `treasury.vouchers.view/create/edit/delete/post/cancel/print` ✅ (print لاحقاً) |
| `/treasury/shifts` | ورديات الكاشير | `treasury.shifts.view/open/close/reopen` ✅ |
| `/treasury/reconciliation` | تسوية الحسابات الوسيطة | `treasury.reconciliation.view/post` ✅ |

**القيود المطبَّقة فعلياً (0027/0028 + `TreasuryRepository`):**
```
قبض  → Dr خزينة/وسيط (المبلغ - المصروف) + Dr payment_fees / Cr customer_advances [الطرف]
صرف  → Dr supplier_advances [الطرف] + Dr payment_fees / Cr خزينة/وسيط (المبلغ - المصروف)
تحويل (نفس الفرع)   → Dr خزينة الوجهة / Cr خزينة المصدر (+ المصاريف على payment_fees)
تحويل (بين الفروع)  → قيد المصدر: Dr inter_branch_clearing / Cr الخزينة
                      قيد الوجهة: Dr خزينة الوجهة / Cr inter_branch_clearing
تخصيص دفعة على فاتورة → Dr advances / Cr receivable (أو Dr payable / Cr advances للمورد)
إغلاق وردية → الفرق: زيادة Dr الخزينة / Cr cash_over_short، عجز Dr cash_over_short / Cr الخزينة
```

> ملاحظات تنفيذ: تُحسب المصاريف تلقائياً من طريقة الدفع (نسبة + مبلغ ثابت) ما لم يتجاوزها السند،
> والترقيم عند الترحيل فقط، وتُرحَّل كل العمليات أعلاه داخل معاملة واحدة مع فحص الفترة المالية.
> أسعار التخصيص تُقفل المستند: `allocated_total` وحده قابل للتحديث على مستند مرحّل، وبقية الحقول محمية.

### 4D
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/sales/bookings` | الحجوزات | `sales.bookings.view/create/edit/confirm/cancel/assign_others` ✅ |
| `/sales/appointments` | جدول المواعيد اليومي | `sales.appointments.view/manage` ✅ |
| `/sales/deliveries` | إذون الصرف والتنفيذ | `sales.deliveries.view/execute/post` ✅ |

> استهلاك وصفات الخدمات (`consumption_mode` + البدائل) مؤجَّل؛ الجدول `service_consumptions`
> موجود والعقد محدد. الفاتورة التلقائية عند التنفيذ (D4) تُنفَّذ مع 4E.

### 4E
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/sales/invoices` | فواتير المبيعات | `sales.invoices.view/create/edit/post/cancel/print` |
| `/sales/returns` | مرتجعات المبيعات | `sales.returns.view/create/post/print` |
| `/sales/customers/quick` | عميل سريع | `sales.customers.quick_create` |

### 4F
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/sales/settlements` | التسويات | `sales.settlements.view/create/approve` |
| `/sales/commissions` | عمولات الموظفين | `sales.commissions.view/calculate` |
| `/reports/aging` | الأعمار الأربعة | `reports.aging.view/export` |
| `/accounting/posting-center` | سجل الترحيل والمطابقات | `accounting.posting.view/view_unposted/reconcile` |

### 4G / 4H
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/pos/terminal` | نقطة البيع | `pos.terminal.view/checkout` |
| `/pos/receipts` | إيصالات POS | `pos.receipts.view/print` |
| `/sales/follow-ups` | المتابعات | `sales.followups.view/create/complete` |

### 6
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/reports/center` | مركز التقارير | `reports.center.view/save_filter/export_csv/export_xlsx/print_pdf` |
| `/dashboard` | لوحة المؤشرات | `dashboard.view/kpi_financials/kpi_inventory` |
| `/notifications/center` | الإشعارات | `notifications.view/mark_as_read/clear_all` |

### 7
| المسار | الشاشة | الأزرار |
|---|---|---|
| `/control/tenants` | إدارة العملاء | `control.tenants.view/create_db/toggle_status/run_migrations` |
| `/admin/import-tool` | الاستيراد الشامل | `admin.import.view/preview/execute` |
| `/admin/system-health` | صحة النظام | `admin.system.health_view/run_backup` |
| `/audit` | سجل الأنشطة (**موجود**) | `AUDIT_LOG/VIEW` — لا صلاحية جديدة (N13) |


---

## 9) نقاط القبول والاختبارات لكل جزء

### 4A-1
- [ ] `SettingsValidationTests` القديمة تمرّ (لم تُمس بنية `app_settings`).
- [ ] `PostingDefaultTypes_cover_all_used_keys` — كل مفتاح في الكود موجود في الجدول المرجعي.
- [ ] `Readiness_blocks_only_the_requested_module` — نقص مفتاح مبيعات لا يمنع ترحيل مخزون.
- [ ] `Readiness_cache_invalidates_on_settings_save`.
- [ ] `Employee_flags_and_unique_code` · `Tax_rate_bounds` · `PriceList_default_unique`.
- [ ] `Recipe_consumption_mode_and_substitutes`.
- [ ] `Registry_buttons_match_screens` (نمط المرحلة 3 على الشاشات الجديدة).

### 4A-2
- [ ] `Backfill_sum_equals_balances` — بعد الترحيل: `Σ lots.qty = on_hand` لكل (صنف، مخزن).
- [ ] `FEFO_issues_nearest_expiry_first`.
- [ ] `Tracked_item_never_negative` (يرفض الصرف الزائد).
- [ ] `Expired_lot_is_not_issued` + `Disposal_posts_loss`.
- [ ] `Non_tracked_item_uses_average_cost_path`.
- [ ] أداء: صرف 100 صنف بدفعات ≤ 300ms.

### 4B
- [ ] `Purchase_invoice_posts_balanced_entry_and_receipt_lot`.
- [ ] `Purchase_return_uses_average_and_variance`.
- [ ] `Posted_invoice_immutable` (Trigger).
- [ ] `Aging_supplier_report_matches_allocations`.
- [ ] `Invoice_party_check_rejects_two_parties`.

### 4C
- [ ] `Receipt_voucher_multi_method_balances`.
- [ ] `Card_payment_goes_to_clearing_then_reconciliation`.
- [ ] `Shift_close_computes_variance_and_posts`.
- [ ] `Transfer_between_branches_uses_clearing`.
- [ ] `One_open_shift_per_treasury` (فهرس فريد).

### 4D
- [ ] `Booking_reserves_available_quantity`.
- [ ] `Booking_over_available_requires_new_booking`.
- [ ] `Appointment_overlap_is_rejected` + `Cancelled_appointment_frees_slot`.
- [ ] `Delivery_consumes_FEFO_and_posts_cogs`.
- [ ] `Service_recipe_consumption_three_modes`.
- [ ] `Assignment_history_is_recorded`.

### 4E
- [ ] `Sales_invoice_requires_booking_line` (Trigger D3).
- [ ] `Invoice_consumes_customer_advance` + `Aging_customer_reflects_allocation`.
- [ ] `Sales_return_uses_original_cost` + `Return_qty_guard`.
- [ ] `Auto_invoice_on_delivery_creates_linked_invoice` (D4).
- [ ] `Tax_rounding_keeps_entry_balanced` (N5).

### 4F
- [ ] `Settlement_five_types_post_correct_accounts`.
- [ ] `Forfeit_posts_to_booking_forfeit_income`.
- [ ] `Commission_basis_three_modes`.
- [ ] `Four_aging_reports_totals_reconcile`.
- [ ] `Unposted_documents_view_lists_all_types`.

### 4G
- [ ] `Pos_checkout_creates_booking_invoice_and_payment_in_one_tx`.
- [ ] `Pos_failure_rolls_back_everything`.

### 6
- [ ] `Xlsx_export_opens_and_matches_grid` (فحص ZIP + XML).
- [ ] `Dashboard_under_300ms` (بيانات يوم واحد).
- [ ] `Notification_bell_counts_unread`.
- [ ] `Outbox_retries_with_backoff_and_no_duplicates`.
- [ ] `Bi_views_refresh_and_reader_has_select_only`.

### 7
- [ ] `Tenant_create_runs_migrations_and_seeds`.
- [ ] `Tenant_isolation_no_cross_db_read`.
- [ ] `Import_preview_reports_errors_without_writes` + `Import_execute_transactional`.
- [ ] `Backup_log_records_and_restore_verified`.
- [ ] `Load_1M_lines_reports_under_500ms` + `100_concurrent_users`.
- [ ] `No_secrets_in_deployed_config` + `Dapper_all_parameterized`.

---

## 10) ملاحظات تنفيذية تُغلق الفجوات المعروفة

| # | الملاحظة | مكانها |
|---|---|---|
| 1 | كل مرحلة تضيف مفاتيح `posting_default_types` الخاصة بها في migration مستقل | 0027 / 0032 |
| 2 | أسماء المستندات في `document_sequences` تُسجَّل مع أول مستند (لا seed مسبق) | `next_document_number` |
| 3 | الواجهة تحوّل `status` القديم (uppercase للمخزون) إلى الجديد (lowercase) في طبقة العرض فقط | `N18` |
| 4 | `customer_advances` تُستخدم للحجز أيضاً؛ التخصيص على الفاتورة لاحقاً | `N12` |
| 5 | أي شاشة تُعيد استخدام `SecurePageBase` + `PermissionService` بلا استثناء | G11 |
| 6 | ممنوع `SELECT *` وممنوع استعلام بلا حد أقصى للصفوف | G13 (جديد) |
| 7 | كل مستند مرحّل له `journal_entry_id` أو يُدرج في `v_unposted_documents` | D19 |

