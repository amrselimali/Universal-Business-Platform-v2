using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// أداة الاستيراد (7D): معاينة بلا كتابة، ثم تنفيذ داخل مسار خدمة الأرصدة الافتتاحية
/// (سجل أسطر + ترحيل واحد)، مع تسجيل كل عملية في <c>import_jobs</c>.
/// </summary>
public sealed class ImportService
{
    public const int MaxCsvLength = 2_000_000;

    private readonly IImportJobRepository _jobs;
    private readonly OpeningBalanceService _opening;
    private readonly PermissionService _permissions;

    public ImportService(IImportJobRepository jobs, OpeningBalanceService opening,
        PermissionService permissions)
    {
        _jobs = jobs; _opening = opening; _permissions = permissions;
    }

    public async Task<OpeningBalanceImportResult> PreviewAsync(ActorContext actor, string csv,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ImportTool, ActionCodes.Preview, ct: ct);
        EnsureSize(csv);
        return await _opening.ImportCsvAsync(actor, csv, ct);
    }

    /// <summary>ينفّذ الاستيراد: أي خطأ = صفر كتابة + سجل FAILED.</summary>
    public async Task<ImportOutcome> ExecuteAsync(ActorContext actor, string csv, string? fileName,
        Guid branchId, DateTime entryDate, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ImportTool, ActionCodes.ImportCsv, ct: ct);
        EnsureSize(csv);

        var parsed = await _opening.ImportCsvAsync(actor, csv, ct);
        var rows = parsed.Lines.Count + parsed.Errors.Count;
        if (parsed.Errors.Count > 0 || !parsed.IsBalanced)
        {
            var summary = parsed.Errors.Count > 0
                ? string.Join("; ", parsed.Errors.Take(10).Select(e => $"#{e.Row} {e.Key}"))
                : "validation.csv_not_balanced";
            var jobId = await _jobs.CreateAsync(new ImportJob
            {
                Kind = ImportJobKinds.OpeningBalances, FileName = fileName,
                Status = ImportJobStatuses.Failed, RowsTotal = rows,
                RowsFailed = parsed.Errors.Count,
                TotalDebit = parsed.TotalDebit, TotalCredit = parsed.TotalCredit,
                ErrorSummary = Truncate(summary), CreatedBy = actor.UserId
            }, ct);
            return new ImportOutcome(jobId, false, null, 0, parsed.Errors.Count, summary);
        }

        var entry = await _opening.SaveDraftAsync(actor, new OpeningBalanceInput
        {
            BranchId = branchId, EntryDate = entryDate, Lines = parsed.Lines
        }, ct);

        var successJobId = await _jobs.CreateAsync(new ImportJob
        {
            Kind = ImportJobKinds.OpeningBalances, FileName = fileName,
            Status = ImportJobStatuses.Imported, RowsTotal = rows, RowsImported = rows,
            TotalDebit = parsed.TotalDebit, TotalCredit = parsed.TotalCredit,
            CreatedBy = actor.UserId
        }, ct);
        return new ImportOutcome(successJobId, true, entry.Header.Id, rows, 0, null);
    }

    public async Task<IReadOnlyList<ImportJob>> JobsAsync(ActorContext actor, int take = 20,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ImportTool, ActionCodes.View, ct: ct);
        return await _jobs.ListAsync(take, ct);
    }

    private static void EnsureSize(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) throw new ValidationAppException("validation.csv_empty");
        if (csv.Length > MaxCsvLength) throw new ValidationAppException("validation.csv_too_large");
    }

    private static string Truncate(string value) => value.Length > 1000 ? value[..1000] : value;
}
