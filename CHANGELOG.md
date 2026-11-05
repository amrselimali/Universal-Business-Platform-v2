# CHANGELOG

## 2026-11-05 — Opening = treasury linked-account balance only

### Changed
- «أول المدة» لكل خزينة أصبح **رصيد حساب الخزينة المرحّل فقط** (للخزائن/طرق التحصيل المعلَّمة
  «يتم متابعتها في الوردية»)، وأُزيل إضافة إيصالات الورديات غير المرحّلة.

## 2026-11-05 — Opening recalculation for all shifts and approve without data

### Fixed
- «أول المدة» يُعاد حسابه لكل وردية (مفتوحة أو مغلقة) = الرصيد المرحّل بالحسابات وقت الفتح
  (= الرصيد الآن − الحركات **المرحّلة** منذ الفتح) + **إيصالات (مسودات) الورديات غير المرحّلة**.
- زر «اعتماد المراجعة» يعمل الآن حتى لو الوردية بلا أي بيانات/بنود مراجعة فتتغير حالتها إلى
  «تمت المراجعة».

## 2026-11-05 — Cash-count opening uses unreviewed shift receipts

### Fixed
- «أول المدة» لكل خزينة = **رصيد الخزينة المرحّل بالحسابات** + **إيصالات التحصيل في الورديات
  غير المرحّلة** (وليس رصيد/عدّ الورديات كما كان).

## 2026-11-05 — Shifts list columns and explicit post/approve flow

### Changed
- شاشة الورديات: أُلغيت أعمدة **النقد المتوقع/المعدود/الفرق**، وأُضيف عمودان:
  **أرصدة الخزائن** المسجّلة في الوردية و**أرصدة الأجهزة** المسجّلة.
- زر **«ترحيل الوردية للحسابات»** يحوّل الوردية المغلقة إلى **تحت المراجعة** (حتى لو بلا بيانات)،
  ثم يظهر زر **«اعتماد المراجعة»** الذي يرحّل فروق عدّ النقدية ويعتمدها.

## 2026-11-05 — Opening balances fix, full-width separators and post-to-accounts button

### Fixed
- «أول المدة» في عدّ النقدية أصبح يجلب **رصيد الحسابات المرحّلة المربوطة بالخزائن** فعلياً:
  للورديات المفتوحة تُحسب اللقطة وقت العرض = رصيد الخزينة المرحّل − حركة الوردية + تحصيلات
  الورديات غير المرحّلة (كانت تبقى صفراً إذا أُنشئت اللقطة متأخرة).

### Changed
- الفاصل بين أجزاء التقرير أصبح **خطاً كامل عرض الصفحة** بعلامة `--------`.
- زر صريح **«ترحيل الوردية للحسابات»** في شاشة الورديات لكل وردية مغلقة تحت المراجعة
  (يرحّل فروق عدّ النقدية ويعتمد المراجعة).

## 2026-11-05 — Cash count opening column and expected formula

### Changed
- جزء عدّ النقدية: عمود **أول المدة** = رصيد الخزينة المرحّل بالحسابات + مجموع تحصيلات الورديات
  غير المرحّلة (عدّ النقدية المسجّل يدوياً).
- **النقد المتوقع = أول المدة + تحصيلات الوردية − المنصرف** لكل خزينة (في العرض والإغلاق وترحيل الفرق).

## 2026-11-05 — Shift view/close fixes: separators, treasury/meter data, editable fields

### Fixed
- إظهار **كل خزائن الفرع المتابَعة** في عدّ النقدية حتى للورديات المفتوحة قبل الترحيل 0076
  (تُنشأ صفوف العدّ تلقائياً عند العرض/الإغلاق).
- إظهار **عدادات الأجهزة** (بما فيها الأجهزة التي لها قراءات سابقة) مع أول/مستهلك/آخر/الفعلي/الفرق.
- **الملاحظات** و**النقد المعدود** و**القراءة الفعلية** أصبحت قابلة للكتابة/التعديل من شاشة
  **عرض الوردية** أيضاً (وليس الإغلاق فقط)، والقراءات الفعلية تُحفظ بزر مستقل في جدول الأجهزة.
- عنوان التقرير يستخدم **تاريخ الفتح** بدل «وقت الفتح».

### Changed
- فواصل نصية `----------` بين كل جدول وآخر في عرض/إغلاق الوردية.

## 2026-11-05 — Shift report: expenses, treasury type, live cash count, editable notes

### Added
- جزء **المصروفات التشغيلية** في عرض/إغلاق الوردية: نوع الخزينة المنصرف منها، القيمة، الصنف (لو
  مخزني)، العدد، سعر الوحدة، والبيان مع الإجمالي.
- عمود **نوع الخزينة** في سطر التحصيل.
- **ملاحظات قابلة للكتابة والحفظ** في جدول الوردية وتظهر معها كل مرة.

### Changed
- عنوان التقرير: **«تقرير وردية — رقم — تاريخ الفتح — تاريخ الإقفال — المستخدم — الفرع»**،
  وأُزيلت الكتلة السفلية (الافتتاحي المجمع/الفرع/أمين الصندوق/وقت الفتح/المتوقع).
- **النقد المتوقع = المحصل − المنصرف** لكل خزينة (وليس افتتاحي + حركة).
- جدول عدّ النقدية أصبح **قبل الملاحظات مباشرة**، وفيه خانة إدخال للنقد المعدود في وضع الإغلاق.

## 2026-11-05 — Shift report rework + POS collection buttons

### Changed
- «عرض الوردية» للوردية المفتوحة يعرض نفس بيانات شاشة الإغلاق: عدّ النقدية لكل خزينة **بالمتوقع
  المحسوب** (لقطة الافتتاح + حركة الخزينة)، ثم الترحيلات والملاحظات.
- أُلغيت حركة الخزائن وجزء «تسجيلات المراجعة» من التقرير والإغلاق.
- التحصيلات والإيرادات في **جدول واحد مرتب بالعميل** بدون رقم السند، بأعمدة: المبلغ المحصل،
  قيمة الإيراد، الكمية، عداد الوحدات، الموظف المسؤول.
- جزء مستقل لعدادات الأجهزة: أول المدة / المستهلك / آخر المدة / **القراءة الفعلية (التي تُرحَّل)** /
  الفرق لكل جهاز مسجل في الفرع.
- مربع نص للملاحظات، ونقل «عملاء بلا متابعة» و«حجوزات الغد» و«مواعيد الموظفين» إلى **نهاية التقرير**.

### Added
- أزرار حوار «تحصيل الخدمات المحجوزة» مثل الإيصال العادي: **ترحيل مسودة / تحصيل / واتساب / إتمام**،
  والمسودة لا تُرحّل أي قيد ولا تخصيص سداد.

## 2026-11-05 — POS draft posting and full customer outstanding in collections

### Added
- زر **«ترحيل مسودة»** في نقطة البيع: يُنشئ إيصالات القبض **مسودات** (`DraftOnly`) بلا أي قيد
  محاسبي ولا تحديث «المسدد سابقاً» — لا أثر على الحسابات حتى يرحّلها المحاسب.
- زر **«تحصيل الخدمات المحجوزة»** يعرض الآن **كل المبالغ غير المسددة للعميل** في الفرع:
  الحجوزات (أي خدمة/بضاعة، وليس الخدمات فقط) **+ فواتير الخدمات المنفذة/الإيرادات** المفتوحة،
  والتحصيل يُخصَّص على الفواتير عبر `document_allocations` وعلى الحجوزات عبر `booking_payments`.
- Tests: `PosDraftTests` (1) — يتحقق أن المسودة لا تنشئ قيداً ولا `booking_payments`.

## 2026-11-05 — Shift carry per treasury, per-treasury cash count (draft) and over/short account

### Added
- Migration `0076`: `shift_cash_counts` (opening snapshot at open, expected/counted/variance at close,
  status `open → draft → posted`) and `treasuries.over_short_account_id`.
- «فتح وردية»: أُلغيت أعمدة «من الحسابات المرحّلة» و«غير مرحّل»، ويبقى **عمود الإجمالي** فقط =
  رصيد الخزينة من **الحسابات المرحّلة** + **أرصدة الشيفتات غير المرحلة المسجّلة يدوياً عند الإغلاق**
  (عدّ النقدية) — وليس مجموع الإيصالات.
- «إغلاق الوردية»: جدول عدّ النقدية بأعمدة **الخزينة / النقد المتوقع / النقد الفعلي / الفرق**،
  ويُحفظ **مسودة** حتى يعتمد المحاسب — لا يُرحَّل أي قيد زيادة/عجز عند الإغلاق.
- اعتماد المحاسب (`APPROVE`): يرحّل فرق كل خزينة على **حساب زيادة/عجز الخزينة** المحدد في
  «الخزائن وطرق الدفع» (أو الافتراضي `cash_over_short` عند تركه فارغاً).
- «الورديات»: إجراء **عرض الوردية** لكل وردية، عمود **إجمالي تحصيلات الوردية** (كل سندات القبض
  خلالها أياً كان نوع الخزينة)، وفلتر **من تاريخ → إلى تاريخ** (افتراضي آخر 30 يوماً).

### Changed
- أُزيل إجراء **إعادة فتح الوردية** من شاشة الورديات (يبقى في الخدمة للتصحيح الإداري).
- Tests: `ShiftBalancesTests` (+1 over/short account posting), تحديث اختبارات الإغلاق/إعادة الفتح
  لعدّ النقدية لكل خزينة؛ full suite (356).

## 2026-11-05 — Shift gate: no more crash on «تنفيذ» for reservation users

### Fixed
- «الخدمات المحجوزة» → «تنفيذ» was throwing a raw `validation.shift_required` from the execution
  dialog constructor. The dialog now catches `AppException`, shows the clear message and closes,
  so the screen keeps working instead of crashing.
- Shift gate became branch-aware: an open shift is accepted when it belongs to **the branch**, to
  **the user himself**, or to **any of the user's authorized branches** (data stays scoped to those
  branches/warehouses), instead of only the session branch.
- Tests: `ShiftBalancesTests` (3 — followed methods, per-device meters, gate acceptance rules);
  `TreasuryShiftTests` back under the 300-line cap; full suite (356).

## 2026-11-05 — Shift opening balances per followed treasury/method, method editing, booking filters and reception-user linking

### Added
- Migration `0075`: `treasury_methods.follow_in_shift` — a switch on **every registered payment method**
  («يتم متابعتها في الوردية»).
- «الخزائن وطرق الدفع» (`/treasury/management`): **تعديل** أي طريقة مسجّلة + **زرار المتابعة في الوردية**
  لكل طريقة (يُحفظ فوراً).
- «فتح وردية»: أُلغيت خليتا **الرصيد المرحل من الحسابات** و**رصيد الورديات تحت المراجعة**، وتُعرض الآن:
  - جدول **الخزائن وطرق الدفع المتابَعة في الوردية** مع رصيد كل خزينة **من الحسابات المرحّلة** +
    **غير المرحّل (تحت المراجعة)** والإجمالي، والرصيد الافتتاحي = مجموعها (إجباري/غير قابل للتعديل)؛
  - **عداد كل جهاز في خلية مستقلة** (حتى لو صفر) مع نوع العدّاد.
- «حجز موعد» (`/sales/bookings`): فلتر **بحث بالعميل** (الاسم/الكود/رقم التليفون/رقم الحجز) افتراضيه فارغ،
  فلتر حالة **3 خيارات فقط** (مسودة/مؤكد/ملغى) متعدد الاختيار وافتراضيه **مسودة + مؤكد**، وفلتر
  **من تاريخ → إلى تاريخ** افتراضيه **تاريخ اليوم**.
- ربط الشاشات بالمستخدم حسب **الفرع والمخزن المصرح به**: `LookupService` صار المصدر الموحّد لقوائم
  الاختيار (أصناف/مخازن/خزائن/طرق دفع/حسابات/مراكز تكلفة) **بدون طلب صلاحية شاشة أخرى** ومقيّداً بنطاقه؛
  شاشة نقطة البيع والحجز والمصروفات التشغيلية تعمل الآن لمستخدم الاستقبال المحدود الصلاحيات،
  والمخازن تُعرض من `user_warehouses` (أو مخازن فروعه عند عدم الربط)، وقائمة الحجوزات تُقيَّد بفروعه.
- Tests: `BookingFilterTests` (3), `LookupScopeTests` (2), `TreasuryShiftTests` (+2: متابعة الووردية
  وأرصدة الخزائن، وعدادات الأجهزة لكل جهاز)؛ full suite (354).

### Changed
- شاشة **«الورديات»** أصبحت أول شاشة في **قسم المبيعات** في الشريط الجانبي (قبل الحجوزات).

## 2026-11-05 — Branch-level shifts (all screens) + computed opening balances + review + 05:00 auto-close

### Added
- Migration `0074`: `cashier_shifts.treasury_id` becomes optional, the open-shift unique index moves
  from **treasury** to **branch** (`ux_shift_open` on `branch_id WHERE status = 'open'`), and new
  columns `carried_balance`, `review_balance`, `meter_balance`, `auto_closed`, `review_status`,
  `reviewed_at/by`. New table `shift_review_items`. New user flag `users.requires_shift`.
- Screen renamed to **«الورديات»** (`/treasury/shifts`): the shift now covers **every screen**, not a
  single treasury. «فتح وردية» shows the **authorized branches only** (no treasury cell) and the
  opening balances are **computed and read-only**:
  **الرصيد المرحل من الحسابات** (branch treasury GL accounts) +
  **رصيد الورديات تحت مراجعة المحاسب** + **رصيد عدادات الأجهزة** = **الرصيد الافتتاحي** (إجباري).
- «تقرير الوردية» on every row and before closing: collections, revenues, treasury movements,
  customers with no follow-up/future booking, tomorrow bookings, tomorrow employee appointments and
  every shift registration (drafts and posted, cancelled excluded) with totals.
- Closing a shift submits its **draft** registrations (vouchers, commercial documents, bookings,
  operating expenses) to `shift_review_items` with `review_status = pending`; an accountant user with
  the new **APPROVE** action on the shifts screen reviews/approves them.
- Shift users (`requires_shift`, granted in the user editor) cannot register on **any** screen
  without an open shift (`validation.shift_required`) and can never post/reconcile/reverse to the
  accounts — their registrations stay drafts and go to review on close.
- `ShiftAutoCloseWorker` closes every open shift at **05:00** every day (counted = expected,
  variance = 0), marks it `auto_closed` and submits its registrations to review.
- Tests: `TreasuryShiftTests` (+4: computed opening balances, drafts→review→approve, auto-close, pending-review carry),
  `ShiftGateTests` (3), `ShiftAutoCloseTests` (3); full suite (347).

### Changed
- One open shift per **branch** (previously per treasury); `TreasuryShiftTests` and
  `TreasuryReconcileTests` updated to the branch-level model.

## 2026-11-04 — Asset meter follow-up (month/day/single-day) with adjustments

### Added
- Action **«متابعة العداد»** on every metered asset in `/assets/management` (migration `0073` adds
  `asset_meter_adjustments`). The dialog shows the branch + asset header and offers **by month**
  (from month → to month), **by day** (from day → to day) and **single day** views:
  - per period row: **بداية المدة** (last reading before the period + earlier adjustments),
    **المستهلك**, **التسويات**, **الرصيد** and **ملاحظة التسويات**;
  - a detailed movement table (readings + adjustments) for the selected period;
  - **إضافة تسوية** on each row: units (+/−) with a mandatory note and a date defaulting to that
    row’s period; the adjustment is stored in `asset_meter_adjustments` (the asset meter log) with an
    audit entry and the period is recalculated immediately.
- Tests: `AssetMeterStatementTests` (2) — monthly opening/consumed/adjustments/balance and
  single-day statement + adjustment validation. Full suite (337) passes.

### Changed
- Idle-customers column renamed to **«آخر نشاط مسجّل (حجز خدمة/موعد/متابعة)»**.

## 2026-11-04 — Bookings columns order, simple future-only idle logic, follow-ups defaults

### Changed
- **شاشة الحجوزات**: العميل أصبح **أول عمود**، وأُضيف عمود **«الملاحظات»** بعد عمود «الخدمة المحجوزة».
- **عملاء بلا حجز أو متابعة**: المنطق الوحيد للاستبعاد الآن هو وجود **حجز غير ملغى** (مسودة أو مؤكد أو
  غير ذلك) **أو متابعة معلّقة** بتاريخ **الغد أو أي تاريخ لاحق**. أُلغيت قواعد «اليوم بأي حالة» وفحص
  المواعيد (appointments)؛ الحجز الملغى والمتابعة المنتهية/الملغاة لا تُخفي العميل أبدًا.
- **متابعات العملاء**: فلتر الحالة يبدأ افتراضيًا على **«معلّقة»**، ونموذج «إضافة متابعة» يبدأ
  **فارغًا بالكامل** (العميل/الموعد/الملاحظة) ويعود فارغًا بعد كل حفظ.
- Tests: `FollowUpTests` محدّثة (حجز مستقبلي يستبعد، ملغى لا يستبعد، متابعة منتهية لا تستبعد، معلّقة
  تستبعد). الحزمة كاملة (335) ناجحة.

## 2026-11-04 — Bookings service column & customer-only screen, follow-up form reset, follow-up rule fix

### Changed
- **شاشة الحجوزات** (`/sales/bookings`): new column **«الخدمة المحجوزة»** right after the appointment
  time (aggregated item/service names from the booking lines), and the **«مصدر الحجز»** filter was
  removed — the screen is now customer appointments only (source fixed to `manual`, no POS source).
- **متابعات العملاء**: after saving a follow-up the form resets completely to its defaults (customer
  cleared, channel back to call, due date back to tomorrow, note cleared).
- **عملاء بلا حجز أو متابعة**: the follow-up rule is back to **pending only** for any date from today
  onwards — a **completed/cancelled follow-up** (even dated today or in the future) no longer hides
  the customer. A **booking or appointment on the report day** still hides the customer whatever its
  status, and future live bookings/appointments still hide them (unchanged).
- Test: `Idle_search_excludes_cancelled_bookings_today_but_ignores_completed_followups`.
  Full suite (335) passes.

## 2026-11-04 — Idle search: any activity on the report day excludes the customer

### Changed
- **عملاء بلا حجز أو متابعة** exclusion rule updated:
  - **Report day (today):** any booking, appointment or follow-up dated today excludes the customer
    **whatever its status** (cancelled/done included).
  - **Future dates:** unchanged — only a live booking (not cancelled/reversed), an active appointment
    (not cancelled) or a **pending** follow-up excludes the customer.
  - Code range, branch, branch-scope and active-customer conditions are unchanged.
- Test: `Idle_search_excludes_any_activity_on_the_report_day_even_if_cancelled` (cancelled booking and
  cancelled follow-up today keep the customer out). Full suite (335) passes.

## 2026-11-04 — Idle search ignores cancelled/completed activity; «حجز موعد جديد» books a customer session draft

### Changed
- **عملاء بلا حجز أو متابعة** now treats a **cancelled booking**, a **completed follow-up** and a
  **cancelled follow-up** as non-existent: only a live booking (not cancelled/reversed), an active
  appointment (not cancelled) or a **pending** follow-up dated today or later hides the customer. The
  «آخر نشاط» cell also ignores done/cancelled follow-ups.
- **«حجز موعد جديد»** (on every follow-up row and inside the idle search) no longer opens the employee
  shift dialog. It opens a new **customer session** dialog (`CustomerSessionDialog`: customer + date +
  start/end time + optional responsible employee + note) and saves it through
  `BookingService.CreateSessionAsync` as a **draft booking without lines** (`CreateSessionDraftAsync`),
  so it shows in the bookings screen as «مسودة» and can be confirmed/executed later — not as an
  employee shift.
- Tests: `FollowUpTests` (8) — completed/cancelled follow-ups do not hide a customer, a pending one
  does, and the session appointment is stored as an empty draft booking. Full suite (334) passes.

## 2026-11-04 — Follow-ups: code-range idle search, customer-name filter, booking action on every row

### Changed
- **عملاء بلا حجز أو متابعة** (the idle-customer search) now filters with a **code range** («من كود» →
  «إلى كود», empty = all) instead of a free list of codes, and **no date range**: it always looks from
  **today to the future** — a customer appears when they have **no booking, no appointment and no
  follow-up from today onwards** (older activity no longer hides them) and the consolidated cell keeps
  their last booking/appointment/follow-up with the note.
- The follow-ups table gained a **customer search filter** (name/code/phone, via `customer_search`),
  next to the due-date range and status filters.
- **«حجز موعد جديد»** is now shown next to **every** follow-up row (and inside the search dialog),
  gated by the follow-ups `CREATE` permission instead of the appointments screen permission that used
  to hide it.

## 2026-11-04 — Follow-ups: notes column, due-date filter, quick actions and “idle customers” search

### Added
- **متابعات العملاء** table now shows the recorded **notes** and a **due-date range filter**
  («من» / «إلى» على `due_at`) whose default is **today → today**.
- Quick actions on every row (and inside the search dialog): **حجز موعد** (opens `AppointmentDialog`
  with the customer preselected — the dialog now accepts `DefaultCustomerId` and can link an
  appointment to a customer) and **تسجيل متابعة** (`QuickFollowUpDialog`: channel, due date, note).
- The «توليد متابعات المتأخرة» button was replaced by **«عملاء بلا حجز أو متابعة»**: a dialog where the
  user picks **customer codes** (comma/space separated, empty = all), a **branch** (only the branches
  linked to the user) and a **from/to** range; it lists every registered customer with **no booking,
  no appointment and no follow-up** inside that range, in one consolidated «آخر نشاط» cell
  (last booking + last appointment + last follow-up with its note), and offers the two quick actions
  per row. Branch scope is enforced on the server.

## 2026-11-04 — Operating-expenses screen (chart accounts · stock lines · payment / credit)

### Added
- New screen **مصروفات تشغيلية** `/expenses/operating` (migration `0072`, screen `OPERATING_EXPENSES`
  with `VIEW/CREATE/POST/DELETE/PRINT`):
  - every line picks a **postable account from the chart of accounts** and carries description,
    quantity, unit price and amount (amount = quantity × unit price, computed server-side) plus an
    optional **cost center**.
  - a line can be a **stock item** (goods + warehouse + base unit): posting creates a stock **RECEIPT**
    per warehouse, moves the quantity (IN movement, lots/average cost) and debits the account chosen
    for the line (normally the inventory account).
  - header links the registered **supplier**, date, notes, and the **payment**: a treasury + treasury
    method (paid) or “آجل” on credit.
- Posting (one transaction): Dr the line accounts / Cr **supplier payable**, plus — when a payment
  method is chosen — a posted **payment voucher** (visible in the treasury and the shift) that debits
  the payable and credits the treasury/clearing account. Document numbering `EXP-…` / `PAY-…`;
  `source_type = 'operating_expense'` in the general ledger.
- Validation: supplier required, postable non-group account, positive amount, cost-center policy,
  stock line completeness and branch/warehouse link, no re-posting a posted document, delete only
  drafts.

### Fixed
- The expense dialog did not write back the **supplier autocomplete** (it had `Value` without
  `ValueChanged`), so saving always failed with «اختر المورد المسجَّل» even after choosing a supplier;
  the **payment-method select** had the same problem. Both are now two-way bound.
- The expenses **list query** failed on PostgreSQL with `42P08 could not determine data type of
  parameter` (nullable filters were compared with `IS NULL` without a type), so the screen showed the
  generic “تعذّر إتمام العملية” error on load and a saved expense never appeared. Every nullable filter
  is now explicitly cast (`::uuid` / `::date` / `::varchar`), and a regression test
  (`Saved_expense_appears_in_the_list`) covers the list.

## 2026-11-04 — Execution mode is visible on the items grid and inside the POS

### Fixed
- The items grid shows a **«طريقة التنفيذ»** column (مباشر / محجوز), so the value actually stored in
  `items.execution_mode` is visible without opening the dialog.
- The POS product tile and cart line display **«يُنفَّذ مباشرة»** for items coded direct, so the
  cashier sees exactly which lines will execute at checkout and which lines stay reserved for
  «الخدمات المحجوزة».
- `ItemOption` now carries `ExecutionMode` (the POS product query selects it), and the sellable-items
  query no longer drops the `is_active` filter for services (missing parentheses made it apply to the
  goods branch only).
- Test: an item created through the item-screen service with `ExecutionMode = direct` comes back from
  the POS product list with that mode and is executed at checkout.
- The direct-execution tests now set the mode through the **item-screen service** (load detail → save,
  exactly like the dialog) instead of raw SQL, and a new test covers a direct item **paid at checkout**
  (revenue + customer receivable + receipt voucher + zero customer balance after full payment).
  `PosDirectSaleTests` (5) + `PosDirectPaymentTests` (1). Full suite (324) passes.

## 2026-11-04 — POS executes the items coded «تنفيذ مباشر» only

### Fixed
- POS checkout executes **only** the lines whose item is coded «تنفيذ مباشر»
  (`items.execution_mode = 'direct'`): stock OUT + COGS, revenue, customer receivable, booking line
  delivered (`reserved_qty = 0`, quantity card in/out) and the booking is completed — bypassing the
  execution constraints (employee/appointment/meter/booking status).
- **Every other item keeps the previous workflow**: a POS booking with reserved quantities,
  optional collection, then execution from «الخدمات المحجوزة». There is no POS-wide “sell all”
  option any more.
- Direct-mode detection is unified in `ItemExecutionModes.IsDirect` (trims, ignores case) and used by
  the POS line builder/executor, the execution dialog/service and item normalization; the previous
  exact `= 'direct'` SQL comparison could skip a line stored with `reserved_qty = 0` and leave the
  booking stuck with no sale and nothing left to execute.
- `PosReceipt.Executed` reports whether lines were actually executed; the receipt shows the
  «تم البيع والتنفيذ مباشرة» note (and hides the offer expiry) only for an executed sale.
- POS direct execution honours the **allow-negative-stock** setting, dates the COGS entry on the
  document date (not `UtcNow`) and keeps the warehouse the stock was issued from on the booking line.
- Tests: `PosDirectSaleTests` (4) — direct item executes, reserved item stays booked, a mixed cart
  executes only the direct line, negative stock when enabled. Full suite (322) passes.

## 2026-11-03 — Editing an item no longer resets its settings

### Fixed
- The item dialog now reloads **all** item settings when opened for edit; `ExecutionMode` (and the
  consumable / expiry / tax / default-price flags) were dropped, so every save wrote the default
  `reserved` back over a `direct` item.
- `ItemRepository.Map` was not populating tax, price-includes-tax, expiry, consumable and default
  price even though the query selected them — those values were lost on every edit as well. Both
  paths are fixed and covered by a round-trip test.

## 2026-11-03 — POS warehouse selection and stock fallback for direct items

### Fixed
- “No stock” at POS when the item was received in a different warehouse: direct items now resolve a
  warehouse that actually holds the quantity (selected warehouse first, then any warehouse linked to
  the branch), so the sale no longer fails while the balance exists elsewhere.
- POS gained an explicit **warehouse selector** limited to the selected branch (it previously used the
  first warehouse in the list, which caused the mismatch).
- When there is genuinely not enough stock, the error now reports the item, the requested quantity and
  the available quantity instead of a generic message.

## 2026-11-03 — Direct items execute at POS checkout

### Fixed
- POS checkout now executes **direct-mode items immediately** in the same transaction instead of
  leaving them as a plain reservation: stock is issued (stock OUT movement + COGS entry), revenue and
  the customer receivable are posted, the booking line’s delivered quantity is updated (customer
  quantity card shows the consumption) and the booking is completed when nothing remains.
- Direct lines no longer reserve stock (`reserved_qty = 0`), so balances stay consistent and the
  amount is not left as a pending reservation.

## 2026-11-03 — Item execution mode: reserved vs direct

### Added
- Items carry an **execution mode** (migration `0071`, default `reserved`):
  - `reserved`: reserved then executed independently with the usual constraints
    (employee/appointment, meter, confirmed booking).
  - `direct`: bypasses execution constraints — it can be executed from a draft booking without an
    employee, appointment or meter, and posts revenue + customer receivable plus the stock
    out-movement and cost entry immediately.
- Item dialog exposes «طريقة التنفيذ»; the execution dialog shows a «تنفيذ مباشر» badge and skips the
  employee/meter gating for direct items.
- Tests: direct SERVICE posts Dr customer / Cr revenue; direct GOODS posts revenue, COGS/inventory
  cost and a stock OUT movement. Full suite (315) passes.

## 2026-11-03 — WhatsApp send diagnostics (no silent SENT)

### Fixed
- The sender no longer marks a WhatsApp message as SENT when the branch/global credentials are
  missing. Previously it logged and returned success, so messages looked “sent” without leaving the
  server; it now fails with `WHATSAPP_NOT_CONFIGURED`, keeping the message retryable and showing the
  reason in `last_error`.
- The outbox screen shows the Meta message id (`wamid`) per message, making it clear whether Meta
  actually accepted the send and enabling correlation with webhook delivery statuses.

## 2026-11-03 — Normalize phone numbers to international WhatsApp format

### Fixed
- Egyptian local numbers are converted before sending to WhatsApp: `01001234567` →
  `201001234567` (strip the leading zero, prepend the country code, no `+`). Numbers with `+`/`00`
  and already-international numbers are normalized too; other country codes are kept as-is.
- Applied when a receipt message is enqueued and again by the sender, so messages already queued
  with a local number are corrected on retry; the POS wa.me link uses the same conversion.
- Tests: formatter cases and a receipt enqueued from a local number. Full suite passes.

## 2026-11-03 — WhatsApp webhooks (incoming messages and delivery status)

### Added
- Anonymous endpoints `/webhooks/whatsapp/{branchId}`:
  - `GET` verification (`hub.mode` / `hub.verify_token`) against the branch’s Verify Token,
  - `POST` receiver with `X-Hub-Signature-256` HMAC-SHA256 verification against the branch’s App
    Secret (requests without a valid signature are rejected with 403).
- Status updates update `message_outbox` (Meta wamid, `delivery_status`
  sent/delivered/read/failed, failures to `last_error`); incoming messages are stored once per wamid
  in `whatsapp_inbound_messages`.
- Branch WhatsApp settings now hold the **Verify Token** and **App Secret** and display the
  **Callback URL** to paste into Meta; the sender stores the Meta message id for correlation.
- New inbox screen `/notifications/inbox` (linked from notifications) and a delivery-status column
  in the outbox screen. Migration `0070`; tests cover verification, signature, status and inbound
  storage. Full suite passes.

## 2026-11-03 — WhatsApp 401 diagnostics and token hygiene

### Fixed
- Branch WhatsApp settings trim the API URL, Phone Number ID, Messaging Account ID and access token
  on save (tokens pasted with spaces/newlines caused `401 Invalid OAuth access token` from Meta).
- The sender now reports a clear `WHATSAPP_AUTH_FAILED` hint for 401 responses (invalid or expired
  token) and the outbox screen exposes the full last error in a tooltip for diagnosis.

## 2026-11-03 — Message outbox screen

### Added
- `/notifications/outbox` («صندوق الرسائل») reachable from the notifications screen: lists outgoing
  messages with channel, masked recipient, body, status, attempts and last error; supports
  status/channel filters, retry of failed/cancelled messages and cancelling pending ones.
- Permissions: new `RETRY_OUTBOX` / `CANCEL_OUTBOX` actions on the notifications screen (list uses
  `View`). Recipients are masked to the last four digits.
- Tests: outbox list/retry/cancel round-trips. No schema or public API break.

## 2026-11-03 — Per-branch WhatsApp settings

### Added
- `branch_whatsapp_settings` (migration `0069`): every branch stores its own WhatsApp Cloud API
  settings (enabled, API URL, Phone Number ID, Messaging Account ID, access token), managed from
  «الفروع» → «إعدادات واتساب» (permission `ManageDefaults`, branch-scoped).
- Receipt messages carry the branch id in the outbox payload; the dispatcher sends with the branch
  credentials and falls back to the global `WhatsApp` options when the branch is disabled. The
  token lives only in the database, is never logged, and an empty value keeps the current token.
- Tests: branch settings enable sending without global configuration and the payload carries the
  branch. Full suite (296) passes.

### Security
- Tokens must be entered from the branch dialog, never committed to the repository. Any token
  shared outside the app should be rotated from the Meta dashboard.

## 2026-11-03 — Receipt viewing, save-and-send WhatsApp and contact numbers

### Added
- POS receipts screen: a **view receipt** action opens the posted receipt (payment methods,
  amounts, fees and totals) in the printed layout with print support.
- POS terminal: a «حفظ وإرسال واتساب» button saves the receipt and queues the WhatsApp message for
  the customer in one action (normal checkout remains unchanged).
- Customer/supplier profile: a secondary phone and a **WhatsApp contact number** selector; the
  notifier uses the designated WhatsApp number first, then the primary phone, then the secondary
  (migration `0068` adds nullable columns only).
- Tests: receipt detail lines/fees and the designated WhatsApp number; full suite (295) passes.

## 2026-11-03 — Partial payments on receipts

### Changed
- Reserved-service collection accepts **partial payments**: the collected amount may be less than
  the selected services total. It is applied to the selected bookings in order, each capped at its
  remaining due; the rest stays due and the booking keeps appearing in the outstanding list.
  Over-collection is rejected (`validation.collection_exceeds_selected`).
- The collection dialog shows the collected amount, fees, grand total, paid and the remaining on
  services, with a hint that partial payment is allowed; confirm is enabled for any collected
  amount between zero and the selected total.
- POS checkout already accepted partial payments; the receipt records the collected part.
- Added a partial-collection test. No schema or public API change.

## 2026-11-03 — POS fee split, live receipt preview and multi-method collections

### Changed
- POS payment rows for fee-bearing methods now show three cells: **total paid with the method**,
  **collected amount** and **fee** (total = collected + fee); fees are included in the displayed
  grand total and on the printed receipt, and the booking’s paid amount records the collected part.
- POS shows a **live receipt preview** (same printed layout) before checkout.
- Reserved-service collection supports **multiple payment methods** with the same fee split and
  shows a full receipt preview before confirming; it creates one posted receipt per treasury group
  and allocates the collected amounts across the bookings (amount-mismatch validation added).
- Tests updated for the collected-amount validation; no schema or public API change.

## 2026-11-03 — POS receipts list, WhatsApp resend and method source clarity

### Fixed
- POS receipts screen now lists **collection receipts** too (linked through `booking_payments`),
  not only receipts matching a POS booking on the same date — reserved-service payments appear.
- Added a per-receipt WhatsApp action on the POS receipts screen (re-enqueues the receipt message,
  deduplicated) with clear feedback when the customer phone or WhatsApp settings are missing.
- POS reloads the collection methods when a customer is selected, and each item shows its code and
  treasury, making it explicit that they come from the treasuries/payment-methods screen (all
  active treasuries of the branch).

## 2026-11-03 — POS collection methods, per-method fee accounts and WhatsApp receipts

### Added
- POS reads the collection methods coded in the treasuries screen for the branch (all active
  treasuries), each carrying its treasury, clearing account, fee % and fee account.
- `treasury_methods.fee_account_id` (migration `0067`): the payment-method fee is posted to its own
  chart account (validated as postable when coding the method) and falls back to the global
  `payment_fees` default when empty; applied to POS checkout and reserved-service collections.
- WhatsApp receipt notification: receipts enqueue a WHATSAPP message in `message_outbox`
  (deduplicated per receipt) and the outbox dispatcher sends it via Meta Cloud API when
  `WhatsApp:Enabled`, `WhatsApp:PhoneNumberId` and `WhatsApp:Token` are configured; otherwise the
  message is logged without failing the sale. The existing wa.me link remains as a manual fallback.
- Tests: per-method fee account posting, branch-wide method listing, and one WhatsApp outbox row
  per receipt. No changes to other screens or APIs.

## 2026-11-03 — POS collection of reserved services

### Added
- POS terminal: with a customer selected, a «تحصيل الخدمات المحجوزة» action lists every reserved
  service of that customer in the branch that still has an outstanding amount, showing the total,
  the **previously paid** amount and the **remaining due**, with a selectable amount per booking.
- Confirming creates one posted receipt voucher (treasury method + fees) and allocates the amounts
  to the bookings in the new `booking_payments` table (migration `0066`) inside one transaction;
  the customer advances account is credited, consistent with POS collections.
- `v_customer_booking_balances` is the single source for total/paid/remaining per booking, and POS
  checkout now records the collected amount against the new booking as well.
- Tests: outstanding list (paid/remaining), voucher posting and allocation, over-collection
  rejection, and POS checkout recording. Other screens, APIs and tables are unchanged.

## 2026-11-03 — Meter asset and units are mandatory for metered items

### Changed
- Booking execution now requires, **only for items flagged as metered**, selecting an active
  branch meter asset and entering units greater than zero; otherwise execution is rejected
  (`validation.asset_meter_required` / `validation.meter_units_required`) and the confirm button
  stays disabled. Non-metered items keep rejecting any meter data (`validation.item_not_metered`).
- The execution dialog preselects the asset when exactly one is available, shows the requirement
  hint, and treats “no metered assets” as a blocking error for metered items.
- Server-side validation mirrors the dialog (service + repository). Tests cover missing asset,
  zero units and a successful metered execution.

## 2026-11-03 — Resolve and persist the responsible employee from the customer appointment

### Fixed
- Booking confirmation now derives and **persists** the responsible employee from the customer’s
  appointment when the booking has none (linked booking first, then confirmed, then same
  branch/day) and always records the default `owner` assignment — so execution no longer depends on
  the booking being created with an employee.
- The execution fallback accepts any non-cancelled customer appointment (confirmed preferred) and
  no longer requires the appointment branch to match the booking branch, with no service/date
  restriction — cross-branch shifts and legacy data resolve.
- Tests: confirmation persistence from a cross-branch customer appointment, execution resolution
  across branches, and updated legacy fallback coverage. No schema or public API change.

## 2026-11-03 — Execution employee follows the booking’s responsible employee

### Fixed
- The execution preview (reserved services → execute) resolves the responsible employee directly
  from `bookings.assigned_employee_id` — the same name shown in the bookings list — without
  re-checking `is_service_executor` or the shift coverage at execution time. Those are validated
  when picking the employee and when confirming the booking, so a confirmed booking no longer
  shows «لا يوجد موظف مسجل» after a later role or shift change. The confirmed customer appointment
  remains a backward-compatible fallback.
- Moved the employee-resolution tests to `BookingExecutionEmployeeTests` and added coverage for a
  later executor-role change; no schema or API change.

## 2026-11-03 — Link booking execution to the responsible employee and shift

### Fixed
- Booking execution resolves the responsible employee from the booking’s `assigned_employee_id`
  (active service executor with a covering, non-cancelled shift for the session window), so
  confirmed bookings created from the appointments screen (customer-less shifts) can execute
  sessions again; the confirmed customer appointment remains as a backward-compatible fallback.
- Every booking save now records the responsible employee with the default `owner` role in
  `booking_assignments` inside one transaction: creation, draft update (new) and the assign
  action. The legacy role-based API remains untouched.
- Split the assignment/extension writes into `BookingRepository.Assign.cs` (no logic change) and
  added tests for shift-based resolution and the default assignment on draft update.

## 2026-11-03 — Sidebar order and sales screen names

### Changed
- Sidebar sales group now starts with **حجز موعد** (bookings), then **متابعات العملاء**
  (follow-ups), then **نقطة البيع** (POS terminal), then **الخدمات المحجوزة** (reserved
  services / reservations); the remaining screens keep their existing registry order.
- Renamed screen titles: `screen.sales_bookings` → «حجز موعد» / “Appointment booking”, and
  `screen.reservations` → «الخدمات المحجوزة» / “Reserved services” (sidebar, page title and
  header share the same keys).
- Ordering is presentation-only, isolated in `NavOrder` (no change to `AppRegistry` screen
  definitions, permissions, routes or data).

## 2026-11-03 — Booking end date removed from booking creation

### Changed
- The new-booking dialog no longer shows «تاريخ نهاية الحجز»: a manual booking is tied to its
  appointment date only and leaves `bookings.expires_at` empty. The model/API
  (`BookingInput.ExpiresAt`) remain available for compatibility; POS still records the offer
  expiry.
- The «تاريخ نهاية الحجز» wording was removed from the application: the expired-bookings and
  reservations columns now use «تاريخ انتهاء الصلاحية» (`sales.offer_expiry`), and the expiry
  validation message was reworded. Unused keys (`sales.booking_expiry`, `sales.extend_expiry`,
  `sales.expiry_extended`, `reservations.expires`) were deleted from both ar/en resources.
- No database schema or public API change; the expired-bookings logic and the reservations
  extend action keep working on `bookings.expires_at`.

## 2026-11-03 — Separate offer expiry from the appointment date

### Changed
- The bookings screen shows the appointment date/time only (single date + session window); the
  “booking end date” column and the extend-expiry action were removed from it, so the sales
  booking no longer mixes its offer validity with the appointment date.
- POS: the date field and the printed booking receipt now use «تاريخ انتهاء الصلاحية»
  (`sales.offer_expiry`) instead of «تاريخ نهاية الحجز»; the value is still stored in
  `bookings.expires_at` (default 6 months, past dates rejected).
- The reservations/deposits screen keeps its expiry column and extend action, and
  `EXTEND_RESERVATION` / `BookingService.ExtendExpiryAsync` remain available for compatibility.
- Removed the now-unused `ExtendBookingDialog` component from the bookings screen.

## 2026-11-03 — Responsible-employee assignment without a role

### Changed (additive)
- The booking action now assigns the **responsible employee** directly: the role selector
  (owner/executor) is removed from `AssignBookingDialog`, and the chosen employee becomes the
  booking's responsible employee by default (the historical `owner` value is kept only in the
  `booking_assignments` record).
- Added `AssignResponsibleEmployeeAsync` in the booking repository/service (isolated, additive):
  it closes the previous open responsible assignment, records the new one, and updates
  `bookings.assigned_employee_id` in one transaction. The legacy
  `AssignAsync(actor, id, employeeId, role)` API and role data remain untouched for backward
  compatibility.
- UI labels: “إسناد الموظف المسؤول” with a hint that the employee becomes responsible by default.

## 2026-11-03 — Booking expiry restored + executed quantities posted

### Added
- `bookings.expires_at` is back (migration `0065` + index + rebuilt `v_customer_reservations`):
  recorded from POS (default 6 months, past dates rejected), printed on the booking receipt, edited
  in the booking dialog, and shown on the bookings, reservations and expired-bookings screens.
- `EXTEND_RESERVATION` action on both the bookings and reservations screens; extending updates
  `expires_at`, logs an `EXTEND` adjustment, and leaves quantities/entries untouched.
- Executing a SERVICE line posts revenue: Dr customer receivable / Cr sales revenue (+ VAT output)
  valued at the executed share of the line, so the customer value statement and party balance show it.
- Executing a stock line posts its cost from the item category accounts
  (`cogs_account_id → inventory_account_id`) with the global posting defaults as fallback; executed
  quantities keep flowing to the customer quantity card (`v_customer_reservations`).
- Tests: expiry save/extend from both screens, past-expiry rejection, service revenue + customer
  statements, and category-based cost entry.

## 2026-11-03 — Meter reading is optional at booking execution

### Fixed
- «تأكيد التنفيذ» no longer stays disabled for metered items: the meter asset and unit reading are
  optional and only recorded for later maintenance follow-up, so a sale with an executable
  remaining quantity always goes through without linking a meter.
- The asset select is now clearable and starts empty; units/current reading appear only when an
  asset is chosen, and units are required only in that case (server and repository enforce the
  same rule).
- Added a remaining-quantity guard with a clear message instead of an enabled button that silently
  does nothing, and clarified the «no metered assets» note as informational.

## 2026-11-03 — Execution employee requires a confirmed customer appointment

### Changed
- The booking execution screen resolves the responsible employee only from a **confirmed**
  appointment of the same customer and branch. Scheduled, in-progress, done and no-show
  appointments no longer count, and the booking's assigned employee is no longer used as a
  fallback; SERVICE execution stays blocked (`validation.execution_employee_required`) until the
  customer's appointment is confirmed.
- Added coverage proving a scheduled appointment blocks execution and that confirming it releases
  the responsible employee.

## 2026-11-02 — Employee shifts screen (appointments)

### Changed
- `/sales/appointments` renamed to **مواعيد الموظفين** (Employee shifts) with new hint text.
- A shift no longer needs a customer: it is purely the employee working window (from/to) plus
  notes. Customer column and booking number column were removed; a day column was added first and
  the shift notes are shown.
- Shift confirmation is not required for bookings: any non-cancelled shift covering the booking
  window is accepted.
- Cancelling a shift reverts the employee's overlapping, not-yet-delivered bookings to **draft**,
  clears the responsible employee and releases their reserved quantities.
- Overlapping sessions for the same responsible employee remain blocked by the database trigger.


## 2026-11-02 — Single booking date + employee schedule enforcement

### Changed
- Removed the booking expiry date (`bookings.expires_at`) everywhere: dialog, POS, list, receipt,
  reservations view, expired-bookings screen/report and the whole "extend" action/permission.
  `booking_date` (plus the session time) is the single date used for reports and execution.
- Expired bookings now use the session date (`starts_at`, else `booking_date`) instead of expiry.
- **Draft** bookings need no schedule: the employee is optional and only must be active.
- **Confirming** a booking with a session time requires an active **service-executor** employee
  whose appointment in the appointments screen fully covers the booking window.
- POS quick sales (no session time) stay exempt from the employee/schedule rule.
- Migrations: `0064` drops `expires_at` + its index, rebuilds `v_customer_reservations` without it,
  and purges `EXTEND` adjustments.

## 2026-11-02 — Remove commissions + booking registrar column and session overlap

### Changed
- Removed the employee commissions screen, service/repository code, report catalog entry, model,
  permission/action codes, translations, tests, the `employee_commissions` table and
  `app_settings.commission_basis`.
- Bookings list shows the user who registered the booking (`bookings.created_by`).
- Booking list order is day, then responsible employee name, then booking time.
- The responsible-employee picker lists only active employees with
  `is_service_executor = true`.
- Added a database trigger rejecting overlapping booking sessions for the same employee
  (`booking_session_overlap`), independent of the appointments table.
- Migration `0061` drops commission artifacts; migration `0062` adds the overlap guard; migration
  `0063` removes stale commission permission rows and saved report filters.

## 2026-11-02 — Split booking/execution commissions + employee visibility

### Changed
- `employee_commissions` now distinguishes `booking` (the user who created the booking) from
  `execution` (the user who executed the session), via `commission_kind` + `user_id`.
- The commissions screen has separate percentages for the booking registrar and session executor;
  the monthly report shows the commission type.
- Commission calculation uses `bookings.created_by` and the latest
  `booking_executions.created_by` for the booking line; it no longer requires an assigned employee.
- Employee options used by booking/appointment screens are readable with booking, appointment,
  reservation or POS view permission (not only Employees/View).

### Tests
- Added a test proving separate booking and execution commission rows; migration `0060` adds
  `commission_kind`/`user_id` and the per-line/kind uniqueness index.

## 2026-11-02 — Execute booking from reservations + asset meter usage

### Changed
- Removed **إلى دفعة مقدمة** from `/sales/reservations`; kept **تمديد** and **تحرير** and added
  **تنفيذ**.
- Execution opens a read-only modal (service, customer, employee, remaining quantity) where only
  quantity (max remaining) and the configurable meter-unit column are editable; warehouse and
  booking number were removed, and the meter column is always editable.
- Executing a **SERVICE** booking now requires an employee, resolved from the linked appointment
  (or assigned employee); the confirm button is disabled and the server rejects it otherwise.
- Execution posts stock/COGS for goods, updates delivered/reserved quantities and booking status,
  and adds meter units to the linked fixed asset atomically.
- Asset meters now support incremental usage readings (`EXECUTION`), manual reset from the assets
  screen, and a company-configurable meter-unit column label (`app_settings.meter_unit_label`).
- Removed the deliveries screen/dialog and its application service/repository/model code; invoices
  use `booking_execution_lines` for cost.
- Migration `0057`: `booking_executions`, `booking_execution_lines`, meter label, and the
  `EXECUTION` journal/meter sources.

### Tests
- Added execution/meter-reset coverage and updated invoice/dashboard tests to execute bookings.
- Build 0 errors; full suite **267/267 passed**.

### Follow-up
- Item metering is now a company-facing flag (`items.is_metered`) instead of a fixed device FK; the
  execution modal lets the user choose any active metered asset for the booking branch, shows the
  units column only for metered items, and hides it otherwise.
- Employee resolution now prefers the appointment linked to the booking, then same-customer
  appointments today/on the booking date, then the assigned employee; SERVICE execution requires it.
- Removed booking number and warehouse from the execution modal.
- Appointment customer is now mandatory; appointment dates are matched in the configured company
  time zone (not the database UTC date), and migration `0059` backfills the customer on legacy
  appointments linked to a booking.
- Execution employee resolution no longer requires the same service, booking link, or date: any
  non-cancelled appointment for the same customer and branch is considered (linked/today first),
  then the booking's assigned employee.

## 2026-11-02 — Customer filters + employee admin role/job title

### Added
- `/sales/reservations` and `/reports/aging` now have an empty-by-default party search
  (name, phone or code), applied server-side for customers, advances, bookings, quantities,
  invoices and suppliers.
- POS customer selector now has the same search field; results come from the normalized
  customer search text (code/name/phone/email).
- Employees can be linked to an existing administrative role (`employees.role_id`) and carry a
  job title (`employees.job_title`); the employee dialog/table expose both.
- Migration `0056_employee_role_and_job_title.sql`.

### Tests
- Added customer-filter tests for reservations and aging, and an employee role/job-title test.
- Build 0 errors; full suite **268/268 passed**.

## 2026-11-02 — Booking expiry on POS + expired-bookings settlement screen

### Added
- POS checkout now has an editable **تاريخ نهاية الحجز** field, defaulting to **today + 6 months**,
  and rejects past dates. The date is stored in `bookings.expires_at`, shown on the POS receipt and
  in the bookings table.
- Booking dialog also exposes the same expiry field (default +6 months).
- `/sales/expired-bookings` screen lists active bookings whose `expires_at` date has passed and
  still have remaining quantity/value, with latest settlement status, **تسوية**,
  **اعتماد** and **ترحيل** actions.
- `SettlementDialog` accepts a preselected booking and default settlement type, enabling
  expired-booking settlement without re-selecting the booking.
- Migration `0055`: partial index on `bookings.expires_at`.

### Tests
- Build 0 errors; POS, expired-bookings, reservations and accounting-registry tests pass.

## 2026-11-02 — Customer quantity card records POS bookings

### Changed
- POS checkout and booking confirmation now always write the customer quantity to
  `booking_lines.reserved_qty`, even when `reserve_stock_on_booking=false`.
- Stock reservation remains conditional: `stock_balances.reserved_qty` changes only when the
  setting is enabled.
- Migration `0054`: `v_customer_reservations` now exposes the current customer remaining quantity
  from `booking_lines.reserved_qty`, and `v_item_sellable` uses the actual stock hold
  (`stock_balances.reserved_qty`) so customer bookings do not reduce sellable stock when stock
  reservation is off.

### Tests
- Added `Customer_card_keeps_quantity_even_when_stock_reservation_is_off`.
- Build 0 errors; POS/reservation tests pass.

## 2026-11-02 — POS is product-only; appointments stay in their own screen

### Changed
- Removed **حجز موعد** from `/pos/terminal`: no mode toggle, no appointment form, and no
  `PosService.BookAppointmentAsync` / `IPosRepository.BookAppointmentAsync` / `PosAppointmentInput`.
- POS checkout now writes a product booking only: `source='pos'` with explicit
  `starts_at = ends_at = assigned_employee_id = NULL`, so it can never be an appointment.
- `/sales/bookings` has a **مصدر الحجز** filter and defaults to `manual` (bookings/appointments);
  POS product bookings are hidden by default and can be viewed by selecting **مبيعات نقطة البيع**.
- Added `BookingFilter.Source` and repository filtering by `bookings.source`.

### Tests
- Removed the two POS appointment tests that no longer apply.
- Updated `Pos_checkout_creates_booking_only_when_no_payment` to assert no time/employee,
  no `appointments` row, and separation from the `manual` booking filter.
- Build 0 errors; full suite 260 passed + QA end-to-end passed after provisioning `ubp_qa`.

## 2026-11-01 — Complete Arabic user manual + asset screen title

### Added
- `docs/USER-MANUAL-AR.md`: a full screen-by-screen and button-by-button reference (66 screens,
  62 dialogs): required permission, what each action saves/audits (`audit_log` table/action), and
  where it navigates (page/dialog/reload/export/print).
- In-app manual page **النظام → دليل المستخدم** (`/system/user-manual`, permission
  `USER_MANUAL/VIEW`): **generates** the manual from `AppRegistry`, the Razor sources (screens,
  buttons, dialogs) and the live PostgreSQL schema (tables/columns), with workflow flow diagrams,
  per-button constraints/permissions, audit mapping and a static fallback. The **تحديث الدليل**
  button re-generates everything without restarting the app.
- `MarkdownRenderer` now renders `flow` blocks as RTL step diagrams; `UserManualSourceReader` copies
  Razor/service sources into `Code/` on build/publish; `IManualSchemaProvider` exposes schema
  metadata under `USER_MANUAL/VIEW`.
- Linked the manual from `README.md`.

### Fixed
- Added the missing `screen.assets` translation in `ar.json` / `en.json` (the assets screen title
  previously rendered as a raw resource key).

## 2026-11-01 — Bootstrap Super Admin works with `AutoMigrate=false`

### Fixed
- `DatabaseBootstrapper` now runs `DataSeeder` on startup even when
  `Database:AutoMigrate=false` (after verifying the critical schema exists), so the documented
  production flow (`--migrate` + `AutoMigrate=false`) honours `Bootstrap:SuperAdminPassword` and
  recreates the Super Admin when none is active.
- `DataSeeder` emits an actionable Critical log when `Bootstrap:SuperAdminUsername` already exists
  as a non-Super-Admin user and no active Super Admin exists.

### Tests
- Build: 0 errors; `StartupOptionsTests` 9/9 pass.

## 2026-10-31 — All remaining standard reports (20 total)

### Added
- **Stock**: aging by lot (received/expiry/age/value), movements detail (in/out with cost),
  receipts-issues per warehouse and branch, and stock value per warehouse.
- **Commercial/HR**: employee commissions by month and basis, booking conversion by status,
  unapplied customer advances, credit-limit utilisation per customer/branch, follow-ups by status
  and assignee.
- **Assets/BI**: depreciation schedule (cost/accumulated/book value), due maintenance (time window or
  meter threshold), daily sales per branch from `sales_daily_totals`, and monthly party balances from
  the `bi` schema.
- All 20 reports share `StandardReportService` + `/reports/standard/{Key}` (branch scope, CSV/XLSX,
  print) and are listed in `ReportCatalog` for `/reports/center`.

### Tests
- 257 tests pass (1 new: **every** key in `StandardReportKeys` executes through the dispatcher and
  returns columns). Screen smoke test 65/65; six sampled report URLs return 200.


## 2026-10-31 — Standard reports R5/R7/R10 (shifts, dead stock, suppliers)

### Added
- **Shift reconciliation** (`treasury.shift_variance`): opening/expected/counted cash and the variance
  per cashier shift with a total footer.
- **Dead stock** (`stock.dead_stock`): items with a positive balance and no movement for the selected
  window (90 days on the page range), showing value and last activity.
- **Supplier performance** (`purchasing.supplier_performance`): posted purchase invoices per supplier
  with documents count, net/tax/gross and the last document date.
- All three are dispatched by `StandardReportService`, listed in `ReportCatalog` and rendered by the
  single `/reports/standard/{Key}` page (CSV/XLSX/print + branch scope).

### Tests
- 256 tests pass (1 new exercising the three new reports end-to-end through the dispatcher).
  Screen smoke test: **65 checks, 0 failures**.


## 2026-10-31 — Asset categories (seed + UI), screen smoke test, dead routes fixed

### Added
- Migration `0053`: four default **asset categories** (metered machines with expected units, furniture,
  vehicles annual, IT equipment monthly) so the assets screen works on first run.
- «تصنيف أصول جديد» in `/assets/management` (permission-gated) with `CategoryDialog` (code, name,
  method LINEAR/UNITS, frequency MONTHLY/ANNUAL, life, expected units, metered toggle) → this answers
  “where do I create asset categories?” completely from the UI.
- `scripts/smoke-pages.sh`: logs in and opens **every route registered in AppRegistry** plus the health
  and session APIs, printing PASS/FAIL and exiting non-zero on any non-200.

### Fixed
- Two dead menu entries the smoke test caught: `/purchasing/aging` now points to the shared aging
  screen (which already supports suppliers), and the service-recipes entry points to the items screen
  where recipes are edited (its permission actions now match `RecipeService`).

### Verification
- 255 unit tests pass; **smoke test: 65 checks, 0 failures** against a running instance.


## 2026-10-31 — Standard reports R3/R4 (customer meter usage, reservation status)

### Added
- **Customer meter usage** (`assets.customer_meter_usage`): sums `SALES` meter readings per customer and
  asset (units, reading count, last reading) joined through `source_line_id` → invoice → customer.
- **Reservation status** (`sales.reservation_status`): open reservations with reserved/consumed/
  remaining, remaining value and the booking deposit, from `v_customer_reservations`; footer carries
  the total remaining value.
- Both are dispatched by the existing `StandardReportService` and rendered by the single
  `/reports/standard/{Key}` page (CSV/XLSX/print + branch scope + period).

### Tests
- 255 tests pass (1 new: open reservations reported with quantities and remaining value). The meter
  report is exercised live (`200`) but has no dedicated automated test yet — noted for the backlog.


## 2026-10-31 — Standard report framework + VAT and item-margin reports

### Added
- Unified report model in `Ubp.Core.Models` (`ReportColumn`/`ReportTable` + `StandardReportKeys`) and a
  single runner `StandardReportService`: one permission (`ReportsCenter/VIEW`), server-side branch
  scoping and date validation, dispatching by report key.
- `StandardReportRepository`: one SQL statement per report on the approved source —
  **VAT summary** (posted invoice lines grouped by period/direction/rate, with total) and
  **item profit margin** (invoice revenue vs the delivery-note cost used by COGS).
- `/reports/standard/{Key}` — one page renders **any** standard report from its table definition,
  with branch scope picker, period pickers and **CSV/XLSX/print** reusing `ReportExporter`.
- Both reports are registered in `ReportCatalog`, so they appear in `/reports/center` and inherit the
  saved-filter/export experience.

### Tests
- 254 tests pass (2 new): the VAT report aggregates posted purchase lines into net/tax/gross, and the
  runner rejects unknown keys and reversed date ranges.
- Live: `/reports/standard/tax.vat_summary` and `/reports/standard/sales.item_margin` answer `200`,
  and `/reports/center` lists them.


## 2026-10-31 — Assets UI disposal + scheduled sales-meter sync

### Added
- «استبعاد» row action on `/assets/management` (hidden for shared/disposed assets, gated by `EDIT`)
  opening `DisposeDialog` (sale amount + date) and calling `DisposeAsync`.
- `SalesMeterSyncWorker` (`BackgroundService`, `Assets:Sync:Enabled/IntervalHours`, default daily):
  converts posted sales lines of metered items into `SALES` readings without an actor
  (`created_by = null`) and relies on the database unique index for idempotency.
- `SyncSalesReadingsAsync` now accepts a nullable user id so system jobs can run it safely.

### Tests
- 252 tests pass; live: the three asset screens answer `200` and the host starts with the new worker
  registered (no startup errors).


## 2026-10-31 — Phase 8A completed: disposal, sales meter link (`0052`)

### Added
- Migration `0052`: posting keys `asset_disposal_gain`/`asset_disposal_loss`, `items.metered_asset_id`
  (item ↔ metered asset link) and `asset_meter_readings.source_line_id` with a **unique partial index**
  so an invoice line produces at most one `SALES` reading.
- `AssetService.DisposeAsync`: writes off the cost, releases the accumulated depreciation, records the
  cash consideration (branch treasury account) and posts the difference as a **gain or loss** through
  the normal journal service, then marks the asset `DISPOSED` (shared assets are refused).
- `AssetService.SyncSalesReadingsAsync` + `AssetRepository.SyncSalesReadingsAsync`: one SQL statement
  converts posted sales-invoice lines of metered items into `SALES` readings and advances the counter,
  idempotent per line — no transaction in the sales path is touched.

### Tests
- 252 tests pass (3 new): disposal posts a **balanced** entry and marks the asset disposed, a second
  disposal is refused, and the sales sync runs idempotently (link persisted, zero when no lines).


## 2026-10-31 — Phase 8A (part 5): UNITS depreciation method (`0051`)

### Added
- Migration `0051`: `asset_categories.expected_units` (the denominator for the units-of-production method).
- `RunDepreciationAsync` now supports **UNITS**: amount = units consumed in the month (from
  `asset_meter_readings`, excluding `MAINTENANCE_RESET`) × `(cost − salvage) ÷ expected_units`,
  still capped at the remaining depreciable base and still posted through the `DEP` journal.
- `AssetCategory`/`AssetCategoryInput` expose `ExpectedUnits`, and category creation requires it for
  UNITS categories (`validation.category_units_required`).
- The depreciable query no longer hard-codes LINEAR, so both methods share one code path
  (single source of truth for the run).

### Tests
- 249 tests pass (1 new: 100 units × (1000 ÷ 1000) = 100). Verified the whole 49-script chain applies
  cleanly on a freshly created database (`MIGRATIONS_OK applied=49`), which also proved the earlier
  failures were stale-database artifacts, not migration defects.


## 2026-10-31 — Phase 8A (part 4): maintenance and depreciation screens

### Added
- `/assets/maintenance` (`ASSET_MAINTENANCE`): asset picker, orders table (date/action/cost/status),
  inline **new order** form and **new plan** form (TIME in days or METER in units), and a per-row
  «إغلاق» that closes the order and resets the counter when the asset is flagged
  `meter_resets_on_maintenance`.
- `/assets/depreciation` (`ASSET_DEPRECIATION`): month picker + «تشغيل الإهلاك» for the current
  branch and a history table (month · assets · total · **entry no** from the journal · run time).
- `AssetService.PlansAsync/CreatePlanAsync` and `RunsAsync` + repository `ListPlansAsync`/
  `ListRunsAsync` (single source: the depreciation run table joined to `journal_entries`).
- Layering fix the architecture test demanded: `MaintenancePlan` and `DepreciationRunRow` moved from
  `Ubp.Infrastructure.Repositories` to `Ubp.Core.Models`, so no page imports the infrastructure layer.

### Tests
- 248 tests pass (0 failures) including the architecture and registry gates; live check: all three
  asset screens answer `200`.


## 2026-10-31 — Phase 8A (part 3): asset screens

### Added
- `/assets/management` (`ASSETS`): branch-scoped list with search and a **metered-only** filter,
  showing category, branch (or «مشترك»), cost, current counter and status; «تشغيل الإهلاك» runs the
  current month for the user's branch; «قراءة عدّاد» is offered only for metered assets and only
  with `ADD_METER_READING`.
- `AssetDialog` (code, name, category, branch or shared, cost, salvage, in-service date, metered
  toggle → meter type + reset-on-maintenance) and `ReadingDialog` (value + note).
- `AssetService.CategoriesAsync/CreateCategoryAsync` + repository listing/creation of asset
  categories (linear life validation, frequency MONTHLY/ANNUAL) so the screen is usable end-to-end.
- Page renamed `AssetManagement` to avoid the registry test's page→screen map colliding with the
  treasury `Management.razor`.

### Tests
- 248 tests pass (the registry test caught the collision and now passes). Live: `/assets/management`
  answers `200` after login.


## 2026-10-31 — Phase 8A (part 2): depreciation engine with GL posting (`0050`)

### Added
- Migration `0050`: system journal `DEP` (depreciation) so runs post through the normal journal
  service (balance, open-period and permission guards stay in force).
- `AssetService.RunDepreciationAsync(actor, month, branchId)`: picks the branch's **LINEAR** assets
  (branch-bound only — shared assets are excluded from a branch run to avoid double depreciation),
  computes the monthly amount `(cost − salvage) / life`, posts **twelve months at once in December**
  for `ANNUAL` categories, caps at the remaining depreciable base, and creates **one posted journal
  entry per run** (debit depreciation expense / credit accumulated depreciation, grouped per
  category accounts).
- Idempotency: `RunExistsAsync` + `UNIQUE (asset_id, period_month)` and
  `UNIQUE NULLS NOT DISTINCT (period_month, branch_id)` refuse a second run for the same month.
- `RUN_DEPRECIATION` action added to the `ASSETS` screen.

### Tests
- 3 new tests (248 total, 0 failures): a monthly run posts one entry and records the line with its
  accumulated value, a second run for the same month is rejected, and an annual category only
  depreciates in December (June raises `validation.nothing_to_depreciate`).
- Live check: `--migrate` applied `0050` (exit 0) and the `DEP` journal exists with manual posting
  allowed for the engine.


## 2026-10-31 — Phase 8A (part 1): fixed assets, meters and maintenance (`0049`)

### Added
- Migration `0049`: `asset_categories` (method LINEAR/UNITS, frequency **MONTHLY/ANNUAL**, useful
  life, four GL accounts), `fixed_assets` (`branch_id` **nullable = shared asset**, meter type,
  current counter, `meter_resets_on_maintenance`, disposal fields), `asset_meter_readings`
  (MANUAL/SALES/MAINTENANCE_RESET + delta), `asset_maintenance_plans` (TIME or METER interval),
  `asset_maintenance_orders` (OPEN/DONE/CANCELLED + cost + GL link), and the depreciation run/line
  tables with `UNIQUE (asset_id, period_month)` to prevent double depreciation.
- `AssetRepository` (list/get/write/status/readings/maintenance) and `AssetService`:
  branch-scoped listing that **includes shared assets**, monotonic meter readings with automatic
  delta, non-metered assets rejecting readings, and maintenance close that **resets the counter to
  zero only when the asset is configured for it** (recording a `MAINTENANCE_RESET` reading), plus
  audit entries.
- Screen registered in `AppRegistry` (`ASSETS` with `VIEW/ADD/EDIT/ADD_METER_READING/
  OPEN_MAINTENANCE/CLOSE_MAINTENANCE`) — the Blazor screens are the next step.

### Tests
- 4 new tests (245 total, 0 failures): branch scope with shared assets, monotonic readings and
  current-value updates, non-metered rejection, and maintenance reset semantics (including a second
  close being refused).
- Live check: `--migrate` applied `0049` (exit 0) and all seven tables exist.


## 2026-10-31 — Phase 8C (part 2): multi-branch reporting

### Added
- Trial balance, party statement and cost-center reports now accept **several branches**
  (`IReadOnlyList<Guid>`, empty = every branch the caller may see) while the old single-branch
  signatures keep working as thin overloads — no caller or test changed behaviour.
- `AccountingReportService.Scope(...)` intersects the requested branches with the actor's
  `AllowedBranchIds` **server-side** (super admin = everything), so a branch manager can never ask
  for a foreign branch even by calling the API directly.
- SQL filters were converted to `cardinality(@branchIds) = 0 OR col = ANY(@branchIds)` in the three
  repositories (trial balance, statements, cost centers; stock-vs-gl follows the same call path).
- New shared component `BranchScopePicker` (MudSelect multi-select + "all branches") wired into
  `/reports/accounting/trial-balance`; party statement and cost centers already pass the scope array
  and receive the picker next.

### Tests
- 3 new tests (241 total, 0 failures): the trial balance sums two branches and narrows to a subset,
  the legacy single-branch signature still works while an empty scope means "all visible", and the
  party statement/cost centers accept the branch array.
- Live check: `/reports/accounting/trial-balance`, `/party-statement` and `/cost-centers` all answer
  `200` after the component swap.


## 2026-10-31 — Phase 8C: main treasury + branch account defaults (`0048`)

### Added
- Migration `0048`: `treasuries.is_main` with a **partial unique index** (exactly one main treasury)
  and `branch_account_defaults (branch_id, key) → account_id` (the approved "global chart + per-branch
  defaults" model, decision 8C-a+c).
- `TreasuryRepository.SetMainAsync/GetMainAsync` (switch inside one transaction) and
  `TreasuryService.SetMainTreasuryAsync` (branch scope + audit) exposed as the `SET_MAIN` action on
  the treasuries screen; `/treasury/management` now shows a main-treasury badge/button.
- `BranchAccountDefaultsService/Repository`: list/upsert/delete per branch with key validation
  (`^[a-z][a-z0-9_.]{2,59}$`), **postable-account enforcement**, and `ResolveAsync(branch, key)` that
  posting code can call before falling back to the global `posting_defaults`.

### Tests
- 4 new tests (238 total, 0 failures): only one main treasury can exist and the switch moves it,
  an inactive treasury cannot be selected, branch defaults upsert/resolve/remove without duplicates,
  and invalid keys or non-postable accounts are rejected.


## 2026-10-31 — Phase 8B: reservations as customer deposits (`0047`)

### Added
- Migration `0047`: `reservation_adjustments` (TO_ADVANCE / RELEASE / EXTEND with a **unique partial
  index** so a booking line converts once), `v_customer_reservations` (reserved · consumed ·
  remaining · remaining value per customer/item/branch) and `v_item_sellable`
  (`on_hand − reserved`).
- `ReservationRepository` + `ReservationService`: paged list scoped to the caller's branches,
  **convert the unused remainder into a customer deposit** by posting a real receipt voucher
  (reuses treasury numbering, GL posting, allocation and audit; advance is consumed by the invoice
  later, and any shortfall stays on the customer account), extension of the expiry, and release
  through the existing booking cancellation.
- `/sales/reservations` screen (`SALES_RESERVATIONS` with
  `VIEW · CONVERT_TO_ADVANCE · RELEASE_RESERVATION · EXTEND_RESERVATION · EXPORT_CSV · PRINT`):
  remaining-only filter, extend-to date, and per-row convert/extend/release buttons that only appear
  when the user holds the permission.
- `AppRegistry` split further into `AppRegistry.Sales.cs` (kept under 300 lines).

### Tests
- 5 new tests (234 total, 0 failures): the view reports reserved/consumed/remaining and value,
  the sellable view subtracts reservations, conversion posts exactly one voucher plus one adjustment
  and refuses a second attempt, release frees the reservation, and listing respects branch scope.
- Live check: `--migrate` applied `0047` (exit 0), both views exist, `/sales/reservations` → `200`.


## 2026-10-31 — Phase 7 complete: import tool, login hardening, runbook

### Added
- Migration `0045`: `import_jobs` (status/kind checks, count invariants) recording every import run.
- `ImportService`: `PreviewAsync` (parse only — **zero writes**) and `ExecuteAsync`
  (parse → reject on any error or imbalance → otherwise one draft entry via
  `OpeningBalanceService.SaveDraftAsync`) with a `FAILED`/`IMPORTED` job row each time.
- `/admin/import-tool` (`IMPORT_TOOL` with `VIEW/PREVIEW/IMPORT_CSV`): CSV upload, branch/date,
  preview table with per-row errors and totals, execute button gated on a clean balanced preview,
  and the import-job history.
- Login hardening (7E): built-in ASP.NET Core rate limiter — **10 requests/minute per IP** on
  `/auth/*` and `/api/v1/auth/*` returning `429` beyond the limit (no new package).
- `docs/PHASE7-RUNBOOK-AR.md` (7F): deploy order (`--migrate` first), health checks, backup cron via
  `fn_record_backup`, restore drill, rollback matrix, pilot checklist and the quality gates.

### Tests
- 5 new tests (229 total, 0 failures): preview writes nothing (no entries, no jobs), a broken CSV
  records a `FAILED` job without touching the ledger, an unbalanced CSV is rejected, a balanced CSV
  saves exactly one draft entry and an `IMPORTED` job with matching totals, and the admin holds the
  import permissions.
- Live checks: `--migrate` applied `0045` (exit 0); `/admin/import-tool` returned `200`; twelve rapid
  login attempts returned `401 ×9` then **`429 ×3`**, proving the limiter works.


## 2026-10-31 — Phase 7 (part 1): migration CLI, backup log, system-health screen

### Added
- `--migrate` command-line path (`MigrationCli`): runs DbUp once and exits with a status code
  (`MIGRATIONS_OK applied=N total=N` / exit 0, or `MIGRATIONS_FAILED error=…` / exit 1) so CI/CD can
  apply migrations before switching the app version. `Program.cs` itself stays free of the
  infrastructure layer (architecture test enforced).
- Migration `0044`: `backup_log` (status/kind checks, "finished implies finish time") plus
  `fn_record_backup(...)` for the **external** `pg_dump` cron; the UI never executes system commands.
- `BackupLogRepository` (read-only list/last) and `SystemHealthRepository` (server version, database
  size, table count, queries running longer than 30 s, last backup).
- `SystemHealthService` with the `SYSTEM_HEALTH` screen permission and `/admin/system-health`:
  status cards, pending-migration names, and the backup log table.

### Tests
- 4 new tests (224 total, 0 failures): a recorded backup appears in the log, an inconsistent backup
  row is rejected by the check constraint, the snapshot reports real database facts with **zero
  pending migrations**, and the backup list is newest-first.
- Live check: `--migrate` applied `0044` and exited 0; after `fn_record_backup(...)`,
  `/admin/system-health` answered `200` and rendered the backup row.


## 2026-10-31 — Phase 6B: `bi` analytics schema (read-only, refreshed)

### Added
- Migration `0043`: schema `bi` with three materialized views — `sales_daily`,
  `stock_value_by_warehouse`, `party_balances_monthly` — each with a **unique index** (required by
  `REFRESH … CONCURRENTLY`), plus `bi.fn_refresh_all()` for a plain refresh.
- Role `bi_reader` (`NOLOGIN`) with `USAGE` on `bi`, `SELECT` on its objects and
  `ALTER DEFAULT PRIVILEGES`; it has **no** rights on `public` (asserted by tests).
- `BiRepository`: read the views, `RefreshAsync` (plain) and `RefreshConcurrentlyAsync`
  (three statements, each in its own implicit transaction).
- `BiRefreshWorker` (`BackgroundService`, `Bi:Enabled/IntervalMinutes`, default every 15 min) that
  refreshes concurrently so readers are never blocked.

### Tests
- 4 new tests (220 total, 0 failures): views reflect the source after refresh, concurrent refresh
  works and picks up new rows, the stock-value view is readable, and `bi_reader` can select inside
  `bi` but cannot insert/update/delete there nor read `public.users`.
- Live check: migration `0043` applied, `bi.sales_daily` matched the source row count after the
  worker's startup refresh, and `bi_reader` privileges were `t` for `bi` and `f` for `public.users`.


## 2026-10-31 — Phase 6E: message outbox + retry worker

### Added
- Migration `0042`: `message_outbox` with channel/status checks, a **partial unique index on
  `dedupe_key`** (no duplicate messages on retry), and a partial index on the due queue.
- `MessageOutboxRepository`: idempotent `EnqueueAsync`, `ClaimDueAsync` using
  `FOR UPDATE SKIP LOCKED` with `next_attempt_at` as a lease, `MarkSent`/`MarkFailed`, status counts.
- `OutboxService` (validation before the queue) and `IMessageSender` with a `LoggingMessageSender`
  demo provider that masks the recipient — the single seam to replace with a real SMS/WhatsApp
  provider (X-15).
- `OutboxDispatcher` (`BackgroundService`): claims a batch, sends, marks sent, or records the failure
  with exponential backoff (60s, 120s, 240s … capped at 1h) until `MaxAttempts` makes it `FAILED`.
  Configurable through the `Outbox` section (`Enabled/PollSeconds/BatchSize/MaxAttempts`).

### Tests
- 12 new tests (216 total, 0 failures): dedupe replay, validation, lease-once semantics,
  lease expiry, backoff scheduling, terminal failure after max attempts, exponential backoff table,
  plus dispatcher success/failure paths against a fake provider.
- Live check: migration `0042` applied, a queued WhatsApp message was picked up by the running
  worker and moved to `SENT` with `sent_at` set, and replaying the same `dedupe_key` inserted nothing.


## 2026-10-31 — Phase 6C: sales daily totals + dashboard under 300 ms

### Added
- Migration `0041`: `sales_daily_totals` keyed by `(day, branch_id)` with invoice/return counters,
  net/tax/gross/COGS columns and a `day` index.
- The totals row is upserted **inside the invoice posting transaction**
  (`SalesInvoiceRepository.DailyTotals.cs`), so it commits or rolls back with the posting; returns
  subtract the same aggregates (`sign = -1`).
- `DashboardRepository` reads only aggregates: today/month sales from `sales_daily_totals`, stock
  value from `stock_balances` (branch-scoped through `warehouse_branches`) and receivables from
  `party_balances` — constant-cost queries independent of invoice volume.
- `DashboardService` (dashboard permission + branch scope) and KPI cards on `/` and the new
  `/dashboard` route: sales today, invoices today, month sales, gross margin, stock value,
  receivables.

### Tests
- 3 new tests (204 total, 0 failures): posting updates the totals and the dashboard, a failed posting
  leaves **no** totals row (atomicity), and a measured read over 180 days × 3 branches stays under
  the 300 ms budget while matching the SQL expectations.
- Live check: migration `0041` applied, `/dashboard` and `/` answer `200` with the KPI block.


## 2026-10-31 — Phase 6D: internal notifications (bell, center, broadcaster)

### Added
- Migration `0040`: `notifications` targeting **either** a user **or** a role (XOR check), severity
  check, JSON-object payload check, and partial indexes for unread lookups.
- `NotificationRepository`: visibility = direct + role-based (`user_roles`), unread counter,
  mark-read / mark-all-read / clear, and role member resolution.
- `NotificationService`: permission-guarded read/mark/clear plus a trusted internal `SendAsync` used
  by other services; the bell counter degrades to zero (no exception) for users without the screen.
- `NotificationBroadcaster` (singleton): per-user subscriptions returning `IDisposable`, publishing
  to every open circuit, and releasing handlers on dispose (no leak — asserted by a test).
- UI: bell with an unread badge in `HeaderBar` (subscribes/disposes correctly) and
  `/notifications/center` with icon/severity, mark-all-read, clear-all and live refresh.
- Registered as the `NOTIFICATIONS` screen (`VIEW/MARK_AS_READ/CLEAR_ALL`).

### Tests
- 6 new tests (201 total, 0 failures): direct + role visibility (members only), unread counter,
  mark-read/mark-all/clear, target XOR validation, broadcaster delivery + dispose release, and
  role broadcast reaching every member.
- Live check: migration `0040` applied, `/notifications/center` answers `200`, and the bell link
  renders in the header.


## 2026-10-31 — Phase 6A: reports center, saved filters, CSV/XLSX export

### Added
- Migration `0039`: `saved_report_filters` with `UNIQUE (user_id, report_key, filter_name)`,
  a JSON-object check constraint, a report-key format check and a lookup index.
- `ReportCatalog` (Core) is the single approved list of reports, each bound to the screen code that
  guards it, so `/reports/center` only offers what the user may open.
- `ReportCenterService`: list approved reports by permission, save/apply/delete per-user filters
  (upsert instead of duplicates), and export stock balances to CSV/XLSX.
- `ReportExporter`: RFC4180 CSV with a UTF-8 BOM (Arabic opens correctly in Excel) and a hand-built
  OOXML `xlsx` package via `System.IO.Compression` only — no `System.IO.Packaging`, no new package
  (X-01). Numeric cells are written as numbers, strings are XML-escaped, sheet names are sanitised
  and truncated to 31 characters.
- `/reports/center` screen: report tiles, warehouse/zero-balance filter, CSV/XLSX/print buttons and
  saved-filter management, registered in `AppRegistry` with `VIEW/SAVE_FILTER/DELETE_FILTER/
  EXPORT_CSV/EXPORT_XLSX/PRINT`; `wwwroot/js/reports.js` performs the download.
- `AppRegistry` split into `AppRegistry.Reports.cs` to keep the 300-line cap.

### Tests
- 16 new tests (195 total, 0 failures): export escaping/BOM/zip layout/number cells/column names,
  filter upsert + ownership + validation, and report visibility by permission.
- Live check: migration `0039` applied, `/reports/center` answers `200` with the Arabic title and
  tiles, and `js/reports.js` is served.


## 2026-10-31 — Phase 3 gate + migration 0038 (concurrency, idempotency)

### Added
- Migration `0038`: `stock_documents.idempotency_key` (unique per branch, partial index) and
  `updated_by` on `stock_documents`/`stocktakes`; the append-only ledger tables deliberately stay
  without `updated_by` (documented inside the script).
- Client retries are now safe: `CreateDraftAsync` relies on
  `ON CONFLICT (branch_id, idempotency_key) DO NOTHING` and returns the existing document, and
  `StockDocumentService.SaveAsync` short-circuits a replayed key before writing an audit row.
- Concurrency gate (Phase 3) with 8 scenarios; the close path is now atomic: the closure write and
  the period status update both reject **inside the same SQL statement** when an unposted stock
  document exists in the scope, so posting cannot slip between the check and the closure.
- `IsolatedDatabaseFactory` (`IsolatedDb` in tests): creates, migrates, seeds and drops a dedicated
  database per concurrency scenario, bounded pool included.
- Connection hygiene: `CreateDraftAsync`/`UpdateDraftAsync` no longer hold a write connection while
  reading the document back (this was halving the practical throughput under load).

### Tests
- 14 new tests (179 total, 0 failures): idempotent replay ×4, concurrent double-posting, 50
  concurrent receipts on an isolated database (numbers + balance + value), post-vs-close race ×5,
  company close blocked by an open branch scope, reopen requires a reason and records an event,
  full rollback of a failed posting, refresh-token single use/logout, and Arabic/English key parity.


## 2026-10-31 — Phase 1 + 2.1 + 2.2: forced password change, lookup layer, display formatter

### Added
- Migration `0036`: `users.must_change_password` + `password_changed_at`; the seeded default
  `Admin@123` account is flagged automatically.
- Forced password change on first sign-in: `LoginResult`/`SessionUser`/`ActorContext` carry the flag,
  `PasswordChangeGate` blocks every route and API except `/change-password`, and `MainLayout`
  redirects internal Blazor navigation (a circuit navigation never reaches the middleware).
  Admin-initiated resets (`UserService.ResetPasswordAsync`) now force a change at the next login.
- Migration `0037`: `app_settings.time_zone` (IANA, default `Africa/Cairo`), validated on save
  (`validation.time_zone`) and editable in `/settings/general`.
- `IDisplayFormatter` (date/time/number) reads the configured formats and zone and converts the
  stored UTC values; it replaces every `.ToLocalTime()` in the UI (which depended on the server
  time zone — UTC in the container — and disagreed with the hard-coded `Africa/Cairo` in the audit
  screen).
- `LookupService` in `Ubp.Application`: partners/items/warehouses/item categories for pickers with
  branch scoping from `ActorContext`, replacing direct repository injection in 7 UI files.
- `TableInfo`/`ColumnInfo`/`RowPage` moved from the repository layer to `Ubp.Core.Models`, so the
  database admin screen no longer imports `Ubp.Infrastructure`.
- 3 architectural tests: UI must not import `Ubp.Infrastructure`, `Ubp.Infrastructure` is confined
  to `Startup` and known adapters, and every source file stays ≤ 300 lines (the CI gate now has a
  test-level twin). The first run caught a 7th violating file (`PriceListItemsDialog.razor`).

### Tests
- 24 new tests (165 total, 0 failures) on a real PostgreSQL: forced-change flow, gate rules,
  display formatting/zone fallback, settings round-trip and validation, plus the architecture suite.
- Live verification: login returns `mustChangePassword: true`, a page request answers
  `302 → /change-password`, `/api/v1/system/me` stays allowed, any other API answers
  `403 {"error":"auth.password_change_required"}`, and normal access resumes once the flag clears.


## 2026-10-31 — Phase 0: security, CI/CD and startup guards

### Security
- Removed the live Neon connection string (and its `neondb_owner` password) from
  `src/Ubp.Web/appsettings.json`. Configuration now ships no secrets and no connection defaults;
  local values live in `appsettings.Development.json` and production values come from the
  environment/Secrets.
- `DatabaseOptions` no longer forces `Trust Server Certificate=true` when converting a
  `postgresql://` URL, and it no longer swallows parsing errors. `sslmode` and `channel_binding`
  are mapped explicitly (`require` → `VerifyFull`) — see `docs/EXCEPTIONS.md` X-02.
- `BootstrapOptions.SuperAdminPassword` has no default value any more; `DataSeeder` refuses to
  create the Super Admin when the password is missing or shorter than 12 characters.
- The applied migration `0009_seed.sql` seeds `admin` with the well-known `Admin@123` hash (it is
  never edited — G1). `DataSeeder` now detects that exact hash and replaces it with the configured
  `Bootstrap:SuperAdminPassword`, so the documented knob finally works on a fresh database; when no
  strong password is configured it logs a Critical instead of rotating silently.
- `docker compose`: Postgres is published on `127.0.0.1` only, and `SUPER_ADMIN_PASSWORD` is a
  required variable (no `Admin@123` fallback).
- Startup validation now uses the standard `IValidateOptions<T>` + `ValidateOnStart()` pattern
  (`Ubp.Web/Startup/Validation`) and runs before any database access. Production rejects the
  database owner role for the runtime connection, well-known passwords, development JWT keys and
  a transaction pooler for the migration connection (X-03).
- Public health probe reduced to `{"status":"healthy|unhealthy"}`: no server name, database name or
  table count; it returns `503` when the database is unreachable, caches the result for 15 seconds,
  and a database-free `/health/live` was added (X-07).
- Data Protection key ring can be persisted to disk (`DataProtection:KeyRingPath`), wired in
  `docker-compose.yml` to `/app/keys` so cookie/token keys survive restarts (X-09).

### CI/CD
- `.github/workflows/ci.yml` added — the previous `ci/ci.yml` was outside `.github/workflows`, so
  the pipeline never ran. It keeps the 300-line gate and adds a guard that fails on committed
  build output or committed database credentials, plus concurrency and a job timeout.
- The same file is committed as `docs/ci-workflow.yml` because the automation token used here has
  no `workflows` permission: copy it to `.github/workflows/ci.yml` in the GitHub UI (or push it
  with a token that has the `workflow` scope) to activate the pipeline.

### Repository hygiene
- `.gitattributes` added (line-ending normalisation, binary document types).
- Removed the tracked Word lock file `0- Plan/~$Foundation.docx`.

### Tests
- 10 new tests (9 in `StartupOptionsTests` without a database + 1 asserting the `0009` default
  password is neutralised) — 131 → 141 total.


## 2026-10-24 — Fix: DateTimeOffset offsets when writing timestamptz

### Fixed
- Npgsql 6+ rejects non-zero offsets for `timestamp with time zone`; booking/appointment/follow-up
  times coming from the UI (e.g. +03:00) failed with `Cannot write DateTimeOffset with Offset=03:00`.
- Added `DbTime.Utc(...)` and normalized all user-supplied times in `BookingRepository`,
  `PosRepository` (appointment), `AppointmentRepository` and `FollowUpRepository` before saving;
  display still converts to localime.
- 1 regression test (131 total) writes a +03:00 offset and asserts the stored value is UTC.

## 2026-10-24 — Booking screen: time-only lines, day filter, hidden unauthorized branches

### Changed
- Booking dialog no longer shows quantity, price or totals: each line is a product only, and the
  booking carries a from/to time range (quantity 1 / price 0 behind the scenes).
- Bookings list filters by a selected day (today by default, clearable) and shows the time range.
- Header branch switcher hides the “no branch” option and any unauthorized branch for non-super
  admins (only assigned branches are listed).
- 1 test (130 total) for the day filter.

## 2026-10-24 — Security fix: branch scope enforced server-side

### Fixed
- `IsAdmin` (permission admin) was also bypassing branch scoping everywhere: branch lists, branch
  switching and data queries. Branch bypass is now reserved for `IsSuperAdmin` only.
- Branch options/list are filtered by the user's assigned branches; warehouse and partner scope
  filters use the same rule.
- Added `BranchScope.Ensure` guards on 11 write paths (bookings, POS checkout/appointment, treasury
  and vouchers, shifts, sales invoices, deliveries, appointments, settlements) so posting to a
  foreign branch returns 403 even if the UI is bypassed.
- 3 tests (129 total): branch options limited for a non-super-admin, super admin still sees all,
  and creating a document in a foreign branch is forbidden.

## 2026-10-24 — Appointment bookings without prices or quantities

### Added
- Migration `0035`: `bookings.starts_at`/`ends_at` with a positive-range check and an index.
- POS appointment mode: choose customer + service + date + from/to time; the booking is saved with
  quantity 1 and price 0 (no price, quantity, tax or collection), and no voucher/invoice is created.
- Booking list shows the appointment time; booking create/update persists the time range.
- 2 tests (126 total): appointment saved with time and zero amounts without vouchers, and rejection
  of non-service items or an invalid time range.

## 2026-10-24 — Fix: sales group in navigation + POS redefined as booking with optional collection

### Fixed
- `NavMenu` was missing the `group.sales` group, so every sales/POS screen was registered but
  invisible in the sidebar. The group is now rendered.

### Changed
- POS quick sale now creates **only a booking** (`source = 'pos'`, confirmed, reserves stock when
  the setting is on). It no longer creates a delivery note or an invoice.
- Collection is optional: zero or more payment lines, each with its own treasury and payment
  method, grouped per treasury into one receipt voucher with multiple lines; over-collection above
  the sale total is rejected. The old forced single-method receipt was removed.
- 3 POS tests updated (121 total): booking-only without payment, multi-method collection in one
  voucher, and rejection + rollback when the collected total exceeds the sale.

## 2026-10-24 — Phase 4H: customer follow-ups

### Added
- Migration `0034`: `follow_ups` (customer/booking/appointment link, channel, due date, status)
  with a partial unique index preventing two pending follow-ups for the same booking.
- `FollowUpRepository` + `FollowUpService` (create, complete with version check, and idempotent
  generation for overdue confirmed bookings) and `sales.followups.*` permissions.
- Screen `/sales/follow-ups` (add, complete, generate overdue) with translations.
- 2 tests (120 total): create/complete and one-time overdue generation.

## 2026-10-24 — Phase 4G: point of sale

### Added
- Migration `0033`: POS booking index and receipt-voucher date index (no new tables).
- `PosRepository.CheckoutAsync`: one transaction creates a confirmed POS booking
  (`source = 'pos'`), its delivery note, issues stock through the FEFO engine, posts the COGS
  entry, auto-creates/posts the sales invoice, posts the receipt voucher and allocates it to the
  invoice — any failure rolls everything back.
- `PosService` with `pos.terminal.view/checkout` and `pos.receipts.view/print` permissions + audit.
- Screens: `/pos/terminal` (quick sale with customer, item, quantity, price, treasury method and a
  receipt summary) and `/pos/receipts` (POS invoices), with translations.
- 2 tests (118 total): full checkout creates booking/delivery/invoice/voucher and balances, and a
  stock failure leaves no booking, invoice or voucher behind.

### Known limitations (4G)
- Terminal supports a single line per checkout; multi-line cart and barcode search are next-step UI.

## 2026-10-24 — Phase 4F: settlements, commissions, aging and posting center

### Added
- Migration `0032`: `app_settings.commission_basis`, `booking_settlements` (five types +
  approval + treasury link), `employee_commissions`, the `booking_forfeit_income` posting key,
  and the `v_unposted_documents` view (commercial documents, vouchers, delivery notes).
- Settlements: refund posts `Dr customer advances / Cr treasury`, forfeit posts
  `Dr customer advances / Cr forfeit income`, quantity cancel releases reservations and cancels
  the booking (or marks it partially delivered when something was delivered), transfer moves the
  deposit to another booking of the same customer; approval is mandatory for any amount.
- Commissions (`D20`): monthly statement per employee and invoice line with three bases
  (1 = net, 2 = gross, 3 = collected) and idempotent recalculation.
- Four aging reports: customers, customer advances, open bookings (reusing the 4B supplier aging).
- Posting center: unposted documents from the new view plus three reconciliations
  (stock vs GL, customers vs GL, suppliers vs GL).
- `SalesFinanceService` with `sales.settlements.*`, `sales.commissions.*`, `reports.aging.*` and
  `accounting.posting.*` permissions and audit entries.
- Screens: `/sales/settlements` (+ dialog with approval/posting), `/sales/commissions`,
  `/reports/aging`, `/accounting/posting-center`, with translations.
- 6 tests (116 total): forfeit entry, refund entry, cancel releasing reservations + cancelling,
  approval requirement, the three commission bases, and aging/unposted/reconciliation reads.

### Known limitations (4F)
- Settlement documents carry no stock movement; quantity cancellation uses the booking’s current
  reserved quantities (not a per-line quantity selector yet).
- Commission “collected” basis uses the invoice-level allocated amount repeated per line.

## 2026-10-24 — Phase 4E: sales invoices and returns

### Added
- Migration `0031`: `booking_id`/`delivered_at` on `commercial_documents`, `booking_line_id` on
  `commercial_document_lines`, `sales_return_links`, the D3 guard (`SALES_LINE_REQUIRES_BOOKING`),
  the N24 return-quantity guard (`RETURN_QTY_EXCEEDED` + `returned_qty` maintenance), and a narrow
  line-guard exception that allows updating only `returned_qty` on a posted line.
- `SalesInvoiceRepository`: drafts from delivered booking remainders or returnable invoice lines,
  posting a sales invoice (`Dr receivable / Cr revenue + VAT`), automatic consumption of open
  receipt vouchers (`Dr advances / Cr receivable`) updating `allocated_total`, and updating
  `booking_lines.invoiced_qty`.
- Sales returns: credit note (`Dr revenue + VAT / Cr receivable`) plus stock re-entry at the
  original delivery cost with `Dr inventory / Cr COGS`, inside one transaction.
- Auto invoice on delivery (`auto_invoice_on_delivery` + `invoice_grouping_mode = 0`) created and
  posted inside the delivery transaction and linked through `delivery_notes.invoice_id`.
- `SalesInvoiceService` with `sales.invoices.*` and `sales.returns.*` permissions and audit.
- Screens: `/sales/invoices` (+ dialog from booking remainders) and `/sales/returns`
  (+ dialog from posted invoices), with translations.
- 5 tests (110 total): D3 guard, revenue + advance consumption with party-balance reconciliation,
  credit note + restock at original cost + N24 guard, auto invoice on delivery, over-invoicing guard.

### Known limitations (4E)
- The existing unique index `ux_commercial_documents_original` allows a single return document per
  original invoice; multiple partial return documents need a schema decision (recorded for 4F).

## 2026-10-24 — Phase 4D: bookings, appointments, delivery execution

### Added
- Migration `0030`: `bookings`, `booking_lines`, `booking_assignments`, `appointments`
  (with `EXCLUDE USING gist` so one employee cannot have overlapping slots), `delivery_notes`,
  `delivery_note_lines`, `service_consumptions`, plus the `delivery` journal source type.
- `BookingRepository`: draft creation with per-line discount/tax totals, confirmation that
  reserves `stock_balances.reserved_qty` only when `reserve_stock_on_booking` is on, cancellation
  that releases reservations, employee assignment with history (`owner`/`executor`).
- `DeliveryRepository`: draft from booking remainders, posting that releases the reservation,
  issues stock through the FEFO engine, books `Dr COGS / Cr Inventory`, fills the line cost
  snapshot, and moves the booking to `partially_delivered`/`completed`.
- `AppointmentRepository` + day schedule; overlapping slots are rejected by the database
  (`23P01` → `validation.appointment_overlap`) and cancellations free the slot.
- `BookingService` and `SalesFulfillmentService` with `sales.bookings.*`,
  `sales.appointments.*`, `sales.deliveries.*` permissions and audit entries.
- Screens: `/sales/bookings` (+ create/assign/cancel dialogs), `/sales/appointments`
  (+ dialog and status actions), `/sales/deliveries` (+ delivery dialog and posting).
- 5 tests (105 total): reservation and over-booking block, delivery + COGS + completion,
  cancellation releasing reservations, appointment overlap/cancel, assignment history.

### Known limitations (4D remainder)
- Service recipe consumption (`consumption_mode` + substitutes) is not wired yet; the table and
  contract exist. Auto-invoice on delivery (`D4`) lands with 4E.

## 2026-10-24 — Phase 4C-ب: clearing reconciliation, voucher cancel, shift reopen, allocation UI

### Added
- Migration `0029`: clearing-account index for `treasury_methods`, plus `reopened_at` and
  `reopen_reason` on `cashier_shifts`.
- `/treasury/reconciliation`: clearing balances (cards/wallets/cheques) aggregated from
  `gl_balances`, with a settlement dialog that posts one balanced entry between the clearing
  account and the selected treasury (`treasury.reconciliation.view/post`).
- Voucher cancel for drafts (`treasury.vouchers.cancel`) — posted vouchers stay immutable.
- Allocation dialog on posted receipts/payments: lists only open invoices of the party,
  shows the remaining voucher amount, and calls the existing allocation engine.
- Shift reopen with a mandatory reason (`treasury.shifts.reopen`): reverses the variance entry
  through `fn_gl_reverse_entry`, returns the shift to `open` and keeps the single-open-shift
  invariant; the reason is audited.
- 4 tests (100 total): clearing settlement zeroes the account, cancel rules, reopen/reclose with
  reversal, and allocation listing limits.

### Changed
- `TreasuryService` split into partial files (core, vouchers, shifts, reconciliation) to keep the
  300-line limit; `TreasuryRepository` gained `Reconcile.cs` and `Reopen.cs`.

## 2026-10-24 — Phase 4C: treasuries, payment methods, vouchers & cashier shifts

### Added
- Migration `0027`: `treasuries` (cash/bank/wallet linked to a GL account), `treasury_methods`
  (cash/card/wallet/cheque/transfer with clearing account and percent/fixed fees), `vouchers` +
  `voucher_lines` (multi-method receipt/payment/transfer), `document_allocations`,
  `cashier_shifts` + `shift_denominations`, immutability triggers after posting, and five new
  `posting_default_types` keys (`customer_advances`, `supplier_advances`, `payment_fees`,
  `cash_over_short`, `inter_branch_clearing`).
- Migration `0028`: `voucher`/`cash_shift` journal source types, treasury reporting indexes,
  `allocated_total <= total` check, and a narrow guard exception that lets a posted invoice
  update only `allocated_total` when a payment is allocated.
- `TreasuryRepository` (partial files): voucher drafting with automatic fee calculation and
  reference enforcement, atomic posting to the cash/bank journal, allocation with voucher and
  document limits, shift open/close with expected-cash computation, denomination grid.
- Posting rules: receipt = Dr treasury/clearing + Dr payment fees / Cr customer advances;
  payment = Dr supplier advances + Dr fees / Cr treasury/clearing; transfer = Dr destination /
  Cr source, with `inter_branch_clearing` plus a second entry when the branches differ;
  allocation = Dr advances / Cr receivable (reverse for suppliers); shift variance goes to
  `cash_over_short`.
- `TreasuryService` with `treasury.management.*`, `treasury.vouchers.*` and `treasury.shifts.*`
  permissions and audit entries.
- Screens: `/treasury/management` (+ treasury and payment-method dialogs), `/treasury/vouchers`
  (multi-line editor with live totals), `/treasury/shifts` (open/close + denomination counting).
- 6 tests (96 total): multi-method receipt with a 2% card fee, allocation limits and party-balance
  reconciliation, inter-branch transfer clearing, voucher immutability, one open shift per
  treasury, and shift variance posting.

### Changed
- Posting readiness covers the treasury module (5 keys, 2 required).
- Journal `source_type` check accepts `voucher` and `cash_shift`.

## 2026-10-22 — Phase 3: accounting core & general ledger

### Added
- Migrations `0020`–`0023`: `accounts` (materialized path tree), `cost_centers`,
  `journals` (7 system journals), `posting_defaults`, `item_category_accounts`,
  `journal_entries`, `journal_entry_lines`, `gl_balances`, `party_balances`,
  plus `app_settings.auto_post_stock_journals` and `app_settings.accounting_start_date`.
- Atomic posting in SQL: `fn_gl_post_entry` (numbering + balance/period/party/cost-center
  checks + monthly & party balances in one transaction) and `fn_gl_reverse_entry`
  (reversing entry + `is_reversed` flag, double reversal impossible).
- Immutability enforced by triggers: posted entries and their lines cannot be edited or
  deleted; only the reversal flag may change.
- 13 screens: chart of accounts, cost centers, journals, accounting settings (defaults,
  category accounts, completeness check), journal entries list, entry editor (party picker,
  cost distribution, auto-balance, print), opening balances (CSV import + equity balancing),
  stock posting center, and 5 reports (ledger, trial balance, party statement, cost-center
  analysis, stock vs GL).
- Permission catalog keys in the form `accounting.<screen>.<action>` via
  `ScreenDefinition.PermissionGroup`, with a test that fails when a screen button is not
  registered in `AppRegistry`.
- Automatic stock journaling from `item_category_accounts` + `posting_defaults`, with
  preview, single/bulk generation, idempotency, rounding handling, and auto post/reverse
  hooks on stock document posting when `auto_post_stock_journals` is enabled.
- 23 new tests (75 total): balance enforcement, immutability, double reversal, party and
  cost-center policies, closed periods, tree move, registry coverage, keyset ledger
  pagination, stock auto-posting, and an opt-in 1M-line performance test.

### Changed
- `StockDocumentService` accepts an optional `StockAccountingService` to auto-post journals
  (warehouse posting never fails because of accounting setup).
- Ledger index rebuilt with `INCLUDE (debit, credit, branch_id)` so period totals are
  served index-only.


## 2026-10-21 — Phase 2 gap closure (0018)

### Added
- `stocktake_lines.reason` + trigger `STOCKTAKE_REASON_REQUIRED`; the service requires a reason
  whenever the counted quantity differs from the book quantity.
- Period-close checks: unfinished transfers, unposted stocktakes, zero-cost balances
  (in addition to drafts and unbalanced balances), for branch and final closures.
- Custom fields for `items`, `customers` and `suppliers`, rendered automatically in their forms.
- Periods screen filters (search + status).
- 6 new tests (52 total).


## 2026-10-21 — Phase 2: inventory, partners, period engine

### Added
- Migrations `0012`–`0017`: document-sequence v2, units/items/barcodes, customers/suppliers
  with company-or-branch scope, unified period engine, stock documents/movements/balances,
  transfer receipts, and stocktakes.
- 21 tables, 9 screens (`/inventory/*`, `/partners/*`, `/accounting/periods`, `/reports/stock`).
- Atomic stock posting with moving weighted average, negative-stock guard, reversal documents,
  partial transfer receiving, stocktake adjustments and period-close checks/events.
- Permission actions `POST` · `REVERSE` · `RECEIVE` · `CLOSE_PERIOD` · `REOPEN_PERIOD`.
- 14 new automated tests (46 total) covering posting, costing, periods, transfers, stocktake.

### Fixed
- `0011` sequence prefix was `varchar(10)` while `STOCK_RECEIPT` needs 13 → 4 failing tests.
  `0012` widens it and adds padding/central numbering (`branch_id` nullable).
- Document numbers are unique per branch (`branch_id, doc_no`); stocktake codes per branch.
- Deleting a draft document/stocktake with lines failed because the line guard saw a missing
  parent during cascade; the guard now allows cascade deletes.

## 2026-10-21 — Phase 2a: settings + document numbering

### Added
- Migration `0011_phase2a_settings_and_sequences.sql`: `app_settings.allow_negative_stock`,
  table `document_sequences`, function `next_document_number`.
- `DocumentSequenceRepository` (atomic numbering inside the caller's transaction).
- "Allow negative stock" switch on the General Settings screen (ar/en).
- `document_sequences` listed (read-only) in the Database admin screen.

### Fixed
- General Settings screen edited the cached settings instance directly; it now edits a clone.

## 2026-10-19 — v0.1.0 Phase 1

### Added
- Standalone solution: Core / Infrastructure / Application / Web / Tests.
- PostgreSQL schema with 21 tables, DbUp migrations, seeds, indexes, Arabic search,
  optimistic concurrency, immutable audit log, and warehouse/branch consistency guards.
- Authentication: cookie UI + JWT API + refresh tokens (hash only) + server-side sessions,
  lockout, uniform login errors, CSRF protection.
- Authorization: default-deny registry, roles, per-user overrides, scope, branch isolation.
- Screens: login, dashboard, settings, branches, warehouses, currencies, custom fields,
  users and permission matrix, roles, audit, QA center, database admin.
- Responsive Arabic/English RTL/LTR UI with MudBlazor.
- Dockerfile, docker-compose, GitHub Actions workflow, Arabic documentation.
- 25 automated tests.

### Security
- Sensitive audit fields (password/token hashes) are stripped before persistence.
- Super Admin protections, last-super-admin guard, and read-only audit triggers.
- QA refuses to run against the application database.
