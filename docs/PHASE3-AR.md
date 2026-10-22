# المرحلة 3 — نواة الحسابات والأستاذ العام

> تاريخ التنفيذ: 2026-10-22 · الترحيلات: `0020`–`0022` · اختبارات المرحلة: 20 اختباراً (الإجمالي 72).

---

## 1. ما تم تسليمه

| البند | التفاصيل |
|---|---|
| دليل الحسابات | شجرة بمسار مادي `path` + `level`، أنواع حسابات محاسبية، سياسة مركز تكلفة لكل حساب، حسابات نظامية محمية |
| مراكز التكلفة | شجرة مستقلة، اختيارية الربط بفرع (`NULL = كل الفروع`) |
| دفاتر اليومية | 7 دفاتر نظامية (GEN/SAL/PUR/CSH/BNK/STK/OPN) + دفاتر مخصصة |
| القيود | رأس + بنود، مسودة/مرحّل/ملغي، ترقيم عند الترحيل فقط، عكس بقيد مقابل |
| الأرصدة السريعة | `gl_balances` شهرية (حساب/فرع/مركز تكلفة) و`party_balances` (حساب/فرع/طرف) |
| الشاشات | 13 شاشة: 8 محاسبية + 5 تقارير (انظر الجدول أدناه) |
| الترحيل الآلي للمخزون | من `item_category_accounts` و`posting_defaults`، مع معاينة قبل الترحيل |
| الصلاحيات | كل شاشة/زر مسجّل في `AppRegistry` بمفتاح `accounting.<screen>.<action>` |

### الشاشات والمسارات

| الشاشة | المسار | الأزرار (صلاحيات) |
|---|---|---|
| دليل الحسابات | `/accounting/accounts` | `create_root` · `create_child` · `edit` · `toggle_active` · `move` · `load_default_tree` |
| مراكز التكلفة | `/accounting/cost-centers` | `create_root` · `create_child` · `edit` · `toggle_active` · `move` |
| دفاتر اليومية | `/accounting/journals` | `create` · `edit` · `toggle_active` |
| إعدادات الحسابات | `/accounting/settings` | `save` · `check_completeness` · `enable_auto_post` |
| قائمة القيود | `/accounting/entries` | `create` · `open` · `copy` · `reverse` · `cancel` |
| شاشة القيد | `/accounting/entries/{id}` و`/new` | `save_draft` · `post` · `add_line` · `delete_line` · `auto_balance` · `select_party` · `distribute_cost` · `reverse` · `print` |
| الأرصدة الافتتاحية | `/accounting/opening-balances` | `import_csv` · `auto_balance_equity` · `save_draft` · `post` · `open_entry` |
| مركز قيود المخزون | `/accounting/stock-posting` | `preview` · `generate_single` · `generate_all` · `open_entry` |
| كشف الأستاذ | `/reports/accounting/ledger` | `view` · `print` |
| ميزان المراجعة | `/reports/accounting/trial-balance` | `view` · `print` |
| كشف حساب طرف | `/reports/accounting/party-statement` | `view` · `print` |
| تحليل مراكز التكلفة | `/reports/accounting/cost-centers` | `view` · `print` |
| مطابقة المخزون مع الأستاذ | `/reports/accounting/stock-vs-gl` | `view` · `print` |

---

## 2. القرارات المعمارية

### D1 — الأرصدة الشهرية هي مصدر التقارير
كل ترحيل يكتب في `gl_balances` و`party_balances` داخل نفس المعاملة. ميزان المراجعة يقرأ من
`gl_balances` فقط، وكشف الأستاذ يجمع «رصيد سابق من الأرصدة الشهرية + حركات الفترة»، لذلك لا
يتأثر زمن التقرير بعدد بنود القيود.

### D2 — الترحيل دالة PostgreSQL واحدة
`fn_gl_post_entry(entry, user)` تنفّذ: الترقيم (إن لم يكن موجوداً) → تعليم البنود كمرحّلة →
التوازن والفترة والأطراف ومراكز التكلفة عبر Triggers → تحديث الأرصدة. أي فشل يُرجع المعاملة
كاملة، فلا يمكن أن يوجد قيد مرحّل بلا رصيد أو رصيد بلا قيد.

### D3 — القيود المرحّلة غير قابلة للتعديل (Trigger لا مجرد Service)
`fn_journal_entry_guard` يمنع تعديل/حذف أي قيد غير مسودة، ويسمح فقط بتعليم الأصل `is_reversed`
بعد العكس. `fn_journal_line_guard` يمنع أي بند جديد أو تعديل داخل قيد مرحّل.

### D4 — منع العكس المزدوج على مستوى قاعدة البيانات
فهرس فريد جزئي `ux_journal_entries_reversal (reversal_of_id)` + علامة `is_reversed` + رفض
`ENTRY_ALREADY_REVERSED` داخل `fn_gl_reverse_entry`. لا يكفي فحص الواجهة.

### D5 — الترقيم عند الترحيل فقط
المسودة `entry_no = NULL`، والرقم يُسحب من `document_sequences` بنوع `JOURNAL_<code>` داخل
معاملة الترحيل. المسودات الملغاة لا تستهلك أرقاماً.

### D6 — ربط المخزون بالحسابات
- حساب المخزون من `item_category_accounts.inventory_account_id` (إلزامي).
- حساب مقابل الاستلام `posting_defaults.stock_inventory` (الافتراضي 2150 مخزون مستلم غير مفوتر).
- الصرف: `cogs_account_id`، وفروق الجرد: `adjustment_gain/loss` ثم الافتراضي.
- تُجمَّع البنود المتشابهة في سطر واحد، وفرق التقريب يذهب إلى حساب `rounding` أو أكبر سطر.

### D7 — صلاحيات بصيغة `accounting.<screen>.<action>`
أُضيف `PermissionGroup` إلى `ScreenDefinition`، و`PermissionKey(action)` يولّد المفتاح المعلن
(`accounting.accounts.create_root`)، بينما يبقى مفتاح التخزين `SCREEN/ACTION` كما هو حتى لا
تنكسر جداول الصلاحيات الحالية. اختبار آلي يفشل إذا وُجد زر في الشاشة غير مسجّل في الـ Registry.

---

## 3. قواعد العمل المطبّقة

| القاعدة | مكان التطبيق |
|---|---|
| لا ترحيل لقيد غير متوازن | الخدمة (فحص مبكر) + `fn_journal_post_integrity` + `CHECK` على الرأس |
| لا تعديل/حذف لقيد أو بند مرحّل | `fn_journal_entry_guard` · `fn_journal_line_guard` |
| لا عكس مزدوج | فهرس فريد على `reversal_of_id` + `is_reversed` + رفض `ENTRY_ALREADY_REVERSED` |
| الحساب المجمّع لا يُرحّل عليه | فحص الترحيل + استبعاده من قوائم الاختيار |
| حساب مدينون ⇒ عميل إلزامي، دائنون ⇒ مورد إلزامي | الخدمة + Trigger الترحيل |
| سياسة مركز التكلفة (0/1/2) | الخدمة + Trigger الترحيل |
| لا ترحيل في فترة مغلقة لوحدة ACCOUNTING | `fn_assert_period_open` داخل معاملة الترحيل |
| تغيير كود حساب له أبناء ممنوع | الخدمة (رسالة `validation.account_has_children`) |
| الحساب النظامي لا يُحذف/يُعطّل/يُغيّر نوعه | `fn_account_system_guard` + الخدمة |
| منع الدوران في الشجرة | `fn_account_tree_guard` / `fn_cost_center_tree_guard` + فحص المسار في المستودع |
| ترقيم القيد عند الترحيل فقط | `fn_gl_post_entry` |
| منع تكرار قيد مستند مخزون | فهرس `ux_journal_entries_source` + قفل استشاري لكل مستند |
| مسودة الأرصدة الافتتاحية واحدة لكل فرع | بحث `source_type='opening' AND status='draft'` |
| الطرف على مستوى البند | `party_balances` تُحدّث من `customer_id/supplier_id` في البند |

---

## 4. الأداء

- ميزان المراجعة: استعلام واحد على `gl_balances` مجمّعاً بالشهر/الحساب/الفرع — لا يلمس بنود القيود.
- كشف الأستاذ: شهري (من أول الشهر إلى آخره)؛ الرصيد الافتتاحي وإجماليات الفترة من
  `gl_balances`، والحركات تُقرأ بـ Keyset على `(entry_date, id)` مع حد 50 سطراً للصفحة.
  الترقيم لا يستخدم OFFSET، والصفحة التالية تبدأ من رصيد مُشفّر في المؤشر نفسه.
- الفهارس: `(account_id, entry_date, id) INCLUDE (debit, credit, branch_id)` للبنود المرحّلة،
  و`(period_month, account_id, branch_id)` للأرصدة، و`gin_trgm` للبحث في الحسابات.
- قياس فعلي (اختبار `UBP_LOAD_ROWS=1000000` على PostgreSQL 15 محلي):
  ميزان المراجعة **7ms** وكشف الأستاذ **17ms** على مليون بند.

---

## 5. الاختبارات

| الملف | التغطية |
|---|---|
| `AccountingJournalTests` | توازن، ترحيل، أرصدة شهرية، قفل القيود المرحّلة، عكس ومنع تكراره |
| `AccountingRulesTests` | الطرف الإلزامي، سياسة مركز التكلفة، الفترة المغلقة، إلغاء المسودة |
| `AccountingSetupTests` | الشجرة الافتراضية، النقل والمسارات، منع الدوران، مفاتيح الصلاحيات |
| `AccountingRegistryTests` | أزرار كل شاشة موجودة في الـ Registry، فحص الاكتمال، Keyset بلا تكرار |
| `StockAccountingTests` | قيد الاستلام، الترحيل التلقائي عند ترحيل المستند، مطابقة المخزون |
| `StockAccountingIssueTests` | قيد الصرف على COGS، المستند بلا حسابات لا يُرحّل |
| `AccountingPerformanceTests` | اختياري: مليون بند، ميزان وأستاذ < 0.5 ثانية |

```bash
UBP_TEST_CONNECTION="Host=localhost;Database=ubp_test;Username=postgres" \
UBP_LOAD_ROWS=1000000 \
dotnet test tests/Ubp.Tests --filter "FullyQualifiedName~Accounting"
```

---

## 6. قيود معروفة

1. الأرصدة الافتتاحية شهرية الدقة (GL مجمّع بالشهر)، وكشف الأستاذ يُعرض شهرياً لهذا السبب.
2. لا عملات متعددة على مستوى القيد بعد؛ الأستاذ بالعملة الأساسية.
3. لا ضرائب ولا فواتير مبيعات/مشتريات في هذه المرحلة.
4. مطابقة المخزون مع الأستاذ على مستوى الشركة (أرصدة المخزون لا تحمل فرعاً).
5. استيراد CSV لا يُخزّن مسودة مستقلة: التحليل والتطبيق في نفس الجلسة.
6. لا إعادة تقييم عملة ولا إغلاق سنة مالية تلقائي (يُدار عبر محرك الفترات).

