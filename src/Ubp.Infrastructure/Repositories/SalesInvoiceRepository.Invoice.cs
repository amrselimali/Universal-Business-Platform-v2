using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesInvoiceRepository
{
    private sealed class OpenVoucherRow
    {
        public Guid Id { get; set; }
        public decimal Remaining { get; set; }
    }

    /// <summary>
    /// ترحيل فاتورة مبيعات: Dr ذمم العملاء / Cr الإيراد + ضريبة المخرجات،
    /// ثم مقاصة الدفعات المقدمة المتاحة بنفس المعاملة (D5/N12).
    /// </summary>
    private static async Task PostInvoiceCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHead head, Guid userId, CancellationToken ct)
    {
        var lines = await LoadLinesAsync(connection, tx, head.Id, ct);
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        var defaults = await LoadDefaultsAsync(connection, tx, ct);
        var receivable = Need(defaults, PostingKeys.CustomerReceivable, "sales");
        var revenue = Need(defaults, PostingKeys.SalesRevenue, "sales");
        var vat = head.TaxTotal > 0 ? Need(defaults, PostingKeys.VatOutput, "sales") : (Guid?)null;

        var gl = new List<EntryLine> { new(receivable, head.Total, 0, head.CustomerId) };
        var netTotal = lines.Sum(l => l.NetAmount);
        if (netTotal != 0) gl.Add(new EntryLine(revenue, 0, netTotal));
        if (vat is not null) gl.Add(new EntryLine(vat.Value, 0, head.TaxTotal));

        var difference = Math.Round(gl.Sum(x => x.Debit) - gl.Sum(x => x.Credit), 2,
            MidpointRounding.AwayFromZero);
        if (difference != 0)
        {
            var rounding = Need(defaults, PostingKeys.RoundingDifference, "sales");
            gl.Add(new EntryLine(rounding, difference > 0 ? difference : 0,
                difference < 0 ? -difference : 0));
        }

        var docNo = await NextNumberAsync(connection, tx, "SALES_INVOICE", head.BranchId,
            head.DocDate, "SI", ct);
        var entryId = await InsertEntryAsync(connection, tx, "sales", head.BranchId, head.DocDate,
            docNo, $"فاتورة مبيعات {docNo}", gl, userId, "invoice", head.Id, ct);

        var allocated = await AllocateAdvancesAsync(connection, tx, head, docNo, userId, ct);
        if (allocated > 0)
        {
            var advances = Need(defaults, PostingKeys.CustomerAdvances, "sales");
            await InsertEntryAsync(connection, tx, "sales", head.BranchId, head.DocDate, docNo,
                $"مقاصة دفعة مقدمة {docNo}", new List<EntryLine>
                {
                    new(advances, allocated, 0, head.CustomerId),
                    new(receivable, 0, allocated, head.CustomerId)
                }, userId, "invoice", null, ct);
        }

        foreach (var line in lines.Where(l => l.BookingLineId is not null))
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE booking_lines SET invoiced_qty = invoiced_qty + @quantity WHERE id = @id",
                new { quantity = line.Quantity, id = line.BookingLineId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE commercial_documents SET status = 'posted', doc_no = @docNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @entryId,
                allocated_total = allocated_total + @allocated, updated_by = @userId
            WHERE id = @id
            """, new { docNo, userId, entryId, allocated, id = head.Id }, tx, cancellationToken: ct));
    }

    /// <summary>مقاصة أرصدة سندات قبض العميل المرحّلة على الفاتورة بالأقدم أولاً.</summary>
    private static async Task<decimal> AllocateAdvancesAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHead head, string docNo, Guid userId, CancellationToken ct)
    {
        var vouchers = (await connection.QueryAsync<OpenVoucherRow>(new CommandDefinition("""
            SELECT v.id AS Id,
                   v.total_amount - COALESCE(SUM(a.amount), 0) AS Remaining
            FROM vouchers v
            LEFT JOIN document_allocations a ON a.source_voucher_id = v.id
            WHERE v.status = 'posted' AND v.voucher_type = 'receipt'
              AND v.party_type = 'C' AND v.party_id = @customerId
            GROUP BY v.id, v.doc_date, v.total_amount
            HAVING v.total_amount - COALESCE(SUM(a.amount), 0) > 0
            ORDER BY v.doc_date, v.id
            LIMIT 50
            """, new { customerId = head.CustomerId }, tx, cancellationToken: ct))).ToList();

        var remaining = Math.Round(head.Total - head.AllocatedTotal, 2, MidpointRounding.AwayFromZero);
        decimal allocated = 0;
        foreach (var voucher in vouchers)
        {
            if (remaining <= 0) break;
            var take = Math.Min(voucher.Remaining, remaining);
            if (take <= 0) continue;

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO document_allocations (id, source_voucher_id, target_document_id, amount,
                    created_by)
                VALUES (@id, @voucherId, @documentId, @amount, @userId)
                """, new { id = Guid.CreateVersion7(), voucherId = voucher.Id, documentId = head.Id,
                    amount = take, userId }, tx, cancellationToken: ct));
            allocated += take;
            remaining -= take;
        }

        return Math.Round(allocated, 2, MidpointRounding.AwayFromZero);
    }

    private static Task<string> NextNumberAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string docType, Guid branchId, DateTime docDate, string prefix, CancellationToken ct) =>
        connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number(@docType::varchar, @branchId::uuid, @docDate::date,
                                        @prefix::varchar, 6::smallint)
            """, new { docType, branchId, docDate, prefix }, tx, cancellationToken: ct))!;
}
