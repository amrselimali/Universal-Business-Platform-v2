namespace Ubp.Core.Models;

public sealed class TrialBalanceRow
{
    public Guid AccountId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string AccountType { get; set; } = "";
    public short Level { get; set; }
    public decimal OpeningDebit { get; set; }
    public decimal OpeningCredit { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    private decimal Net => OpeningDebit + Debit - OpeningCredit - Credit;
    public decimal ClosingDebit => Net > 0 ? Net : 0;
    public decimal ClosingCredit => Net < 0 ? -Net : 0;

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class TrialBalanceReport
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    /// <summary>الفروع المطلوبة؛ مصفوفة فارغة = كل الفروع (8ج).</summary>
    public Guid[] BranchIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<TrialBalanceRow> Rows { get; set; } = Array.Empty<TrialBalanceRow>();
    public decimal TotalOpeningDebit => Rows.Sum(r => r.OpeningDebit);
    public decimal TotalOpeningCredit => Rows.Sum(r => r.OpeningCredit);
    public decimal TotalDebit => Rows.Sum(r => r.Debit);
    public decimal TotalCredit => Rows.Sum(r => r.Credit);
    public decimal TotalClosingDebit => Rows.Sum(r => r.ClosingDebit);
    public decimal TotalClosingCredit => Rows.Sum(r => r.ClosingCredit);
    public bool IsBalanced => TotalDebit == TotalCredit && TotalOpeningDebit == TotalOpeningCredit;
}

public sealed class LedgerCursor
{
    public DateTime EntryDate { get; set; }
    public Guid LineId { get; set; }
    public decimal Balance { get; set; }

    public static LedgerCursor? From(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('|');
        if (parts.Length != 3) return null;
        if (!DateTime.TryParse(parts[0], out var date)) return null;
        if (!Guid.TryParse(parts[1], out var id)) return null;
        if (!decimal.TryParse(parts[2], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var balance)) return null;
        return new LedgerCursor { EntryDate = date, LineId = id, Balance = balance };
    }

    public string ToToken() =>
        $"{EntryDate:yyyy-MM-dd}|{LineId}|{Balance.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

public sealed class LedgerRequest
{
    public Guid AccountId { get; set; }
    public Guid? BranchId { get; set; }
    /// <summary>عدة فروع (8ج)؛ عند تعبئتها تتقدم على <see cref="BranchId"/>.</summary>
    public Guid[] BranchIds { get; set; } = Array.Empty<Guid>();
    public DateTime From { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime To { get; set; } = DateTime.Today;
    public bool IncludeChildren { get; set; } = true;
    public int PageSize { get; set; } = 50;
    public LedgerCursor? Cursor { get; set; }
}

public sealed class LedgerRow
{
    public Guid LineId { get; set; }
    public Guid EntryId { get; set; }
    public string? EntryNo { get; set; }
    public DateTime EntryDate { get; set; }
    public string? JournalCode { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public string? CostCenterName { get; set; }
    public string? PartyName { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}

public sealed class LedgerPage
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal ClosingBalance => OpeningBalance + PeriodDebit - PeriodCredit;
    public IReadOnlyList<LedgerRow> Rows { get; set; } = Array.Empty<LedgerRow>();
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}

public sealed class PartyStatementRow
{
    public Guid EntryId { get; set; }
    public string? EntryNo { get; set; }
    public DateTime EntryDate { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}

public sealed class PartyStatementReport
{
    public string PartyName { get; set; } = "";
    public char PartyType { get; set; } = 'C';
    public Guid PartyId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance => OpeningBalance + TotalDebit - TotalCredit;
    public IReadOnlyList<PartyStatementRow> Rows { get; set; } = Array.Empty<PartyStatementRow>();
}

public sealed class CostCenterAnalysisRow
{
    public Guid? CostCenterId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public short Level { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net => Debit - Credit;
}

public sealed class StockVsGlRow
{
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal StockValue { get; set; }
    public decimal GlBalance { get; set; }
    public decimal Difference => StockValue - GlBalance;
    public bool IsMatched => Difference == 0;
}

public sealed class StockPostingCandidate
{
    public Guid DocumentId { get; set; }
    public string? DocNo { get; set; }
    public string DocType { get; set; } = "";
    public DateTime DocDate { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public decimal TotalCost { get; set; }
    public Guid? EntryId { get; set; }
    public string? EntryNo { get; set; }
    public bool IsMissingAccounts { get; set; }
}

public sealed class StockPostingPreview
{
    public StockPostingCandidate Document { get; set; } = new();
    public IReadOnlyList<JournalEntryLineInput> Lines { get; set; } = Array.Empty<JournalEntryLineInput>();
    public string? ErrorKey { get; set; }
    public bool CanPost => ErrorKey is null && Lines.Count > 0;
}
