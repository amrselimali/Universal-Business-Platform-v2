namespace Ubp.Core.Models;

/// <summary>سطر قابل للتعديل من تقرير الوردية (توحيد مستندات الشيفت) — 0089.</summary>
public sealed class ShiftEditableLine
{
    public string EntityType { get; set; } = "";
    public Guid DocId { get; set; }
    public string? DocNo { get; set; }
    public DateTime? DocDate { get; set; }
    public Guid? LineId { get; set; }
    public string? PartyName { get; set; }
    public string? ItemName { get; set; }
    public string? EmployeeName { get; set; }
    public string? TreasuryName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    /// <summary>المعرّفات الأصلية لتهيئة الاختيارات ومقارنة التغييرات.</summary>
    public Guid? AccountId { get; set; }
    public Guid? TreasuryId { get; set; }
    public Guid? EmployeeId { get; set; }
    /// <summary>الحقول القابلة للتعديل لهذا السطر مفصولة بفاصلة.</summary>
    public string FieldsCsv { get; set; } = "";

    public IReadOnlyList<string> Fields => FieldsCsv.Split(',',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>طلب تعديل حقل في سطر مستند ضمن الوردية — 0089.</summary>
public sealed class ShiftLineEditInput
{
    public Guid ShiftId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid DocId { get; set; }
    public Guid? LineId { get; set; }
    /// <summary>account / treasury / item / employee / quantity / unit_price / amount / description.</summary>
    public string Field { get; set; } = "";
    public string? NewValue { get; set; }
    public string? Note { get; set; }
}

public sealed class ShiftEditOutcome
{
    public bool Applied { get; set; }
    public Guid? DifferenceEntryId { get; set; }
    public string? Message { get; set; }
}
