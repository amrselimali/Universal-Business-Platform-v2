using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PosRepository
{
    private static async Task<Dictionary<Guid, decimal>> LoadTaxRatesAsync(NpgsqlConnection c,
        NpgsqlTransaction tx, PosCheckoutInput input, CancellationToken ct)
    {
        var ids = input.Lines.Where(l => l.TaxId is not null).Select(l => l.TaxId!.Value)
            .Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, decimal>();
        return (await c.QueryAsync<(Guid Id, decimal Rate)>(new CommandDefinition(
            "SELECT id, rate FROM taxes WHERE id = ANY(@ids)", new { ids }, tx,
            cancellationToken: ct))).ToDictionary(x => x.Id, x => x.Rate);
    }

    private static List<BuiltLine> BuildLines(PosCheckoutInput input,
        Dictionary<Guid, decimal> rates)
    {
        var result = new List<BuiltLine>();
        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            lineNo++;
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.booking_quantity");
            if (line.ItemId == Guid.Empty || line.UnitId == Guid.Empty)
                throw new ValidationAppException("validation.required_fields");

            var gross = line.Quantity * line.UnitPrice;
            var discount = Math.Round(gross * line.DiscountPercent / 100m, 2,
                MidpointRounding.AwayFromZero);
            var net = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
            var rate = line.TaxId is not null && rates.TryGetValue(line.TaxId.Value, out var value)
                ? value : 0m;
            var tax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);

            result.Add(new BuiltLine
            {
                LineNo = lineNo, ItemId = line.ItemId, UnitId = line.UnitId, TaxId = line.TaxId,
                Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent, NetAmount = net, TaxAmount = tax,
                TotalAmount = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero)
            });
        }
        return result;
    }

    private static async Task<Guid> CreateStockDocAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid deliveryId, PosCheckoutInput input,
        IReadOnlyList<(Guid Id, BuiltLine Line)> bookingLines, Guid userId, CancellationToken ct)
    {
        var stockDocId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO stock_documents (id, doc_type, doc_date, branch_id, warehouse_id, status,
                notes, created_by)
            VALUES (@stockDocId, @docType, @docDate, @BranchId, @WarehouseId, 'DRAFT', @notes, @userId)
            """, new { stockDocId, docType = StockDocumentTypes.Issue,
                docDate = input.DocDate.Date, input.BranchId, input.WarehouseId,
                notes = $"POS {deliveryId:N}", userId }, tx, cancellationToken: ct));

        foreach (var (_, line) in bookingLines)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_document_lines (document_id, line_no, direction, item_id, unit_id,
                    conversion_factor, quantity, base_quantity, unit_cost)
                VALUES (@stockDocId, @LineNo, 'OUT', @ItemId, @UnitId, 1, @Quantity, @Quantity, 0)
                """, new { stockDocId, line.LineNo, line.ItemId, line.UnitId, line.Quantity }, tx,
                cancellationToken: ct));
        }

        await StockPostingRepository.PostCoreAsync(connection, tx, stockDocId, userId, false, ct);
        return stockDocId;
    }

    private static Task<string> NextNumberAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string docType, Guid branchId, DateTime docDate, string prefix, CancellationToken ct) =>
        connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number(@docType::varchar, @branchId::uuid, @docDate::date,
                                        @prefix::varchar, 6::smallint)
            """, new { docType, branchId, docDate, prefix }, tx, cancellationToken: ct))!;

    private static async Task<Guid> NeedAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string key, CancellationToken ct)
    {
        var account = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT account_id FROM posting_defaults WHERE setting_key = @key", new { key }, tx,
            cancellationToken: ct));
        return account ?? throw new ValidationAppException("validation.posting_account_missing",
            new Dictionary<string, string> { ["key"] = key, ["module"] = "sales" });
    }

    private static async Task<Guid> PostEntryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, string journalType, Guid branchId, DateTime docDate, string docNo,
        string description, IReadOnlyList<EntryLine> gl, Guid userId, string sourceType,
        Guid? sourceId, CancellationToken ct)
    {
        var debit = gl.Sum(x => x.Debit);
        var credit = gl.Sum(x => x.Credit);
        if (Math.Round(debit - credit, 2, MidpointRounding.AwayFromZero) != 0)
            throw new ValidationAppException("validation.entry_not_balanced");

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = @journalType AND is_active " +
            "ORDER BY sort_order LIMIT 1", new { journalType }, tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries (id, journal_id, branch_id, entry_date, reference,
                description, status, source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @docDate, @docNo, @description, 'draft',
                    @sourceType, @sourceId, @debit, @credit, @userId)
            """, new { entryId, journalId, branchId, docDate, docNo, description, sourceType,
                sourceId, debit, credit, userId }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines (id, entry_id, line_no, account_id, description,
                    debit, credit, customer_id, branch_id, entry_date)
                VALUES (@lineId, @entryId, @lineNo, @accountId, @description, @debit, @credit,
                        @customerId, @branchId, @docDate)
                """, new
            {
                lineId = Guid.CreateVersion7(), entryId, lineNo, accountId = line.AccountId,
                description, debit = line.Debit, credit = line.Credit,
                customerId = line.CustomerId, branchId, docDate
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }
}
