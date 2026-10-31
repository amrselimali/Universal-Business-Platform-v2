using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// سباق الترحيل مع إغلاق الفترة، وتسلسل إغلاق الفروع ثم الشركة، وإعادة الفتح بسبب مُسجَّل.
/// </summary>
public sealed class PeriodConcurrencyTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PeriodConcurrencyTests(TestDb db) => _db = db;

    private static async Task<string> OutcomeAsync(Task task)
    {
        try { await task; return "ok"; }
        catch (ValidationAppException ex) { return ex.Code; }
        catch (ConflictException ex) { return ex.Code; }
        catch (Exception ex) { return ex.GetType().Name; }
    }

    /// <summary>فترة واحدة فقط تغطي تاريخ اليوم (الفترات لا تتقاطع في النظام).</summary>
    private async Task<FiscalPeriod> TodayPeriodAsync()
    {
        var today = DateTime.UtcNow.Date;
        var existing = (await _db.PeriodRepo.ListOptionsAsync())
            .FirstOrDefault(p => p.StartDate.Date <= today && p.EndDate.Date >= today);
        if (existing is not null) return existing;

        var actor = await _db.AdminActorAsync();
        return await _db.PeriodService().CreateAsync(actor, new FiscalPeriodInput
        {
            Code = "PCP" + Guid.NewGuid().ToString("N")[..6],
            NameAr = "فترة التزامن",
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(1)
        });
    }

    [Fact]
    public async Task Posting_and_closing_never_leave_a_posted_document_in_a_closed_scope()
    {
        var actor = await _db.AdminActorAsync();
        var period = await TodayPeriodAsync();

        for (var round = 0; round < 5; round++)
        {
            var suffix = "R" + round;
            var branch = await _db.CreateBranchAsync("PC-" + suffix, "فرع سباق " + round);
            var warehouse = await _db.CreateWarehouseAsync("PCW-" + suffix, "مخزن سباق " + round, branch);
            var item = await _db.CreateItemAsync("IT-PC-" + suffix, "صنف سباق " + round);

            var draft = await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
            {
                DocType = StockDocumentTypes.Receipt,
                DocDate = DateTime.UtcNow.Date,
                BranchId = branch,
                WarehouseId = warehouse,
                Lines =
                {
                    new StockDocumentLineInput
                    {
                        ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                        Quantity = 2, UnitCost = 1
                    }
                }
            }, actor.UserId);

            var postTask = _db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, false);
            var closeTask = _db.PeriodRepo.UpsertClosureAsync(period.Id, PeriodScopes.Company, null,
                PeriodModules.Inventory, true, actor.UserId, null);
            await Task.WhenAll(OutcomeAsync(postTask), OutcomeAsync(closeTask));

            await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
            await connection.OpenAsync();
            var closed = await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS (SELECT 1 FROM period_scope_closures
                               WHERE period_id = @id AND status = 'CLOSED')
                """, new { id = period.Id });
            var status = await connection.ExecuteScalarAsync<string>(
                "SELECT status FROM stock_documents WHERE id = @id", new { id = draft.Id });

            // الثابت: إن كُتب إغلاق فالمستند كان مرحّلاً قبله؛ ولا مستند يُرحَّل بعد الإغلاق.
            if (closed) Assert.Equal(StockStatuses.Posted, status);

            await _db.PeriodRepo.UpsertClosureAsync(period.Id, PeriodScopes.Company, null,
                PeriodModules.Inventory, false, actor.UserId, "تصفير السباق");
        }
    }

    [Fact]
    public async Task Company_close_is_blocked_while_a_branch_scope_is_still_open()
    {
        var branch = await _db.CreateBranchAsync("PCC-1", "فرع إغلاق الشركة");
        var actor = await _db.AdminActorAsync();
        var period = await _db.PeriodService().CreateAsync(actor, new FiscalPeriodInput
        {
            Code = "PCC" + Guid.NewGuid().ToString("N")[..6],
            NameAr = "فترة إغلاق الشركة",
            StartDate = new DateTime(2030, 6, 1),
            EndDate = new DateTime(2030, 6, 30)
        });

        // نطاق فرع مفتوح يمنع الإغلاق النهائي للشركة.
        await _db.PeriodRepo.UpsertClosureAsync(period.Id, PeriodScopes.Branch, branch,
            PeriodModules.Inventory, false, actor.UserId, null);

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.PeriodService().FinalizeAsync(actor, period.Id));
        Assert.Equal("validation.period_close_blocked", exception.Code);
        Assert.Equal("OPEN_CLOSURES", exception.Args["check"]);
    }

    [Fact]
    public async Task Reopen_requires_a_reason_and_records_an_event()
    {
        var branch = await _db.CreateBranchAsync("PCR-1", "فرع إعادة الفتح");
        var actor = await _db.AdminActorAsync();
        var period = await _db.PeriodService().CreateAsync(actor, new FiscalPeriodInput
        {
            Code = "PCR" + Guid.NewGuid().ToString("N")[..6],
            NameAr = "فترة إعادة الفتح",
            StartDate = new DateTime(2030, 7, 1),
            EndDate = new DateTime(2030, 7, 31)
        });
        await _db.PeriodRepo.UpsertClosureAsync(period.Id, PeriodScopes.Branch, branch,
            PeriodModules.Inventory, true, actor.UserId, null);

        var missingReason = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.PeriodService().ReopenAsync(actor, period.Id, PeriodScopes.Branch, branch,
                PeriodModules.Inventory, "  "));
        Assert.Equal("validation.reopen_reason_required", missingReason.Code);

        await _db.PeriodService().ReopenAsync(actor, period.Id, PeriodScopes.Branch, branch,
            PeriodModules.Inventory, "تصحيح ترحيل خاطئ");
        var events = await _db.PeriodRepo.ListEventsAsync(period.Id);
        Assert.Contains(events, e => e.ActionCode == "REOPEN" && e.Reason == "تصحيح ترحيل خاطئ");
    }
}
