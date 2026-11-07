namespace Ubp.Core.Models;

/// <summary>أنواع عملية سطر الخدمة — 0084.</summary>
public static class ServiceOperationTypes
{
    public const string Normal = "normal";
    public const string Gift = "gift";
    public const string Treatment = "treatment";

    public static bool IsValid(string? value) => value is Normal or Gift or Treatment;
    public static string Normalize(string? value) =>
        IsValid(value) ? value! : Normal;
}

/// <summary>صرف سلفة لموظف من المصروفات التشغيلية على حساب مختار — 0084.</summary>
public sealed class EmployeeAdvanceInput
{
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TreasuryId { get; set; }
    public Guid? TreasuryMethodId { get; set; }
    /// <summary>الحساب الذي تُحمَّل عليه السلفة (مدين). إن كان فارغًا يُستخدم حساب سلف الموظفين الافتراضي.</summary>
    public Guid? ChargeAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
}

public sealed class EmployeeAdvance
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public Guid TreasuryId { get; set; }
    public string? TreasuryName { get; set; }
    public Guid? TreasuryMethodId { get; set; }
    public string? MethodName { get; set; }
    public Guid ChargeAccountId { get; set; }
    public string? ChargeAccountName { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "posted";
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
}

/// <summary>تعديل يقوم به يوزر المراجعة على سطر مستند قبل/عند الترحيل — 0084.</summary>
public sealed class ReviewAdjustmentInput
{
    public Guid ShiftId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid? LineId { get; set; }
    /// <summary>الحقل المعدَّل: quantity / unit_price / amount / description / note.</summary>
    public string Field { get; set; } = "";
    public string? NewValue { get; set; }
    public string? Note { get; set; }
}

public sealed class ReviewAdjustment
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid? LineId { get; set; }
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public decimal DiffAmount { get; set; }
    public decimal DiffQuantity { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? JournalEntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReviewNote
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>الحقول القابلة للتعديل لكل نوع مستند (Whitelist) — 0084.</summary>
public static class ReviewAdjustmentRules
{
    public const string Quantity = "quantity";
    public const string UnitPrice = "unit_price";
    public const string Amount = "amount";
    public const string Description = "description";
    public const string Note = "note";

    private static readonly Dictionary<string, HashSet<string>> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["operating_expense"] = new(StringComparer.OrdinalIgnoreCase)
            { Quantity, UnitPrice, Amount, Description },
        ["booking"] = new(StringComparer.OrdinalIgnoreCase) { Quantity, UnitPrice },
        ["voucher"] = new(StringComparer.OrdinalIgnoreCase) { Amount, Description },
        ["commercial_document"] = new(StringComparer.OrdinalIgnoreCase) { Quantity, UnitPrice, Amount },
        ["service_transfer"] = new(StringComparer.OrdinalIgnoreCase) { Quantity },
    };

    public static bool IsEditable(string entityType, string field) =>
        Fields.TryGetValue(entityType, out var set) && set.Contains(field);
}
