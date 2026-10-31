namespace Ubp.Core.Models;

public static class ImportJobKinds
{
    public const string OpeningBalances = "OPENING_BALANCES";
    public const string Items = "ITEMS";
    public const string Partners = "PARTNERS";
}

public static class ImportJobStatuses
{
    public const string Previewed = "PREVIEWED";
    public const string Imported = "IMPORTED";
    public const string Failed = "FAILED";
}

public sealed class ImportJob
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = ImportJobKinds.OpeningBalances;
    public string? FileName { get; set; }
    public string Status { get; set; } = ImportJobStatuses.Previewed;
    public int RowsTotal { get; set; }
    public int RowsImported { get; set; }
    public int RowsFailed { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public string? ErrorSummary { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>ناتج تنفيذ الاستيراد: إما قيد مسودة محفوظ أو قائمة أخطاء بلا كتابة.</summary>
public sealed record ImportOutcome(
    Guid JobId,
    bool Success,
    Guid? JournalEntryId,
    int RowsImported,
    int RowsFailed,
    string? ErrorSummary);
