namespace Ubp.Core.Models;

public static class AssetStatuses
{
    public const string Active = "ACTIVE";
    public const string Idle = "IDLE";
    public const string Disposed = "DISPOSED";
}

public static class MeterTypes
{
    public const string Pages = "PAGES";
    public const string Copies = "COPIES";
    public const string Hours = "HOURS";
    public const string Units = "UNITS";

    public static readonly IReadOnlyList<string> All = [Pages, Copies, Hours, Units];

    public static string Normalize(string? value) =>
        value is not null && All.Contains(value.ToUpperInvariant())
            ? value.ToUpperInvariant() : Pages;
}

public static class MeterSources
{
    public const string Manual = "MANUAL";
    public const string Sales = "SALES";
    public const string MaintenanceReset = "MAINTENANCE_RESET";
}

public static class MaintenanceKinds
{
    public const string Time = "TIME";
    public const string Meter = "METER";
}

public static class MaintenanceStatuses
{
    public const string Open = "OPEN";
    public const string Done = "DONE";
    public const string Cancelled = "CANCELLED";
}

public static class DepreciationMethods
{
    public const string Linear = "LINEAR";
    public const string Units = "UNITS";
}

public static class DepreciationFrequencies
{
    public const string Monthly = "MONTHLY";
    public const string Annual = "ANNUAL";
}

/// <summary>أصل ثابت (8أ): بعدّاد (متابعة استهلاك) أو بلا عدّاد (إهلاك زمني فقط).</summary>
public sealed class FixedAsset
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public DateTime PurchaseDate { get; set; }
    public DateTime? InServiceDate { get; set; }
    public decimal Cost { get; set; }
    public decimal SalvageValue { get; set; }
    public string Status { get; set; } = AssetStatuses.Active;
    public bool IsMetered { get; set; }
    public string? MeterType { get; set; }
    public decimal CurrentMeterValue { get; set; }
    public bool MeterResetsOnMaintenance { get; set; }
    public int Version { get; set; } = 1;

    /// <summary>أصل مشترك بين الفروع (بلا فرع).</summary>
    public bool IsShared => BranchId is null;
}

public sealed class AssetInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public Guid CategoryId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VendorId { get; set; }
    public DateTime PurchaseDate { get; set; } = DateTime.Today;
    public DateTime? InServiceDate { get; set; }
    public decimal Cost { get; set; }
    public decimal SalvageValue { get; set; }
    public bool IsMetered { get; set; }
    public string? MeterType { get; set; }
    public bool MeterResetsOnMaintenance { get; set; }
}

public sealed class AssetCategory
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string DepreciationMethod { get; set; } = DepreciationMethods.Linear;
    public string DepreciationFrequency { get; set; } = DepreciationFrequencies.Monthly;
    public int? UsefulLifeMonths { get; set; }
    public bool IsMetered { get; set; }
    public Guid? AssetAccountId { get; set; }
    public Guid? AccumAccountId { get; set; }
    public Guid? ExpenseAccountId { get; set; }
    public Guid? MaintenanceExpenseAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AssetCategoryInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string DepreciationMethod { get; set; } = DepreciationMethods.Linear;
    public string DepreciationFrequency { get; set; } = DepreciationFrequencies.Monthly;
    public int? UsefulLifeMonths { get; set; }
    public bool IsMetered { get; set; }
    public Guid? AssetAccountId { get; set; }
    public Guid? AccumAccountId { get; set; }
    public Guid? ExpenseAccountId { get; set; }
    public Guid? MaintenanceExpenseAccountId { get; set; }
}

public sealed class AssetMeterReading
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public DateTimeOffset ReadingAt { get; set; }
    public decimal Value { get; set; }
    public decimal Delta { get; set; }
    public string Source { get; set; } = MeterSources.Manual;
    public string? Note { get; set; }
}

public sealed class MaintenanceOrder
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid? PlanId { get; set; }
    public DateTime MaintenanceDate { get; set; }
    public decimal Cost { get; set; }
    public string Action { get; set; } = "PREVENTIVE";
    public string Status { get; set; } = MaintenanceStatuses.Open;
    public decimal? ResetMeterValue { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed record MaintenanceOrderInput(Guid AssetId, Guid? PlanId, DateTime MaintenanceDate,
    decimal Cost, string Action, string? Notes);

/// <summary>سطر إهلاك محسوب لأصل في شهر معين (قبل/بعد الترحيل المحاسبي).</summary>
public sealed record DepreciationLine(Guid AssetId, decimal Amount, decimal AccumulatedAfter);

public sealed record MaintenancePlan(Guid Id, Guid AssetId, string Kind, int? IntervalDays,
    decimal? IntervalUnits, DateTime? NextDueDate, decimal? NextDueMeter, bool IsActive);

/// <summary>سطر من سجل تشغيلات الإهلاك (مع رقم القيد — مصدر واحد للبيانات المحاسبية).</summary>
public sealed record DepreciationRunRow(Guid Id, DateOnly PeriodMonth, Guid? BranchId,
    decimal TotalAmount, Guid? JournalEntryId, string? EntryNo, int AssetsCount,
    DateTimeOffset RunAt);

