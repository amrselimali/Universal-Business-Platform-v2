using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IServiceTransferRepository
{
    /// <summary>الخدمات/الجلسات المتبقية القابلة للتحويل لعميل داخل الفرع.</summary>
    Task<IReadOnlyList<TransferableServiceRow>> ListAvailableAsync(Guid branchId, Guid customerId,
        CancellationToken ct = default);

    /// <summary>إنشاء مسودة تحويل (بلا أي أثر على الحسابات أو أرصدة العميل حتى الترحيل).</summary>
    Task<ServiceTransfer> CreateAsync(ServiceTransferInput input, Guid userId,
        CancellationToken ct = default);

    /// <summary>ترحيل التحويل: نقل الكميات والقيمة وإنشاء القيد (من حساب المحوَّل إليه إلى المحوَّل منه).</summary>
    Task<ServiceTransfer> PostAsync(Guid transferId, Guid userId, CancellationToken ct = default);

    Task<ServiceTransfer?> GetAsync(Guid transferId, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceTransfer>> ListAsync(Guid branchId, int limit = 100,
        CancellationToken ct = default);
}

/// <summary>
/// تحويل خدمات/جلسات بين العملاء من نقطة البيع. المسودة لا تُغيّر أي رصيد أو حساب؛
/// وعند الترحيل (اعتماد المراجعة) تُخصم الكمية من رصيد العميل المحوَّل منه وتُضاف — بإنشاء
/// حجز للعميل المحوَّل إليه — ويُنشأ قيد من حساب العميل المحوَّل إليه (مدين) إلى حساب
/// العميل المحوَّل منه (دائن) بقيمة الوحدات المحوَّلة.
/// </summary>
public sealed class ServiceTransferRepository : IServiceTransferRepository
{
    private readonly IDbConnectionFactory _factory;
    public ServiceTransferRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed class SourceLine
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public Guid WarehouseId { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal ReservedQty { get; set; }
        public Guid CustomerId { get; set; }
        public Guid BranchId { get; set; }
    }

    public async Task<IReadOnlyList<TransferableServiceRow>> ListAvailableAsync(Guid branchId,
        Guid customerId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<TransferableServiceRow>(new CommandDefinition("""
            SELECT l.id AS BookingLineId, l.item_id AS ItemId, i.code AS ItemCode,
                   i.name_ar AS ItemName, l.unit_id AS UnitId, l.warehouse_id AS WarehouseId,
                   l.reserved_qty AS RemainingQty, l.unit_price AS UnitPrice
            FROM booking_lines l
            JOIN bookings b ON b.id = l.booking_id
            JOIN items i ON i.id = l.item_id
            WHERE b.branch_id = @branchId AND b.customer_id = @customerId
              AND b.status NOT IN ('cancelled', 'reversed')
              AND l.reserved_qty > 0
            ORDER BY i.name_ar, l.line_no
            """, new { branchId, customerId }, cancellationToken: ct))).ToList();
    }

    public async Task<ServiceTransfer> CreateAsync(ServiceTransferInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.FromCustomerId == Guid.Empty ||
            input.ToCustomerId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.FromCustomerId == input.ToCustomerId)
            throw new ValidationAppException("validation.transfer_same_customer");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var transferId = Guid.CreateVersion7();
        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('SERVICE_TRANSFER'::varchar, @branchId::uuid,
                                        @date::date, 'TR'::varchar, 6::smallint)
            """, new { branchId = input.BranchId, date = input.DocDate.Date }, tx,
            cancellationToken: ct));

        var totalQty = 0m;
        var totalAmount = 0m;
        var lineNo = 0;
        var rows = new List<(int LineNo, SourceLine Src, decimal Qty, decimal Amount)>();

        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.quantity_positive");

            var src = await connection.QuerySingleOrDefaultAsync<SourceLine>(new CommandDefinition("""
                SELECT l.id AS Id, l.item_id AS ItemId, l.unit_id AS UnitId,
                       l.warehouse_id AS WarehouseId, l.unit_price AS UnitPrice,
                       l.reserved_qty AS ReservedQty, b.customer_id AS CustomerId,
                       b.branch_id AS BranchId
                FROM booking_lines l
                JOIN bookings b ON b.id = l.booking_id
                WHERE l.id = @id AND b.status NOT IN ('cancelled', 'reversed')
                FOR UPDATE OF l
                """, new { id = line.FromBookingLineId }, tx, cancellationToken: ct));

            if (src is null || src.Id == Guid.Empty)
                throw new ValidationAppException("validation.transfer_source_not_found");
            if (src.CustomerId != input.FromCustomerId || src.BranchId != input.BranchId)
                throw new ValidationAppException("validation.transfer_source_mismatch");
            if (src.ReservedQty < line.Quantity)
                throw new ValidationAppException("validation.transfer_quantity_exceeds",
                    new Dictionary<string, string>
                    {
                        ["available"] = src.ReservedQty.ToString("N4"),
                        ["requested"] = line.Quantity.ToString("N4")
                    });

            var amount = Math.Round(line.Quantity * src.UnitPrice, 2,
                MidpointRounding.AwayFromZero);
            lineNo++;
            totalQty += line.Quantity;
            totalAmount += amount;
            rows.Add((lineNo, src, line.Quantity, amount));
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO service_transfers
                (id, branch_id, doc_no, doc_date, from_customer_id, to_customer_id, status,
                 total_quantity, total_amount, notes, created_by)
            VALUES (@transferId, @branchId, @docNo, @date, @fromCustomerId, @toCustomerId, 'draft',
                    @totalQty, @totalAmount, @notes, @userId)
            """, new
        {
            transferId, branchId = input.BranchId, docNo, date = input.DocDate.Date,
            fromCustomerId = input.FromCustomerId, toCustomerId = input.ToCustomerId,
            totalQty, totalAmount, notes = input.Notes, userId
        }, tx, cancellationToken: ct));

        foreach (var row in rows)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO service_transfer_lines
                    (id, transfer_id, line_no, from_booking_line_id, item_id, unit_id,
                     warehouse_id, quantity, unit_price, amount)
                VALUES (@id, @transferId, @lineNo, @srcLine, @itemId, @unitId, @warehouseId,
                        @quantity, @unitPrice, @amount)
                """, new
            {
                id = Guid.CreateVersion7(), transferId, lineNo = row.LineNo,
                srcLine = row.Src.Id, itemId = row.Src.ItemId, unitId = row.Src.UnitId,
                warehouseId = row.Src.WarehouseId, quantity = row.Qty,
                unitPrice = row.Src.UnitPrice, amount = row.Amount
            }, tx, cancellationToken: ct));
        }

        // نقل الكميات فورًا عند إنشاء المسودة: يظهر في الخدمات المحجوزة ويصبح قابلًا للتنفيذ.
        await ApplyQuantitiesCoreAsync(connection, tx, transferId, userId, ct);

        await tx.CommitAsync(ct);
        return (await GetAsync(transferId, ct))!;
    }

    public async Task<ServiceTransfer> PostAsync(Guid transferId, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await PostCoreAsync(connection, tx, transferId, userId, ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(transferId, ct))!;
    }

    /// <summary>
    /// ترحيل التحويل داخل معاملة قائمة (يُستخدم أيضًا عند اعتماد مراجعة الوردية).
    /// </summary>
    public static async Task PostCoreAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid transferId, Guid userId, CancellationToken ct = default)
    {
        var head = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId,
            string? DocNo, DateTime DocDate, Guid FromCustomerId, Guid ToCustomerId, string Status,
            Guid? TargetBookingId)>(new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, doc_no AS DocNo, doc_date AS DocDate,
                   from_customer_id AS FromCustomerId, to_customer_id AS ToCustomerId,
                   status AS Status, target_booking_id AS TargetBookingId
            FROM service_transfers WHERE id = @transferId FOR UPDATE
            """, new { transferId }, tx, cancellationToken: ct));

        if (head.Id == Guid.Empty) throw new NotFoundAppException();
        if (head.Status != ServiceTransferStatuses.Draft)
            throw new ValidationAppException("validation.transfer_not_draft");

        var lines = (await connection.QueryAsync<(Guid Id, int LineNo, Guid FromBookingLineId,
            Guid ItemId, Guid UnitId, Guid WarehouseId, decimal Quantity, decimal UnitPrice,
            decimal Amount)>(new CommandDefinition("""
            SELECT id AS Id, line_no AS LineNo, from_booking_line_id AS FromBookingLineId,
                   item_id AS ItemId, unit_id AS UnitId, warehouse_id AS WarehouseId,
                   quantity AS Quantity, unit_price AS UnitPrice, amount AS Amount
            FROM service_transfer_lines WHERE transfer_id = @transferId ORDER BY line_no
            """, new { transferId }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        var total = Math.Round(lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);

        // الكميات تُنقل عند إنشاء المسودة؛ نضمن تطبيقها هنا أيضًا (توافق المسودات القديمة).
        var bookingId = head.TargetBookingId
            ?? await ApplyQuantitiesCoreAsync(connection, tx, transferId, userId, ct);

        // 3) القيد: من حساب العميل المحوَّل إليه (مدين) إلى حساب العميل المحوَّل منه (دائن).
        Guid? entryId = null;
        if (total > 0)
            entryId = await PostTransferEntryAsync(connection, tx, head.BranchId, head.DocDate,
                head.DocNo ?? transferId.ToString("N")[..8], head.FromCustomerId,
                head.ToCustomerId, total, transferId, userId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE service_transfers
            SET status = 'posted', posted_at = now(), posted_by = @userId,
                journal_entry_id = @entryId, target_booking_id = @bookingId
            WHERE id = @transferId
            """, new { userId, entryId, bookingId, transferId }, tx, cancellationToken: ct));
    }

    /// <summary>
    /// نقل الكميات فعليًا (خصم من المحوَّل منه وإنشاء حجز للمحوَّل إليه) — يُنفَّذ عند إنشاء
    /// المسودة حتى تظهر في الخدمات المحجوزة وتكون قابلةً للتنفيذ قبل الترحيل. idempotent عبر
    /// target_booking_id (لا يُنفَّذ مرتين).
    /// </summary>
    public static async Task<Guid> ApplyQuantitiesCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid transferId, Guid userId, CancellationToken ct = default)
    {
        var head = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId,
            string? DocNo, DateTime DocDate, Guid ToCustomerId, string Status,
            Guid? TargetBookingId)>(new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, doc_no AS DocNo, doc_date AS DocDate,
                   to_customer_id AS ToCustomerId, status AS Status,
                   target_booking_id AS TargetBookingId
            FROM service_transfers WHERE id = @transferId FOR UPDATE
            """, new { transferId }, tx, cancellationToken: ct));
        if (head.Id == Guid.Empty) throw new NotFoundAppException();
        if (head.Status != ServiceTransferStatuses.Draft)
            throw new ValidationAppException("validation.transfer_not_draft");
        if (head.TargetBookingId is { } existing) return existing;

        var lines = (await connection.QueryAsync<(Guid Id, int LineNo, Guid FromBookingLineId,
            Guid ItemId, Guid UnitId, Guid WarehouseId, decimal Quantity, decimal UnitPrice,
            decimal Amount)>(new CommandDefinition("""
            SELECT id AS Id, line_no AS LineNo, from_booking_line_id AS FromBookingLineId,
                   item_id AS ItemId, unit_id AS UnitId, warehouse_id AS WarehouseId,
                   quantity AS Quantity, unit_price AS UnitPrice, amount AS Amount
            FROM service_transfer_lines WHERE transfer_id = @transferId ORDER BY line_no
            """, new { transferId }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        var total = Math.Round(lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);

        foreach (var line in lines)
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_lines
                SET reserved_qty = reserved_qty - @quantity,
                    transferred_qty = transferred_qty + @quantity
                WHERE id = @id AND reserved_qty >= @quantity
                """, new { quantity = line.Quantity, id = line.FromBookingLineId }, tx,
                cancellationToken: ct));
            if (affected == 0)
                throw new ValidationAppException("validation.transfer_quantity_exceeds");
        }

        var bookingId = Guid.CreateVersion7();
        var bookNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('BOOK'::varchar, @branchId::uuid, @date::date,
                                        'BK'::varchar, 6::smallint)
            """, new { branchId = head.BranchId, date = head.DocDate.Date }, tx,
            cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bookings
                (id, branch_id, doc_no, booking_date, status, customer_id, source,
                 total_amount, notes, created_by)
            VALUES (@bookingId, @branchId, @bookNo, @date, 'confirmed', @toCustomerId, 'transfer',
                    @total, @notes, @userId)
            """, new
        {
            bookingId, branchId = head.BranchId, bookNo, date = head.DocDate.Date,
            toCustomerId = head.ToCustomerId, total, notes = $"تحويل خدمات {head.DocNo}", userId
        }, tx, cancellationToken: ct));
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO booking_lines
                    (id, booking_id, line_no, item_id, unit_id, warehouse_id, quantity,
                     unit_price, net_amount, tax_amount, total_amount, reserved_qty,
                     transferred_qty)
                VALUES (@id, @bookingId, @lineNo, @itemId, @unitId, @warehouseId, @quantity,
                        @unitPrice, @amount, 0, @amount, @quantity, -@quantity)
                """, new
            {
                id = Guid.CreateVersion7(), bookingId, lineNo = line.LineNo,
                itemId = line.ItemId, unitId = line.UnitId, warehouseId = line.WarehouseId,
                quantity = line.Quantity, unitPrice = line.UnitPrice, amount = line.Amount
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE service_transfers SET target_booking_id = @bookingId WHERE id = @transferId",
            new { bookingId, transferId }, tx, cancellationToken: ct));
        return bookingId;
    }

    private static async Task<Guid> PostTransferEntryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid branchId, DateTime docDate, string docNo, Guid fromCustomerId,
        Guid toCustomerId, decimal amount, Guid transferId, Guid userId, CancellationToken ct)
    {
        var receivable = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT account_id FROM posting_defaults WHERE setting_key = @key",
            new { key = PostingKeys.CustomerReceivable }, tx, cancellationToken: ct));
        if (receivable is null || receivable == Guid.Empty)
            throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string>
                {
                    ["key"] = PostingKeys.CustomerReceivable, ["module"] = "pos"
                });

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'sales' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        var description = $"تحويل خدمات بين عملاء {docNo}";
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status,
                 source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @date, @docNo, @description, 'draft',
                    'service_transfer', @transferId, @amount, @amount, @userId)
            """, new
        {
            entryId, journalId, branchId, date = docDate.Date, docNo, description, transferId,
            amount, userId
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entry_lines
                (id, entry_id, line_no, account_id, description, debit, credit, customer_id,
                 branch_id, entry_date)
            VALUES (@id1, @entryId, 1, @accountId, @description, @amount, 0, @toCustomerId,
                    @branchId, @date),
                   (@id2, @entryId, 2, @accountId, @description, 0, @amount, @fromCustomerId,
                    @branchId, @date)
            """, new
        {
            id1 = Guid.CreateVersion7(), id2 = Guid.CreateVersion7(), entryId,
            accountId = receivable.Value, description, amount, toCustomerId, fromCustomerId,
            branchId, date = docDate.Date
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }

    public async Task<ServiceTransfer?> GetAsync(Guid transferId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var head = await connection.QuerySingleOrDefaultAsync<ServiceTransfer>(new CommandDefinition("""
            SELECT st.id AS Id, st.branch_id AS BranchId, st.doc_no AS DocNo, st.doc_date AS DocDate,
                   st.from_customer_id AS FromCustomerId, fc.name_ar AS FromCustomerName,
                   st.to_customer_id AS ToCustomerId, tc.name_ar AS ToCustomerName,
                   st.status AS Status, st.total_quantity AS TotalQuantity,
                   st.total_amount AS TotalAmount, st.journal_entry_id AS JournalEntryId,
                   st.target_booking_id AS TargetBookingId, st.notes AS Notes,
                   st.created_at AS CreatedAt, st.posted_at AS PostedAt
            FROM service_transfers st
            JOIN customers fc ON fc.id = st.from_customer_id
            JOIN customers tc ON tc.id = st.to_customer_id
            WHERE st.id = @transferId
            """, new { transferId }, cancellationToken: ct));
        if (head is null) return null;

        head.Lines = (await connection.QueryAsync<ServiceTransferLine>(new CommandDefinition("""
            SELECT l.id AS Id, l.line_no AS LineNo, l.from_booking_line_id AS FromBookingLineId,
                   l.item_id AS ItemId, i.code AS ItemCode, i.name_ar AS ItemName,
                   l.unit_id AS UnitId, l.warehouse_id AS WarehouseId, l.quantity AS Quantity,
                   l.unit_price AS UnitPrice, l.amount AS Amount
            FROM service_transfer_lines l
            JOIN items i ON i.id = l.item_id
            WHERE l.transfer_id = @transferId ORDER BY l.line_no
            """, new { transferId }, cancellationToken: ct))).ToList();
        return head;
    }

    public async Task<IReadOnlyList<ServiceTransfer>> ListAsync(Guid branchId, int limit = 100,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<ServiceTransfer>(new CommandDefinition("""
            SELECT st.id AS Id, st.branch_id AS BranchId, st.doc_no AS DocNo, st.doc_date AS DocDate,
                   st.from_customer_id AS FromCustomerId, fc.name_ar AS FromCustomerName,
                   st.to_customer_id AS ToCustomerId, tc.name_ar AS ToCustomerName,
                   st.status AS Status, st.total_quantity AS TotalQuantity,
                   st.total_amount AS TotalAmount, st.journal_entry_id AS JournalEntryId,
                   st.notes AS Notes, st.created_at AS CreatedAt, st.posted_at AS PostedAt
            FROM service_transfers st
            JOIN customers fc ON fc.id = st.from_customer_id
            JOIN customers tc ON tc.id = st.to_customer_id
            WHERE st.branch_id = @branchId
            ORDER BY st.created_at DESC
            LIMIT @limit
            """, new { branchId, limit }, cancellationToken: ct))).ToList();
    }
}
