using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesInvoiceRepository
{
    private sealed class PostHead
    {
        public Guid Id { get; set; }
        public string DocType { get; set; } = CommercialDocTypes.SalesInvoice;
        public Guid BranchId { get; set; }
        public DateTime DocDate { get; set; }
        public string Status { get; set; } = CommercialStatuses.Draft;
        public Guid? CustomerId { get; set; }
        public Guid? BookingId { get; set; }
        public Guid? OriginalDocumentId { get; set; }
        public decimal Subtotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal Total { get; set; }
        public decimal AllocatedTotal { get; set; }
    }

    private sealed class PostLine
    {
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public Guid? BookingLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public Guid? OriginalLineId { get; set; }
        public Guid WarehouseId { get; set; }
        public decimal UnitCost { get; set; }
    }

    private sealed record EntryLine(Guid AccountId, decimal Debit, decimal Credit,
        Guid? CustomerId = null);

    public async Task<Guid> PostAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var head = await LockHeadAsync(connection, tx, id, ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
            new { docDate = head.DocDate, branchId = head.BranchId }, tx, cancellationToken: ct));

        if (head.DocType == CommercialDocTypes.SalesInvoice)
            await PostInvoiceCoreAsync(connection, tx, head, userId, ct);
        else if (head.DocType == CommercialDocTypes.SalesReturn)
            await PostReturnCoreAsync(connection, tx, head, userId, ct);
        else
            throw new ValidationAppException("validation.not_supported_document");

        // إجماليات اللوحة ضمن نفس المعاملة: تنجح مع الترحيل أو تتراجع معه.
        await UpsertDailyTotalsAsync(connection, tx, head,
            head.DocType == CommercialDocTypes.SalesInvoice ? 1 : -1, ct);

        await tx.CommitAsync(ct);
        return id;
    }

    private static async Task<PostHead> LockHeadAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid id, CancellationToken ct)
    {
        var head = await connection.QuerySingleOrDefaultAsync<PostHead>(new CommandDefinition("""
            SELECT id AS Id, doc_type AS DocType, branch_id AS BranchId, doc_date AS DocDate,
                   status AS Status, customer_id AS CustomerId, booking_id AS BookingId,
                   original_document_id AS OriginalDocumentId, subtotal AS Subtotal,
                   tax_total AS TaxTotal, total AS Total, allocated_total AS AllocatedTotal
            FROM commercial_documents WHERE id = @id FOR UPDATE
            """, new { id }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (head.Status != CommercialStatuses.Draft)
            throw new ValidationAppException("validation.document_not_draft");
        if (head.CustomerId is null)
            throw new ValidationAppException("validation.party_required");
        return head;
    }

    private static async Task<List<PostLine>> LoadLinesAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid id, CancellationToken ct) =>
        (await connection.QueryAsync<PostLine>(new CommandDefinition("""
            SELECT l.line_no AS LineNo, l.item_id AS ItemId, l.unit_id AS UnitId,
                   l.booking_line_id AS BookingLineId, l.quantity AS Quantity,
                   l.net_amount AS NetAmount, l.tax_amount AS TaxAmount,
                   l.total_amount AS TotalAmount, r.original_line_id AS OriginalLineId,
                   COALESCE(b.warehouse_id, '00000000-0000-0000-0000-000000000000'::uuid) AS WarehouseId,
                   COALESCE((SELECT SUM(dnl.cost_total) / NULLIF(SUM(dnl.quantity), 0)
                             FROM delivery_note_lines dnl
                             WHERE dnl.booking_line_id = l.booking_line_id), 0) AS UnitCost
            FROM commercial_document_lines l
            LEFT JOIN sales_return_links r ON r.return_line_id = l.id
            LEFT JOIN booking_lines b ON b.id = l.booking_line_id
            WHERE l.document_id = @id ORDER BY l.line_no
            """, new { id }, tx, cancellationToken: ct))).ToList();

    private static async Task<Dictionary<string, Guid>> LoadDefaultsAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct) =>
        (await connection.QueryAsync<(string Key, Guid? AccountId)>(new CommandDefinition(
            "SELECT setting_key, account_id FROM posting_defaults", transaction: tx,
            cancellationToken: ct)))
        .Where(r => r.AccountId.HasValue)
        .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

    private static Guid Need(Dictionary<string, Guid> defaults, string key, string module) =>
        defaults.TryGetValue(key, out var account) ? account
            : throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string> { ["key"] = key, ["module"] = module });

    private static async Task<Guid> InsertEntryAsync(NpgsqlConnection connection,
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
