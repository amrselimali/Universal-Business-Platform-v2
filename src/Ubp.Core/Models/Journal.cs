namespace Ubp.Core.Models;

public static class EntryStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

public static class EntrySources
{
    public const string Manual = "manual";
    public const string StockDocument = "stock_document";
    public const string Stocktake = "stocktake";
    public const string Opening = "opening";
    public const string Invoice = "invoice";
}

public sealed class JournalEntry
{
    public Guid Id { get; set; }
    public Guid JournalId { get; set; }
    public string JournalCode { get; set; } = "";
    public string JournalName { get; set; } = "";
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? EntryNo { get; set; }
    public DateTime EntryDate { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = EntryStatuses.Draft;
    public string SourceType { get; set; } = EntrySources.Manual;
    public Guid? SourceId { get; set; }
    public Guid? ReversalOfId { get; set; }
    public bool IsReversed { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    public int LinesCount { get; set; }

    public decimal Difference => TotalDebit - TotalCredit;
    public bool IsBalanced => TotalDebit == TotalCredit && TotalDebit > 0;
    public bool IsDraft => Status == EntryStatuses.Draft;
    public bool IsPosted => Status == EntryStatuses.Posted;
}

public sealed class JournalEntryLine
{
    public Guid Id { get; set; }
    public Guid EntryId { get; set; }
    public int LineNo { get; set; }
    public Guid AccountId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public string? AccountType { get; set; }
    public short CostCenterPolicy { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostCenterName { get; set; }
    public Guid BranchId { get; set; }
    public DateTime EntryDate { get; set; }
    public bool IsPosted { get; set; }
}

public sealed class JournalEntryInput
{
    public Guid JournalId { get; set; }
    public Guid BranchId { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public List<JournalEntryLineInput> Lines { get; set; } = new();
}

public sealed class JournalEntryLineInput
{
    public Guid AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CostCenterId { get; set; }
}

public sealed class JournalEntryDetail
{
    public JournalEntry Header { get; set; } = new();
    public IReadOnlyList<JournalEntryLine> Lines { get; set; } = Array.Empty<JournalEntryLine>();
    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
    public bool IsBalanced => Lines.Count > 0 && TotalDebit == TotalCredit;
}

public sealed class JournalEntryFilter
{
    public Guid? BranchId { get; set; }
    public Guid? JournalId { get; set; }
    public string? Status { get; set; }
    public string? SourceType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool UnbalancedOnly { get; set; }
}

public sealed class PartyOption
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public decimal Balance { get; set; }
}

public sealed class CostDistributionLine
{
    public Guid CostCenterId { get; set; }
    public decimal Percentage { get; set; }
}
