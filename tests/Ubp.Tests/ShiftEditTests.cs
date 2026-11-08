using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>تعديل بيانات الشفت الموحّد: يعدّل المستند الأصلي أثناء الفتح/المراجعة ويُمنع بعد الاعتماد.</summary>
public sealed class ShiftEditTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ShiftEditTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid ExpenseId, Guid LineId, Guid ShiftId)> SetupAsync(
        string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("SE-B-" + suffix, "فرع تعديل الشفت " + suffix);
        var supplier = await _db.PartnerRepo.CreateAsync(PartnerKind.Supplier, new PartnerInput
        { Code = "SE-S-" + suffix, NameAr = "مورد تعديل الشفت " + suffix });
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.SupplierPayable,
            chart["2100"].Id);
        var shift = await _db.TreasuryService().OpenShiftAsync(actor, new ShiftInput
        { BranchId = branch });

        var expenseId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO operating_expenses
                (id, branch_id, doc_date, supplier_id, status, total_amount, created_by)
            VALUES (@id, @branch, current_date, @supplier, 'DRAFT', 100, @user)
            """, new { id = expenseId, branch, supplier = supplier.Id, user = actor.UserId });
        await connection.ExecuteAsync("""
            INSERT INTO operating_expense_lines
                (id, expense_id, line_no, account_id, description, quantity, unit_price, amount)
            VALUES (@id, @expense, 1, @account, 'بند اختبار', 1, 100, 100)
            """, new { id = lineId, expense = expenseId, account = chart["5490"].Id });
        return (branch, expenseId, lineId, shift.Id);
    }

    private static async Task<(decimal Amount, decimal Total)> ReadAsync(Guid expenseId, Guid lineId)
    {
        await using var c = new NpgsqlConnection(TestDb.AdminConnection);
        await c.OpenAsync();
        var amount = await c.ExecuteScalarAsync<decimal>(
            "SELECT amount FROM operating_expense_lines WHERE id = @id", new { id = lineId });
        var total = await c.ExecuteScalarAsync<decimal>(
            "SELECT total_amount FROM operating_expenses WHERE id = @id", new { id = expenseId });
        return (amount, total);
    }

    [Fact]
    public async Task Editing_the_amount_and_quantity_updates_the_original_document()
    {
        var actor = await _db.AdminActorAsync();
        var s = await SetupAsync("1");
        var svc = _db.ShiftEdit();

        await svc.ApplyAsync(actor, new ShiftLineEditInput
        {
            ShiftId = s.ShiftId, EntityType = "operating_expense", DocId = s.ExpenseId,
            LineId = s.LineId, Field = "amount", NewValue = "150"
        });
        var afterAmount = await ReadAsync(s.ExpenseId, s.LineId);
        Assert.Equal(150m, afterAmount.Amount);
        Assert.Equal(150m, afterAmount.Total);

        await svc.ApplyAsync(actor, new ShiftLineEditInput
        {
            ShiftId = s.ShiftId, EntityType = "operating_expense", DocId = s.ExpenseId,
            LineId = s.LineId, Field = "quantity", NewValue = "3"
        });
        var afterQty = await ReadAsync(s.ExpenseId, s.LineId);
        Assert.Equal(300m, afterQty.Amount); // 3 × 100
        Assert.Equal(300m, afterQty.Total);
    }

    [Fact]
    public async Task Editing_is_blocked_after_the_shift_is_reviewed()
    {
        var actor = await _db.AdminActorAsync();
        var s = await SetupAsync("2");
        var treasury = _db.TreasuryService();
        await treasury.CloseShiftAsync(actor, s.ShiftId);
        await treasury.ReviewShiftAsync(actor, s.ShiftId);

        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.ShiftEdit().ApplyAsync(actor, new ShiftLineEditInput
            {
                ShiftId = s.ShiftId, EntityType = "operating_expense", DocId = s.ExpenseId,
                LineId = s.LineId, Field = "amount", NewValue = "999"
            }));
        Assert.Equal("validation.shift_edit_locked", error.Code);
    }

    [Fact]
    public async Task Editing_a_posted_expense_amount_posts_a_difference_entry()
    {
        var actor = await _db.AdminActorAsync();
        var s = await SetupAsync("3");
        await using (var c = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await c.OpenAsync();
            await c.ExecuteAsync("UPDATE operating_expenses SET status = 'POSTED' WHERE id = @id",
                new { id = s.ExpenseId });
        }

        await _db.ShiftEdit().ApplyAsync(actor, new ShiftLineEditInput
        {
            ShiftId = s.ShiftId, EntityType = "operating_expense", DocId = s.ExpenseId,
            LineId = s.LineId, Field = "amount", NewValue = "150"
        });

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var diff = await connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(total_debit), 0) FROM journal_entries
            WHERE source_type = 'review_adjustment' AND source_id = @id
            """, new { id = s.ExpenseId });
        Assert.Equal(50m, diff); // 150 − 100
    }
}
