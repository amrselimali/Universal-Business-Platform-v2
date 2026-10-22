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
