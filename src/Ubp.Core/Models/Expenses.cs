namespace Ubp.Core.Models;

/// <summary>حالات مستند المصروفات التشغيلية.</summary>
public static class OperatingExpenseStatuses
{
    public const string Draft = "DRAFT";
    public const string Posted = "POSTED";
}

/// <summary>سطر مصروف: حساب من شجرة الحسابات + بيان/كمية/سعر، ويجوز أن يكون بندًا مخزنيًا.</summary>
public sealed class OperatingExpenseLineInput
{
    public Guid AccountId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    /// <summary>القيمة = الكمية × سعر الوحدة (تُحسب على السيرفر).</summary>
    public decimal Amount => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}

public sealed class OperatingExpenseInput
{
    public Guid BranchId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public Guid SupplierId { get; set; }
    public Guid? TreasuryId { get; set; }
    /// <summary>اختياري: تُستخدم طريقة الخزينة الافتراضية تلقائياً — 0093.</summary>
    public Guid? TreasuryMethodId { get; set; }
    public string? Notes { get; set; }
    public List<OperatingExpenseLineInput> Lines { get; set; } = new();
}

public sealed class OperatingExpenseLine
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public Guid? ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostCenterName { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public Guid? StockDocumentId { get; set; }

    public bool IsStockLine => ItemId is not null;
}

public sealed class OperatingExpense
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public Guid? TreasuryId { get; set; }
    public string? TreasuryName { get; set; }
    public Guid? TreasuryMethodId { get; set; }
    public string? MethodName { get; set; }
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = OperatingExpenseStatuses.Draft;
    public Guid? JournalEntryId { get; set; }
    public Guid? StockDocumentId { get; set; }
    public Guid? VoucherId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public IReadOnlyList<OperatingExpenseLine> Lines { get; set; } =
        Array.Empty<OperatingExpenseLine>();

    public bool IsDraft => Status == OperatingExpenseStatuses.Draft;
    /// <summary>مسدد من الشاشة (خزينة) أو آجل على حساب المورد.</summary>
    public bool IsPaid => TreasuryId is not null;
}
