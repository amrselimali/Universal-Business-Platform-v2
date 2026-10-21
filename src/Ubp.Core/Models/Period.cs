namespace Ubp.Core.Models;

public static class PeriodScopes
{
    public const string Branch = "BRANCH";
    public const string Company = "COMPANY";
}

public static class PeriodModules
{
    public const string Inventory = "INVENTORY";
    public const string Accounting = "ACCOUNTING";
    public const string Sales = "SALES";
    public const string Purchases = "PURCHASES";
    public const string Cash = "CASH";

    public static readonly string[] All =
        { Inventory, Accounting, Sales, Purchases, Cash };
}

public sealed class FiscalPeriod
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "OPEN";
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class FiscalPeriodInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;
}

public sealed class PeriodClosure
{
    public Guid Id { get; set; }
    public Guid PeriodId { get; set; }
    public string ScopeType { get; set; } = PeriodScopes.Branch;
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string ModuleCode { get; set; } = PeriodModules.Inventory;
    public string Status { get; set; } = "CLOSED";
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedByName { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
}

public sealed class PeriodCheck
{
    public Guid Id { get; set; }
    public Guid PeriodId { get; set; }
    public string ScopeType { get; set; } = PeriodScopes.Branch;
    public Guid? BranchId { get; set; }
    public string ModuleCode { get; set; } = PeriodModules.Inventory;
    public string CheckCode { get; set; } = "";
    public string Status { get; set; } = "PASS";
    public string Message { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class PeriodEvent
{
    public long Id { get; set; }
    public Guid PeriodId { get; set; }
    public string ScopeType { get; set; } = PeriodScopes.Branch;
    public Guid? BranchId { get; set; }
    public string ModuleCode { get; set; } = PeriodModules.Inventory;
    public string ActionCode { get; set; } = "";
    public string? ActorName { get; set; }
    public DateTimeOffset EventAt { get; set; }
    public string? Reason { get; set; }
    public string? TraceId { get; set; }
}

public sealed class PeriodDetail
{
    public FiscalPeriod Period { get; set; } = new();
    public IReadOnlyList<PeriodClosure> Closures { get; set; } = Array.Empty<PeriodClosure>();
    public IReadOnlyList<PeriodCheck> Checks { get; set; } = Array.Empty<PeriodCheck>();
    public IReadOnlyList<PeriodEvent> Events { get; set; } = Array.Empty<PeriodEvent>();
}

public sealed class PeriodCheckInput
{
    public Guid PeriodId { get; set; }
    public string ScopeType { get; set; } = PeriodScopes.Branch;
    public Guid? BranchId { get; set; }
    public string ModuleCode { get; set; } = PeriodModules.Inventory;
    public string CheckCode { get; set; } = "";
    public string Status { get; set; } = "PASS";
    public string Message { get; set; } = "";
    public string? Details { get; set; }
}
