using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IShiftEditRepository
{
    Task<IReadOnlyList<ShiftEditableLine>> ListEditableAsync(Guid shiftId,
        CancellationToken ct = default);
    Task<ShiftEditOutcome> ApplyAsync(ShiftLineEditInput input, Guid userId,
        CancellationToken ct = default);
}

/// <summary>
/// تعديل موحّد لسطور مستندات الوردية (مسودة) من تقرير الوردية: يسمح أثناء الوردية المفتوحة
/// (اليوزر العادي) أو أثناء الانتظار للمراجعة (المحاسب)، ويعدّل المستند الأصلي بكل آثاره — 0089.
/// </summary>
public sealed partial class ShiftEditRepository : IShiftEditRepository
{
    private readonly IDbConnectionFactory _factory;
    public ShiftEditRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed class ShiftWindow
    {
        public Guid BranchId { get; set; }
        public DateTimeOffset OpenedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string Status { get; set; } = "";
        public string ReviewStatus { get; set; } = "";
    }

    private static async Task<ShiftWindow> LoadShiftAsync(NpgsqlConnection connection,
        Guid shiftId, NpgsqlTransaction? tx, CancellationToken ct)
    {
        var shift = await connection.QuerySingleOrDefaultAsync<ShiftWindow>(new CommandDefinition("""
            SELECT branch_id AS BranchId, opened_at AS OpenedAt, closed_at AS ClosedAt,
                   status AS Status, review_status AS ReviewStatus
            FROM cashier_shifts WHERE id = @shiftId
            """, new { shiftId }, tx, cancellationToken: ct));
        return shift ?? throw new NotFoundAppException();
    }

    public async Task<IReadOnlyList<ShiftEditableLine>> ListEditableAsync(Guid shiftId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var s = await LoadShiftAsync(connection, shiftId, null, ct);
        var args = new { branchId = s.BranchId, openedAt = s.OpenedAt, until = s.ClosedAt };
        var rows = new List<ShiftEditableLine>();

        rows.AddRange(await connection.QueryAsync<ShiftEditableLine>(new CommandDefinition("""
            SELECT 'voucher' AS EntityType, v.id AS DocId, v.doc_no AS DocNo, v.doc_date AS DocDate,
                   l.id AS LineId, v.party_name AS PartyName, NULL::text AS ItemName,
                   NULL::text AS EmployeeName, t.name_ar AS TreasuryName,
                   0::numeric AS Quantity, l.amount AS UnitPrice, l.amount AS Amount,
                   NULL::text AS Description,
                   CASE WHEN v.status = 'posted' THEN 'amount' ELSE 'amount,treasury' END AS FieldsCsv,
                   v.treasury_id AS TreasuryId, NULL::uuid AS AccountId, NULL::uuid AS EmployeeId,
                   v.status AS DocStatus
            FROM vouchers v JOIN voucher_lines l ON l.voucher_id = v.id
            JOIN treasuries t ON t.id = v.treasury_id
            WHERE v.branch_id = @branchId AND v.status <> 'cancelled' AND v.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR v.created_at <= @until::timestamptz)
            """, args, cancellationToken: ct)));

        rows.AddRange(await connection.QueryAsync<ShiftEditableLine>(new CommandDefinition("""
            SELECT 'operating_expense' AS EntityType, o.id AS DocId, o.doc_no AS DocNo,
                   o.doc_date AS DocDate, l.id AS LineId, s.name_ar AS PartyName,
                   i.name_ar AS ItemName, NULL::text AS EmployeeName, t.name_ar AS TreasuryName,
                   l.quantity AS Quantity, l.unit_price AS UnitPrice, l.amount AS Amount,
                   l.description AS Description,
                   CASE WHEN o.status = 'POSTED'
                        THEN 'description,quantity,unit_price,amount'
                        ELSE 'account,treasury,description,quantity,unit_price,amount' END AS FieldsCsv,
                   o.treasury_id AS TreasuryId, l.account_id AS AccountId, NULL::uuid AS EmployeeId,
                   o.status AS DocStatus
            FROM operating_expenses o
            JOIN operating_expense_lines l ON l.expense_id = o.id
            JOIN suppliers s ON s.id = o.supplier_id
            LEFT JOIN items i ON i.id = l.item_id
            LEFT JOIN treasuries t ON t.id = o.treasury_id
            WHERE o.branch_id = @branchId AND o.status IN ('DRAFT', 'POSTED') AND o.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR o.created_at <= @until::timestamptz)
            """, args, cancellationToken: ct)));

        rows.AddRange(await connection.QueryAsync<ShiftEditableLine>(new CommandDefinition("""
            SELECT 'commercial_document' AS EntityType, d.id AS DocId, d.doc_no AS DocNo,
                   d.doc_date AS DocDate, l.id AS LineId, d.party_name AS PartyName,
                   i.name_ar AS ItemName, NULL::text AS EmployeeName, NULL::text AS TreasuryName,
                   l.quantity AS Quantity, l.unit_price AS UnitPrice, l.total_amount AS Amount,
                   l.description AS Description,
                   'quantity,unit_price,amount,description' AS FieldsCsv,
                   NULL::uuid AS TreasuryId, NULL::uuid AS AccountId, NULL::uuid AS EmployeeId,
                   d.status AS DocStatus
            FROM commercial_documents d
            JOIN commercial_document_lines l ON l.document_id = d.id
            JOIN items i ON i.id = l.item_id
            WHERE d.branch_id = @branchId AND d.status = 'draft' AND d.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR d.created_at <= @until::timestamptz)
            """, args, cancellationToken: ct)));

        rows.AddRange(await connection.QueryAsync<ShiftEditableLine>(new CommandDefinition("""
            SELECT 'booking' AS EntityType, b.id AS DocId, b.doc_no AS DocNo,
                   b.booking_date AS DocDate, l.id AS LineId, c.name_ar AS PartyName,
                   i.name_ar AS ItemName, e.name_ar AS EmployeeName, NULL::text AS TreasuryName,
                   l.quantity AS Quantity, l.unit_price AS UnitPrice, l.total_amount AS Amount,
                   NULL::text AS Description, 'quantity,unit_price,employee' AS FieldsCsv,
                   NULL::uuid AS TreasuryId, NULL::uuid AS AccountId,
                   COALESCE(l.employee_id, b.assigned_employee_id) AS EmployeeId,
                   b.status AS DocStatus
            FROM bookings b JOIN booking_lines l ON l.booking_id = b.id
            JOIN customers c ON c.id = b.customer_id
            JOIN items i ON i.id = l.item_id
            LEFT JOIN employees e ON e.id = COALESCE(l.employee_id, b.assigned_employee_id)
            WHERE b.branch_id = @branchId AND b.status IN ('draft', 'confirmed')
              AND b.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR b.created_at <= @until::timestamptz)
              AND l.delivered_qty = 0
            """, args, cancellationToken: ct)));

        rows.AddRange(await connection.QueryAsync<ShiftEditableLine>(new CommandDefinition("""
            SELECT 'service_transfer' AS EntityType, st.id AS DocId, st.doc_no AS DocNo,
                   st.doc_date AS DocDate, l.id AS LineId,
                   (fc.name_ar || ' → ' || tc.name_ar) AS PartyName, i.name_ar AS ItemName,
                   NULL::text AS EmployeeName, NULL::text AS TreasuryName,
                   l.quantity AS Quantity, l.unit_price AS UnitPrice, l.amount AS Amount,
                   NULL::text AS Description, 'quantity' AS FieldsCsv,
                   NULL::uuid AS TreasuryId, NULL::uuid AS AccountId, NULL::uuid AS EmployeeId,
                   st.status AS DocStatus
            FROM service_transfers st JOIN service_transfer_lines l ON l.transfer_id = st.id
            JOIN items i ON i.id = l.item_id
            JOIN customers fc ON fc.id = st.from_customer_id
            JOIN customers tc ON tc.id = st.to_customer_id
            WHERE st.branch_id = @branchId AND st.status = 'draft' AND st.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR st.created_at <= @until::timestamptz)
            """, args, cancellationToken: ct)));

        return rows;
    }
}
