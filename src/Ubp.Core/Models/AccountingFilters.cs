namespace Ubp.Core.Models;

public sealed class AccountFilter
{
    public string? Search { get; set; }
    public bool IncludeInactive { get; set; }
    public bool? IsGroup { get; set; }
    public string? AccountType { get; set; }
    public bool PostableOnly { get; set; }
    public Guid? ParentId { get; set; }
}

public sealed class CostCenterFilter
{
    public string? Search { get; set; }
    public bool IncludeInactive { get; set; }
    public Guid? BranchId { get; set; }
    public bool? IsGroup { get; set; }
}

public sealed class JournalFilter
{
    public string? Search { get; set; }
    public bool IncludeInactive { get; set; }
    public string? JournalType { get; set; }
}

public sealed class EntryFilter
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? SourceType { get; set; }
    public Guid? JournalId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class AccountMoveInput
{
    public Guid Id { get; set; }
    public Guid? NewParentId { get; set; }
}

public sealed class CostCenterMoveInput
{
    public Guid Id { get; set; }
    public Guid? NewParentId { get; set; }
}

public sealed class OpeningBalanceInput
{
    public Guid BranchId { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string? Description { get; set; }
    public Guid JournalId { get; set; }
    public List<JournalEntryLineInput> Lines { get; set; } = new();
}

public sealed class OpeningBalanceCsvRow
{
    public int RowNo { get; set; }
    public string Code { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }
    public string? PartyCode { get; set; }
    public string? CostCenterCode { get; set; }
}

public sealed class OpeningBalanceImportResult
{
    public List<JournalEntryLineInput> Lines { get; set; } = new();
    public List<OpeningBalanceError> Errors { get; set; } = new();
    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
    public decimal Difference => TotalDebit - TotalCredit;
    public bool IsBalanced => TotalDebit == TotalCredit && Lines.Count > 0;
}

public sealed class OpeningBalanceError
{
    public int Row { get; set; }
    public string Key { get; set; } = "";
    public string? Detail { get; set; }
}
