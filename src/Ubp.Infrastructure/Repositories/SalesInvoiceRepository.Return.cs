using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesInvoiceRepository
{
    /// <summary>
    /// ترحيل مرتجع مبيعات: إشعار دائن (Dr إيراد + ضريبة / Cr ذمم العملاء)،
    /// وإعادة المخزون بتكلفة الأصل (D22) مع عكس تكلفة البيع.
    /// </summary>
    private static async Task PostReturnCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHead head, Guid userId, CancellationToken ct)
    {
        var lines = await LoadLinesAsync(connection, tx, head.Id, ct);
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        if (lines.Any(l => l.OriginalLineId is null))
            throw new ValidationAppException("validation.original_line_required");

        var defaults = await LoadDefaultsAsync(connection, tx, ct);
        var revenue = Need(defaults, PostingKeys.SalesRevenue, "sales");
        var receivable = Need(defaults, PostingKeys.CustomerReceivable, "sales");
        var vat = head.TaxTotal > 0 ? Need(defaults, PostingKeys.VatOutput, "sales") : (Guid?)null;

        var netTotal = lines.Sum(l => l.NetAmount);
        var gl = new List<EntryLine>();
        if (netTotal != 0) gl.Add(new EntryLine(revenue, netTotal, 0));
        if (vat is not null) gl.Add(new EntryLine(vat.Value, head.TaxTotal, 0));
        gl.Add(new EntryLine(receivable, 0, head.Total, head.CustomerId));

        var difference = Math.Round(gl.Sum(x => x.Debit) - gl.Sum(x => x.Credit), 2,
            MidpointRounding.AwayFromZero);
        if (difference != 0)
        {
            var rounding = Need(defaults, PostingKeys.RoundingDifference, "sales");
            gl.Add(new EntryLine(rounding, difference > 0 ? difference : 0,
                difference < 0 ? -difference : 0));
        }

        var docNo = await NextNumberAsync(connection, tx, "SALES_RETURN", head.BranchId,
            head.DocDate, "SR", ct);
        var entryId = await InsertEntryAsync(connection, tx, "sales", head.BranchId, head.DocDate,
            docNo, $"مرتجع مبيعات {docNo}", gl, userId, "invoice", head.Id, ct);

        var costTotal = await ReturnStockAsync(connection, tx, head, lines, docNo, userId, ct);
        if (costTotal > 0)
        {
            var inventory = Need(defaults, PostingKeys.StockInventory, "sales");
            var cogs = Need(defaults, PostingKeys.StockCogs, "sales");
            await InsertEntryAsync(connection, tx, "stock", head.BranchId, head.DocDate, docNo,
                $"عكس تكلفة مرتجع {docNo}", new List<EntryLine>
                {
                    new(inventory, costTotal, 0),
                    new(cogs, 0, costTotal)
                }, userId, "invoice", null, ct);
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE commercial_documents SET status = 'posted', doc_no = @docNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @entryId, updated_by = @userId
            WHERE id = @id
            """, new { docNo, userId, entryId, id = head.Id }, tx, cancellationToken: ct));
    }

    /// <summary>إدخال المخزون بتكلفة الأصل، وإرجاع إجمالي التكلفة المعكوسة.</summary>
    private static async Task<decimal> ReturnStockAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHead head, IReadOnlyList<PostLine> lines, string docNo,
        Guid userId, CancellationToken ct)
    {
        decimal total = 0;
        foreach (var group in lines.GroupBy(l => l.WarehouseId)
                     .Where(g => g.Key != Guid.Empty))
        {
            var stockDocId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_documents (id, doc_type, doc_date, branch_id, warehouse_id, status,
                    notes, created_by)
                VALUES (@stockDocId, @docType, @docDate, @branchId, @warehouseId, 'DRAFT', @notes,
                        @userId)
                """, new
            {
                stockDocId, docType = StockDocumentTypes.Receipt, docDate = head.DocDate,
                branchId = head.BranchId, warehouseId = group.Key,
                notes = $"مرتجع مبيعات {docNo}", userId
            }, tx, cancellationToken: ct));

            foreach (var line in group)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO stock_document_lines (document_id, line_no, direction, item_id,
                        unit_id, conversion_factor, quantity, base_quantity, unit_cost)
                    VALUES (@stockDocId, @LineNo, 'IN', @ItemId, @UnitId, 1, @Quantity, @Quantity,
                            @UnitCost)
                    """, new { stockDocId, line.LineNo, line.ItemId, line.UnitId, line.Quantity,
                        line.UnitCost }, tx, cancellationToken: ct));
                total += Math.Round(line.Quantity * line.UnitCost, 2, MidpointRounding.AwayFromZero);
            }

            await StockPostingRepository.PostCoreAsync(connection, tx, stockDocId, userId, false, ct);
        }

        return Math.Round(total, 2, MidpointRounding.AwayFromZero);
    }
}
