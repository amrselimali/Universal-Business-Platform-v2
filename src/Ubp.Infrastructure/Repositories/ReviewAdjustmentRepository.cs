using System.Globalization;
using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IReviewAdjustmentRepository
{
    Task<ReviewAdjustment> RecordAsync(ReviewAdjustmentInput input, Guid userId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ReviewAdjustment>> ListAsync(Guid shiftId, CancellationToken ct = default);
    Task<IReadOnlyList<ReviewNote>> ListNotesAsync(string entityType, Guid entityId,
        CancellationToken ct = default);
}

/// <summary>
/// تعديلات المراجعة: تُسجَّل أولًا (مع الملاحظة في سطر مستقل)، ويُطبّقها اعتماد المراجعة على
/// المستند الأصلي مع قيد بالفرق (موجب/سالب) — 0084.
/// </summary>
public sealed class ReviewAdjustmentRepository : IReviewAdjustmentRepository
{
    private readonly IDbConnectionFactory _factory;
    public ReviewAdjustmentRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<ReviewAdjustment> RecordAsync(ReviewAdjustmentInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.EntityType) ||
            !ReviewAdjustmentRules.IsEditable(input.EntityType, input.Field))
            throw new ValidationAppException("validation.review_field_not_editable");
        if (input.ShiftId == Guid.Empty || input.EntityId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        input.LineId ??= await ResolveLineAsync(connection, tx, input.EntityType, input.EntityId, ct);
        var oldText = await ReadOldTextAsync(connection, tx, input, ct);
        decimal diffAmount = 0, diffQuantity = 0;
        if (decimal.TryParse(input.NewValue, NumberStyles.Any, CultureInfo.InvariantCulture,
                out var newNum) &&
            decimal.TryParse(oldText, NumberStyles.Any, CultureInfo.InvariantCulture, out var oldNum))
        {
            var delta = newNum - oldNum;
            if (input.Field == ReviewAdjustmentRules.Quantity) diffQuantity = delta;
            else if (input.Field is ReviewAdjustmentRules.Amount or ReviewAdjustmentRules.UnitPrice)
                diffAmount = delta;
        }

        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO review_adjustments
                (id, shift_id, entity_type, entity_id, line_id, field, old_value, new_value,
                 diff_amount, diff_quantity, note, status, created_by)
            VALUES (@id, @shiftId, @entityType, @entityId, @lineId, @field, @oldValue, @newValue,
                    @diffAmount, @diffQuantity, @note, 'pending', @userId)
            """, new { id, shiftId = input.ShiftId, entityType = input.EntityType,
                entityId = input.EntityId, lineId = input.LineId, field = input.Field,
                oldValue = oldText, newValue = input.NewValue, diffAmount, diffQuantity,
                note = input.Note, userId }, tx, cancellationToken: ct));

        if (!string.IsNullOrWhiteSpace(input.Note))
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO review_notes (id, shift_id, entity_type, entity_id, note, created_by)
                VALUES (@nid, @shiftId, @entityType, @entityId, @note, @userId)
                """, new { nid = Guid.CreateVersion7(), shiftId = input.ShiftId,
                    entityType = input.EntityType, entityId = input.EntityId, note = input.Note,
                    userId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new ReviewAdjustment
        {
            Id = id, ShiftId = input.ShiftId, EntityType = input.EntityType,
            EntityId = input.EntityId, LineId = input.LineId, Field = input.Field,
            OldValue = oldText, NewValue = input.NewValue, DiffAmount = diffAmount,
            DiffQuantity = diffQuantity, Note = input.Note, Status = "pending"
        };
    }

    public async Task<IReadOnlyList<ReviewAdjustment>> ListAsync(Guid shiftId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<ReviewAdjustment>(new CommandDefinition("""
            SELECT id AS Id, shift_id AS ShiftId, entity_type AS EntityType, entity_id AS EntityId,
                   line_id AS LineId, field AS Field, old_value AS OldValue, new_value AS NewValue,
                   diff_amount AS DiffAmount, diff_quantity AS DiffQuantity, note AS Note,
                   status AS Status, journal_entry_id AS JournalEntryId, created_at AS CreatedAt
            FROM review_adjustments WHERE shift_id = @shiftId ORDER BY created_at
            """, new { shiftId }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<ReviewNote>> ListNotesAsync(string entityType, Guid entityId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<ReviewNote>(new CommandDefinition("""
            SELECT id AS Id, shift_id AS ShiftId, entity_type AS EntityType, entity_id AS EntityId,
                   note AS Note, created_at AS CreatedAt
            FROM review_notes WHERE entity_type = @entityType AND entity_id = @entityId
            ORDER BY created_at
            """, new { entityType, entityId }, cancellationToken: ct))).ToList();
    }

    private static async Task<Guid?> ResolveLineAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, string entityType, Guid entityId, CancellationToken ct)
    {
        var sql = entityType switch
        {
            "voucher" => "SELECT id FROM voucher_lines WHERE voucher_id = @id ORDER BY line_no LIMIT 1",
            "operating_expense" =>
                "SELECT id FROM operating_expense_lines WHERE expense_id = @id ORDER BY line_no LIMIT 1",
            "booking" => "SELECT id FROM booking_lines WHERE booking_id = @id ORDER BY line_no LIMIT 1",
            "service_transfer" =>
                "SELECT id FROM service_transfer_lines WHERE transfer_id = @id ORDER BY line_no LIMIT 1",
            _ => null
        };
        if (sql is null) return null;
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new { id = entityId },
            tx, cancellationToken: ct));
    }

    private static async Task<string?> ReadOldTextAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ReviewAdjustmentInput input, CancellationToken ct)
    {
        var sql = (input.EntityType, input.Field) switch
        {
            ("booking", ReviewAdjustmentRules.Quantity) =>
                "SELECT quantity::text FROM booking_lines WHERE id = @lineId",
            ("booking", ReviewAdjustmentRules.UnitPrice) =>
                "SELECT unit_price::text FROM booking_lines WHERE id = @lineId",
            ("operating_expense", ReviewAdjustmentRules.Quantity) =>
                "SELECT quantity::text FROM operating_expense_lines WHERE id = @lineId",
            ("operating_expense", ReviewAdjustmentRules.UnitPrice) =>
                "SELECT unit_price::text FROM operating_expense_lines WHERE id = @lineId",
            ("operating_expense", ReviewAdjustmentRules.Amount) =>
                "SELECT amount::text FROM operating_expense_lines WHERE id = @lineId",
            ("operating_expense", ReviewAdjustmentRules.Description) =>
                "SELECT description FROM operating_expense_lines WHERE id = @lineId",
            ("voucher", ReviewAdjustmentRules.Amount) =>
                "SELECT amount::text FROM voucher_lines WHERE id = @lineId",
            ("service_transfer", ReviewAdjustmentRules.Quantity) =>
                "SELECT quantity::text FROM service_transfer_lines WHERE id = @lineId",
            _ => null
        };
        if (sql is null) return null;
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(sql,
            new { lineId = input.LineId }, tx, cancellationToken: ct));
    }

    /// <summary>تطبيق كل تعديلات المراجعة المعلّقة على المستندات الأصلية وإنشاء قيود الفرق — 0084.</summary>
    public static async Task ApplyPendingCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid shiftId, Guid userId, CancellationToken ct = default)
    {
        var pending = (await connection.QueryAsync<(Guid Id, string EntityType, Guid EntityId,
            Guid? LineId, string Field, string? NewValue)>(new CommandDefinition("""
            SELECT id AS Id, entity_type AS EntityType, entity_id AS EntityId, line_id AS LineId,
                   field AS Field, new_value AS NewValue
            FROM review_adjustments WHERE shift_id = @shiftId AND status = 'pending'
            ORDER BY created_at
            """, new { shiftId }, tx, cancellationToken: ct))).ToList();

        foreach (var adj in pending)
        {
            var lineId = adj.LineId ?? await ResolveLineAsync(connection, tx, adj.EntityType,
                adj.EntityId, ct);
            Guid? entryId = adj.EntityType switch
            {
                "booking" => await ApplyBookingAsync(connection, tx, adj.Id, lineId,
                    adj.Field, adj.NewValue, userId, ct),
                "operating_expense" => await ApplyExpenseAsync(connection, tx, adj.Id, lineId,
                    adj.Field, adj.NewValue, userId, ct),
                "voucher" => await ApplyVoucherAsync(connection, tx, lineId, adj.Field,
                    adj.NewValue, userId, ct),
                "service_transfer" => await ApplyServiceTransferAsync(connection, tx, lineId,
                    adj.Field, adj.NewValue, userId, ct),
                _ => null
            };
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE review_adjustments
                SET status = 'applied', applied_at = now(), journal_entry_id = @entryId
                WHERE id = @id
                """, new { entryId, id = adj.Id }, tx, cancellationToken: ct));
        }
    }

    /// <summary>قيد بالفرق (موجب أو سالب): مدين طرف ودائن آخر، مع ربط العميل/المورد عند الحاجة.</summary>
    private static async Task<Guid?> PostDiffAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid branchId, DateTime date, string docNo, string description,
        Guid debitAccount, Guid creditAccount, decimal amount, Guid? customerId,
        Guid? supplierId, Guid sourceId, Guid userId, CancellationToken ct)
    {
        if (amount == 0 || debitAccount == creditAccount) return null;
        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'sales' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");
        var entryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status,
                 source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @date, @docNo, @description, 'draft',
                    'review_adjustment', @sourceId, @amount, @amount, @userId)
            """, new { entryId, journalId, branchId, date = date.Date, docNo, description,
                sourceId, amount, userId }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entry_lines
                (id, entry_id, line_no, account_id, description, debit, credit, customer_id,
                 supplier_id, branch_id, entry_date)
            VALUES (@id1, @entryId, 1, @debit, @description, @amount, 0, @customerId,
                    @supplierId, @branchId, @date),
                   (@id2, @entryId, 2, @credit, @description, 0, @amount, NULL, NULL,
                    @branchId, @date)
            """, new { id1 = Guid.CreateVersion7(), id2 = Guid.CreateVersion7(), entryId,
                debit = debitAccount, credit = creditAccount, description, amount, customerId,
                supplierId, branchId, date = date.Date }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }

    private static async Task<Guid> NeedAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string key, CancellationToken ct)
    {
        var account = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT account_id FROM posting_defaults WHERE setting_key = @key", new { key }, tx,
            cancellationToken: ct));
        return account ?? throw new ValidationAppException("validation.posting_account_missing",
            new Dictionary<string, string> { ["key"] = key, ["module"] = "review" });
    }

    private static decimal ParseDec(string? text) =>
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static async Task<Guid?> ApplyBookingAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid adjId, Guid? lineId, string field, string? newValue,
        Guid userId, CancellationToken ct)
    {
        if (lineId is null) return null;
        var line = await connection.QuerySingleOrDefaultAsync<(Guid BookingId, Guid BranchId,
            string? DocNo, Guid CustomerId, decimal Quantity, decimal UnitPrice,
            decimal DiscountPercent, Guid? TaxId, decimal TotalAmount)>(
            new CommandDefinition("""
            SELECT l.booking_id AS BookingId, b.branch_id AS BranchId, b.doc_no AS DocNo,
                   b.customer_id AS CustomerId, l.quantity AS Quantity, l.unit_price AS UnitPrice,
                   l.discount_percent AS DiscountPercent, l.tax_id AS TaxId,
                   l.total_amount AS TotalAmount
            FROM booking_lines l JOIN bookings b ON b.id = l.booking_id
            WHERE l.id = @id FOR UPDATE OF l
            """, new { id = lineId }, tx, cancellationToken: ct));
        if (line.BookingId == Guid.Empty) return null;

        var qty = field == ReviewAdjustmentRules.Quantity ? ParseDec(newValue) : line.Quantity;
        var price = field == ReviewAdjustmentRules.UnitPrice ? ParseDec(newValue) : line.UnitPrice;
        if (qty <= 0) qty = line.Quantity;

        var gross = qty * price;
        var discount = Math.Round(gross * line.DiscountPercent / 100m, 2,
            MidpointRounding.AwayFromZero);
        var net = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
        var rate = line.TaxId is null ? 0m : await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition("SELECT COALESCE(rate,0) FROM taxes WHERE id = @id",
                new { id = line.TaxId }, tx, cancellationToken: ct));
        var tax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var total = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE booking_lines
            SET quantity = @qty, unit_price = @price, net_amount = @net, tax_amount = @tax,
                total_amount = @total, reserved_qty = GREATEST(reserved_qty + (@qty - @oldQty), 0)
            WHERE id = @id
            """, new { qty, price, net, tax, total, oldQty = line.Quantity, id = lineId }, tx,
            cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings SET total_amount = COALESCE(
                (SELECT SUM(total_amount) FROM booking_lines WHERE booking_id = @bookingId), 0)
            WHERE id = @bookingId
            """, new { bookingId = line.BookingId }, tx, cancellationToken: ct));

        var delta = total - line.TotalAmount;
        if (delta == 0) return null;
        var receivable = await NeedAsync(connection, tx, PostingKeys.CustomerReceivable, ct);
        var revenue = await NeedAsync(connection, tx, PostingKeys.SalesRevenue, ct);
        var docNo = line.DocNo ?? adjId.ToString("N")[..8];
        return delta > 0
            ? await PostDiffAsync(connection, tx, line.BranchId, DateTime.UtcNow.Date, docNo,
                $"فرق تعديل مراجعة حجز {docNo}", receivable, revenue, delta, line.CustomerId,
                null, adjId, userId, ct)
            : await PostDiffAsync(connection, tx, line.BranchId, DateTime.UtcNow.Date, docNo,
                $"فرق تعديل مراجعة حجز {docNo}", revenue, receivable, -delta, line.CustomerId,
                null, adjId, userId, ct);
    }

    private static async Task<Guid?> ApplyExpenseAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid adjId, Guid? lineId, string field, string? newValue,
        Guid userId, CancellationToken ct)
    {
        if (lineId is null) return null;
        var line = await connection.QuerySingleOrDefaultAsync<(Guid ExpenseId, Guid BranchId,
            string? DocNo, Guid AccountId, decimal Quantity, decimal UnitPrice, decimal Amount,
            Guid? TreasuryId, Guid? SupplierId)>(new CommandDefinition("""
            SELECT o.id AS ExpenseId, o.branch_id AS BranchId, o.doc_no AS DocNo,
                   l.account_id AS AccountId, l.quantity AS Quantity, l.unit_price AS UnitPrice,
                   l.amount AS Amount, o.treasury_id AS TreasuryId, o.supplier_id AS SupplierId
            FROM operating_expense_lines l JOIN operating_expenses o ON o.id = l.expense_id
            WHERE l.id = @id FOR UPDATE OF l
            """, new { id = lineId }, tx, cancellationToken: ct));
        if (line.ExpenseId == Guid.Empty) return null;

        if (field == ReviewAdjustmentRules.Description)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE operating_expense_lines SET description = @v WHERE id = @id",
                new { v = newValue, id = lineId }, tx, cancellationToken: ct));
            return null;
        }

        var qty = field == ReviewAdjustmentRules.Quantity ? ParseDec(newValue) : line.Quantity;
        var price = field == ReviewAdjustmentRules.UnitPrice ? ParseDec(newValue) : line.UnitPrice;
        if (qty <= 0) qty = line.Quantity;
        var amount = field == ReviewAdjustmentRules.Amount
            ? ParseDec(newValue)
            : Math.Round(qty * price, 2, MidpointRounding.AwayFromZero);
        if (amount < 0) amount = 0;

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE operating_expense_lines
            SET quantity = @qty, unit_price = @price, amount = @amount WHERE id = @id
            """, new { qty, price, amount, id = lineId }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE operating_expenses SET total_amount = COALESCE(
                (SELECT SUM(amount) FROM operating_expense_lines WHERE expense_id = @expenseId), 0)
            WHERE id = @expenseId
            """, new { expenseId = line.ExpenseId }, tx, cancellationToken: ct));

        var delta = amount - line.Amount;
        if (delta == 0) return null;
        Guid credit;
        Guid? supplierId = null;
        if (line.TreasuryId is { } tid)
            credit = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT account_id FROM treasuries WHERE id = @id", new { id = tid }, tx,
                cancellationToken: ct));
        else
        {
            credit = await NeedAsync(connection, tx, PostingKeys.SupplierPayable, ct);
            supplierId = line.SupplierId;
        }
        var docNo = line.DocNo ?? adjId.ToString("N")[..8];
        return delta > 0
            ? await PostDiffAsync(connection, tx, line.BranchId, DateTime.UtcNow.Date, docNo,
                $"فرق تعديل مراجعة مصروف {docNo}", line.AccountId, credit, delta, null,
                supplierId, adjId, userId, ct)
            : await PostDiffAsync(connection, tx, line.BranchId, DateTime.UtcNow.Date, docNo,
                $"فرق تعديل مراجعة مصروف {docNo}", credit, line.AccountId, -delta, null,
                supplierId, adjId, userId, ct);
    }

    private static async Task<Guid?> ApplyVoucherAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? lineId, string field, string? newValue, Guid userId,
        CancellationToken ct)
    {
        if (lineId is null || field != ReviewAdjustmentRules.Amount) return null;
        var line = await connection.QuerySingleOrDefaultAsync<(Guid VoucherId, Guid BranchId,
            string? DocNo, string VoucherType, string? PartyType, Guid? PartyId, Guid TreasuryId,
            decimal Amount)>(new CommandDefinition("""
            SELECT v.id AS VoucherId, v.branch_id AS BranchId, v.doc_no AS DocNo,
                   v.voucher_type AS VoucherType, v.party_type::text AS PartyType,
                   v.party_id AS PartyId, v.treasury_id AS TreasuryId, l.amount AS Amount
            FROM voucher_lines l JOIN vouchers v ON v.id = l.voucher_id
            WHERE l.id = @id FOR UPDATE OF l
            """, new { id = lineId }, tx, cancellationToken: ct));
        if (line.VoucherId == Guid.Empty) return null;

        var amount = ParseDec(newValue);
        if (amount <= 0) return null;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE voucher_lines SET amount = @amount WHERE id = @id",
            new { amount, id = lineId }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE vouchers SET total_amount = COALESCE(
                (SELECT SUM(amount) FROM voucher_lines WHERE voucher_id = @voucherId), 0)
            WHERE id = @voucherId
            """, new { voucherId = line.VoucherId }, tx, cancellationToken: ct));

        var delta = amount - line.Amount;
        if (delta == 0) return null;
        var treasury = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT account_id FROM treasuries WHERE id = @id", new { id = line.TreasuryId }, tx,
            cancellationToken: ct));
        var isReceipt = line.VoucherType == VoucherTypes.Receipt;
        var advance = isReceipt
            ? await NeedAsync(connection, tx, PostingKeys.CustomerAdvances, ct)
            : await NeedAsync(connection, tx, PostingKeys.SupplierAdvances, ct);
        Guid? customerId = isReceipt && line.PartyType == "C" ? line.PartyId : null;
        Guid? supplierId = !isReceipt && line.PartyType == "S" ? line.PartyId : null;
        var docNo = line.DocNo ?? line.VoucherId.ToString("N")[..8];

        var (debit, credit) = isReceipt ? (treasury, advance) : (advance, treasury);
        return delta > 0
            ? await PostDiffAsync(connection, tx, line.BranchId, DateTime.UtcNow.Date, docNo,
                $"فرق تعديل مراجعة سند {docNo}", debit, credit, delta, customerId, supplierId,
                line.VoucherId, userId, ct)
            : await PostDiffAsync(connection, tx, line.BranchId, DateTime.UtcNow.Date, docNo,
                $"فرق تعديل مراجعة سند {docNo}", credit, debit, -delta, customerId, supplierId,
                line.VoucherId, userId, ct);
    }

    /// <summary>تعديل كمية تحويل خدمات (مسودة) عند المراجعة قبل ترحيله — 0085.</summary>
    private static async Task<Guid?> ApplyServiceTransferAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? lineId, string field, string? newValue, Guid userId,
        CancellationToken ct)
    {
        if (lineId is null || field != ReviewAdjustmentRules.Quantity) return null;
        var newQty = ParseDec(newValue);
        if (newQty <= 0) return null;

        var line = await connection.QuerySingleOrDefaultAsync<(Guid TransferId, decimal UnitPrice)>(
            new CommandDefinition(
                "SELECT transfer_id AS TransferId, unit_price AS UnitPrice " +
                "FROM service_transfer_lines WHERE id = @id FOR UPDATE", new { id = lineId }, tx,
                cancellationToken: ct));
        if (line.TransferId == Guid.Empty) return null;

        var amount = Math.Round(newQty * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE service_transfer_lines SET quantity = @qty, amount = @amount WHERE id = @id",
            new { qty = newQty, amount, id = lineId }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE service_transfers
            SET total_quantity = COALESCE(
                    (SELECT SUM(quantity) FROM service_transfer_lines WHERE transfer_id = @t), 0),
                total_amount = COALESCE(
                    (SELECT SUM(amount) FROM service_transfer_lines WHERE transfer_id = @t), 0)
            WHERE id = @t AND status = 'draft'
            """, new { t = line.TransferId }, tx, cancellationToken: ct));
        return null;
    }
}
