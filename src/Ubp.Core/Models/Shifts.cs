namespace Ubp.Core.Models;

public static class ShiftReviewStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Reviewed = "reviewed";
}

/// <summary>
/// شفت على مستوى الفرع: تغطي كل شاشات التطبيق (لا خزينة واحدة)، وواحدة فقط
/// مفتوحة لكل فرع، وتُغلق آلياً 5 صباحاً كل يوم (الترحيل 0074).
/// </summary>
public sealed class Shift
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid? TreasuryId { get; set; }
    public string? TreasuryCode { get; set; }
    public string? TreasuryName { get; set; }
    public Guid UserId { get; set; }
    public string? Username { get; set; }
    public string Code { get; set; } = "";
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>مستخدم إقفال الشفت (يُستخدم في اسم ملف تصدير PDF) — 0094.</summary>
    public Guid? ClosedBy { get; set; }
    public string? ClosedByName { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal CarriedBalance { get; set; }
    public decimal ReviewBalance { get; set; }
    public decimal MeterBalance { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    /// <summary>إجمالي تحصيلات الشفت: كل سندات القبض الصادرة خلالها أياً كان نوع الخزينة.</summary>
    public decimal ReceiptsTotal { get; set; }
    /// <summary>مجموع أرصدة الخزائن المسجّلة في الشفت.</summary>
    public decimal TreasuryBalance { get; set; }
    public string Status { get; set; } = ShiftStatuses.Open;
    public string? Notes { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
    public string ReviewStatus { get; set; } = ShiftReviewStatuses.None;
    public bool AutoClosed { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public List<ShiftDenomination> Denominations { get; set; } = new();
    /// <summary>أرصدة الخزائن المتابَعة في الشفت (اسم وحدث رصيد) — 0097.</summary>
    public List<ShiftTreasuryBalance> TreasuryBalances { get; set; } = new();
    /// <summary>قراءات عدادات أجهزة الشفت (كل جهاز عمود مستقل) — 0097.</summary>
    public List<ShiftMeterBalance> MeterBalances { get; set; } = new();

    public bool IsOpen => Status == ShiftStatuses.Open;
    public bool IsPendingReview => ReviewStatus == ShiftReviewStatuses.Pending;
}

/// <summary>فتح شفت: الفرع فقط — الرصيد الافتتاحي يُحسب آلياً ولا يُدخل يدوياً.</summary>
public sealed class ShiftInput
{
    public Guid BranchId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>لقطة أرصدة الافتتاح: كلها محسوبة من النظام للفرع، غير قابلة للتعديل.</summary>
public sealed class ShiftOpeningBalances
{
    public Guid BranchId { get; set; }
    /// <summary>الرصيد المرحل من حسابات خزائن الفرع في الأستاذ العام (كل الخزائن).</summary>
    public decimal CarriedFromAccounts { get; set; }
    /// <summary>قيمة تسجيلات الشفتات المرحّلة للمراجعة لدى المحاسب (مسودات لم تُرحَّل بعد).</summary>
    public decimal PendingReview { get; set; }
    /// <summary>إجمالي قراءات عدادات الأجهزة للفرع.</summary>
    public decimal MeterBalance { get; set; }
    public int PendingReviewShifts { get; set; }
    /// <summary>الخزائن وطرق الدفع المعلَّمة «يتم متابعتها في الشفت» مع أرصدتها.</summary>
    public List<ShiftTreasuryRow> Treasuries { get; set; } = new();
    /// <summary>عدادات أجهزة الفرع — كل جهاز صف مستقل حتى لو كان بصفر.</summary>
    public List<ShiftMeterRow> Meters { get; set; } = new();

    /// <summary>الرصيد الافتتاحي = مجموع أرصدة الخزائن المتابَعة في الشفت.</summary>
    public decimal OpeningCash { get; set; }
}

/// <summary>خزينة متابَعة في الشفت: رصيدها من الحسابات المرحّلة + الحركات غير المرحّلة.</summary>
public sealed class ShiftTreasuryRow
{
    public Guid TreasuryId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string TreasuryType { get; set; } = TreasuryTypes.Cash;
    /// <summary>الرصيد من القيود المرحّلة لحساب الخزينة في فرعها.</summary>
    public decimal PostedBalance { get; set; }
    /// <summary>حركات مسودة ما زالت تحت المراجعة (غير مرحّلة).</summary>
    public decimal UnpostedBalance { get; set; }
    public List<ShiftMethodRow> Methods { get; set; } = new();

    public decimal Total =>
        Math.Round(PostedBalance + UnpostedBalance, 2, MidpointRounding.AwayFromZero);
}

public sealed class ShiftMethodRow
{
    public Guid MethodId { get; set; }
    public Guid TreasuryId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string MethodType { get; set; } = PaymentMethodTypes.Cash;
    public bool FollowInShift { get; set; }
}

/// <summary>عداد جهاز في الفرع: يُعرض في خلية مستقلة في شاشة فتح الشفت.</summary>
public sealed class ShiftMeterRow
{
    public Guid AssetId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? MeterType { get; set; }
    public decimal Value { get; set; }
}

/// <summary>عدّ نقدية خزينة داخل الشفت (مسودة حتى يعتمدها المحاسب) — 0076.</summary>
public sealed class ShiftCashCount
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public Guid TreasuryId { get; set; }
    public string TreasuryCode { get; set; } = "";
    public string TreasuryName { get; set; } = "";
    public string TreasuryType { get; set; } = TreasuryTypes.Cash;
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string Status { get; set; } = ShiftCashCountStatuses.Open;
}

public static class ShiftCashCountStatuses
{
    public const string Open = "open";
    public const string Draft = "draft";
    public const string Posted = "posted";
}

/// <summary>النقد الفعلي المُدخل عند إغلاق الشفت لكل خزينة.</summary>
public sealed class ShiftCashCountInput
{
    public Guid TreasuryId { get; set; }
    public decimal CountedCash { get; set; }
}

public sealed class ShiftDenominationInput
{
    public decimal Denomination { get; set; }
    public int Count { get; set; }
}

public sealed class ShiftDenomination
{
    public Guid Id { get; set; }
    public decimal Denomination { get; set; }
    public int Count { get; set; }
    public decimal Total => Math.Round(Denomination * Count, 2, MidpointRounding.AwayFromZero);
}

/// <summary>تقرير الشفت عند الإغلاق: كل تسجيلات الشيفت غير الملغاة (مسودة أو مرحّلة).</summary>
public sealed class ShiftReport
{
    public Shift Shift { get; set; } = new();
    /// <summary>التحصيلات والإيرادات في جدول واحد مرتب بالعميل (0077).</summary>
    public List<ShiftCollectionRow> Lines { get; set; } = new();
    /// <summary>عدد عملاء بلا متابعة أو حجز مستقبلي (يُعرض عدداً فقط) — 0094.</summary>
    public int IdleCustomersCount { get; set; }
    /// <summary>عدد حجوزات الغد المؤكدة فقط (يُعرض عدداً فقط) — 0094.</summary>
    public int TomorrowBookingsCount { get; set; }
    /// <summary>عدد حجوزات الغد غير المؤكدة (باستثناء الملغى/المنتهي) — 0095.</summary>
    public int TomorrowBookingsUnconfirmedCount { get; set; }
    public List<ShiftAppointmentRow> TomorrowAppointments { get; set; } = new();
    /// <summary>عدّ النقدية لكل خزينة (مسودة حتى اعتماد المحاسب) — 0076.</summary>
    public List<ShiftCashCount> CashCounts { get; set; } = new();
    /// <summary>حركة عدادات الأجهزة في الشفت (أول المدة/المستهلك/آخر المدة/الفعلي/الفرق) — 0077.</summary>
    public List<ShiftMeterMovementRow> Meters { get; set; } = new();
    /// <summary>المصروفات التشغيلية المسجلة خلال الشفت — 0078.</summary>
    public List<ShiftExpenseRow> Expenses { get; set; } = new();
    public decimal ExpensesTotal =>
        Math.Round(Expenses.Sum(e => e.Amount), 2, MidpointRounding.AwayFromZero);
    public string? Notes { get; set; }

    public decimal CollectionsTotal =>
        Math.Round(Lines.Sum(l => l.Collected), 2, MidpointRounding.AwayFromZero);
    public decimal RevenuesTotal =>
        Math.Round(Lines.Sum(l => l.Revenue), 2, MidpointRounding.AwayFromZero);
}

public sealed class ShiftReportLine
{
    public Guid Id { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public string? PartyName { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "draft";
    public decimal Amount { get; set; }
}


public sealed class ShiftMovementLine
{
    public string? DocNo { get; set; }
    public string DocType { get; set; } = "";
    public DateTime DocDate { get; set; }
    public string? TreasuryName { get; set; }
    public string? ToTreasuryName { get; set; }
    public string Status { get; set; } = "draft";
    public string? PartyName { get; set; }
    public decimal Amount { get; set; }
    public bool Inflow { get; set; }
}

public sealed class ShiftCustomerRow
{
    public Guid CustomerId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? Phone { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string? LastActivity { get; set; }
}

public sealed class ShiftBookingRow
{
    public Guid Id { get; set; }
    public string? DocNo { get; set; }
    public string? CustomerName { get; set; }
    public DateTime BookingDate { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public string Status { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string? EmployeeName { get; set; }
}

public sealed class ShiftAppointmentRow
{
    public Guid Id { get; set; }
    public string? EmployeeName { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Status { get; set; } = "";
}

/// <summary>سطر التحصيلات/الإيرادات في تقرير الشفت (مرتب بالعميل) — 0077.</summary>
public sealed class ShiftCollectionRow
{
    public string CustomerName { get; set; } = "";
    public string? TreasuryType { get; set; }
    public decimal Collected { get; set; }
    /// <summary>عمولة التحصيل المخصومة من الخزينة وتُضاف للمصروف — 0080.</summary>
    public decimal Fee { get; set; }
    public decimal Revenue { get; set; }
    public decimal Quantity { get; set; }
    public decimal MeterUnits { get; set; }
    public string? EmployeeName { get; set; }
    /// <summary>الصنف/الخدمة المنفَّذة — ظهور كل حركة تنفيذ بسطر مستقل — 0083.</summary>
    public string? ItemName { get; set; }
    /// <summary>سعر وحدة الخدمة المنفَّذة — 0083.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>مصدر الداتا للسطر: تحصيل/قبض، فاتورة بيع، تنفيذ خدمة محجوزة/علاجية، خدمات محولة.</summary>
    public string? Source { get; set; }
    /// <summary>الموظف المتسبب في شكوى مرتبطة بالخدمة (يختلف عن منفّذ الخدمة).</summary>
    public string? ComplaintEmployeeName { get; set; }
}

/// <summary>مصروف تشغيلي مسجّل داخل الشفت (0078).</summary>
public sealed class ShiftExpenseRow
{
    public string? TreasuryType { get; set; }
    public string? TreasuryName { get; set; }
    public decimal Amount { get; set; }
    public string? ItemName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "DRAFT";
}

/// <summary>حركة عداد جهاز خلال الشفت: القراءة الفعلية هي التي تُرحَّل من الشيفت — 0077.</summary>
public sealed class ShiftMeterMovementRow
{
    public Guid AssetId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public decimal Opening { get; set; }
    public decimal Consumed { get; set; }
    public decimal Closing { get; set; }
    public decimal Actual { get; set; }
    public decimal Variance => Math.Round(Actual - Closing, 2, MidpointRounding.AwayFromZero);
}

/// <summary>تسجيل داخل الشفت حُصر عند الإغلاق ليراجعه يوزر محاسب.</summary>
public sealed class ShiftReviewItem
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public string ScreenCode { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string? DocNo { get; set; }
    public DateTime? DocDate { get; set; }
    public string? PartyName { get; set; }
    public decimal Amount { get; set; }
    public string DocStatus { get; set; } = "draft";
    public string Status { get; set; } = ShiftReviewStatuses.Pending;
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}


/// <summary>تسليم نقدية من خزينة إلى حساب وسيط لنقدية الفرع — 0080.</summary>
public sealed class CashHandover
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Guid TreasuryId { get; set; }
    public string TreasuryCode { get; set; } = "";
    public string TreasuryName { get; set; } = "";
    public string TreasuryType { get; set; } = TreasuryTypes.Cash;
    public Guid ClearingAccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>عمولة التسليم (تُرحَّل على حساب عمولة الخزينة) — 0082.</summary>
    public decimal CommissionAmount { get; set; }
    public DateTime DocDate { get; set; }
    public string? Notes { get; set; }
}

public sealed class CashHandoverInput
{
    public Guid BranchId { get; set; }
    public Guid TreasuryId { get; set; }
    public Guid ClearingAccountId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>عمولة التسليم (اختيارية) — تُضاف لمصروفات الشفت وتُرحَّل على حساب عمولة الخزينة.</summary>
    public decimal CommissionAmount { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
}

/// <summary>رصيد خزينة داخل الشفت كعمود مستقل في شاشة الشفتات — 0097.</summary>
public sealed class ShiftTreasuryBalance
{
    public Guid TreasuryId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public decimal Balance { get; set; }
}

/// <summary>قيمة عداد جهاز داخل الشفت كعمود مستقل — 0097.</summary>
public sealed class ShiftMeterBalance
{
    public Guid AssetId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public decimal Value { get; set; }
}
