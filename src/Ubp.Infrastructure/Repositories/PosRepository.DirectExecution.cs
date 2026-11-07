using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

/// <summary>
/// الأصناف ذات التنفيذ المباشر في نقطة البيع: تُنفَّذ فور الحفظ (بدون انتظار موعد أو تحصيل)
/// فتصرف المخزون وتُرحّل التكلفة والإيراد والذمم وتُحدِّث كارتة كميات العميل.
/// </summary>
public sealed partial class PosRepository
{
    private sealed class DirectLine
    {
        public Guid BookingLineId { get; set; }
        public Guid ItemId { get; set; }
        public Guid? CategoryId { get; set; }
        public string ItemType { get; set; } = "GOODS";
        public string ExecutionMode { get; set; } = ItemExecutionModes.Reserved;
        public Guid UnitId { get; set; }
        public decimal Quantity { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string OperationType { get; set; } = ServiceOperationTypes.Normal;
        public Guid? EmployeeId { get; set; }
        public Guid? TreatmentAccountId { get; set; }
        public decimal UnitPrice { get; set; }
        public string? ItemName { get; set; }
    }

    /// <summary>ينفّذ سطور الأصناف المباشرة داخل معاملة البيع نفسها.</summary>
    private static async Task ExecuteDirectLinesAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PosCheckoutInput input, Guid bookingId, Guid userId,
        bool allowNegative, CancellationToken ct)
    {
        // تُنفَّذ سطور الأصناف المكوَّدة «تنفيذ مباشر» فقط؛ باقي الأصناف تظل حجزًا
        // ينتظر التنفيذ من شاشة الخدمات المحجوزة (نفس الدورة السابقة).
        // القرار في C# بنفس دالة الكشف المستخدمة عند بناء السطور، حتى لا يتحول صنف مباشر
        // إلى حجز معلّق بسبب اختلاف حالة الأحرف أو مسافة زائدة في القيمة المخزنة.
        var lines = (await connection.QueryAsync<DirectLine>(new CommandDefinition("""
            SELECT l.id AS BookingLineId, l.item_id AS ItemId, i.category_id AS CategoryId,
                   i.item_type AS ItemType, i.execution_mode AS ExecutionMode,
                   l.unit_id AS UnitId, l.quantity AS Quantity,
                   l.net_amount AS NetAmount, l.tax_amount AS TaxAmount,
                   l.total_amount AS TotalAmount, l.operation_type AS OperationType,
                   l.unit_price AS UnitPrice, i.name_ar AS ItemName,
                   COALESCE(b.assigned_employee_id, l.employee_id) AS EmployeeId,
                   COALESCE(resp.treatment_account_id, le.treatment_account_id,
                            (SELECT pd.account_id FROM posting_defaults pd
                             WHERE pd.setting_key = 'employee_advances')) AS TreatmentAccountId
            FROM booking_lines l
            JOIN items i ON i.id = l.item_id
            JOIN bookings b ON b.id = l.booking_id
            LEFT JOIN employees resp ON resp.id = b.assigned_employee_id
            LEFT JOIN employees le ON le.id = l.employee_id
            WHERE l.booking_id = @bookingId
            ORDER BY l.line_no
            """, new { bookingId }, tx, cancellationToken: ct)))
            .Where(l => ItemExecutionModes.IsDirect(l.ExecutionMode))
            .ToList();
        if (lines.Count == 0) return;

        foreach (var line in lines)
            await ExecuteDirectLineAsync(connection, tx, input, bookingId, line, userId,
                allowNegative, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings b
            SET status = CASE WHEN NOT EXISTS (
                    SELECT 1 FROM booking_lines l
                    WHERE l.booking_id = b.id
                      AND (l.quantity - l.delivered_qty - l.cancelled_qty) > 0)
                THEN 'completed' ELSE 'partially_delivered' END,
                updated_by = @userId
            WHERE b.id = @bookingId
            """, new { bookingId, userId }, tx, cancellationToken: ct));
    }

    private static async Task ExecuteDirectLineAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PosCheckoutInput input, Guid bookingId, DirectLine line,
        Guid userId, bool allowNegative, CancellationToken ct)
    {
        var executionId = Guid.CreateVersion7();
        var executionLineId = Guid.CreateVersion7();
        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('EXECUTION'::varchar, @branchId::uuid, @date::date,
                                        'EX'::varchar, 6::smallint)
            """, new { branchId = input.BranchId, date = input.DocDate.Date }, tx,
            cancellationToken: ct));

        Guid? stockDocumentLineId = null;
        var stockWarehouseId = Guid.Empty;
        var cost = 0m;
        if (line.ItemType == "GOODS")
            (stockDocumentLineId, cost, stockWarehouseId) = await IssueStockAsync(connection, tx,
                input, line, docNo, userId, allowNegative, ct);

        var journalEntryId = cost > 0
            ? await PostCostAsync(connection, tx, line.CategoryId, input.BranchId, input.DocDate,
                docNo, cost, executionId, userId, ct)
            : (Guid?)null;
        var revenueEntryId = await PostRevenueAsync(connection, tx, input, line, docNo,
            executionLineId, userId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_executions
                (id, branch_id, doc_no, execution_date, booking_id, status, stock_document_id,
                 journal_entry_id, notes, created_by)
            VALUES (@id, @branchId, @docNo, @date, @bookingId, 'posted',
                    (SELECT document_id FROM stock_document_lines WHERE id = @stockLineId),
                    @journalEntryId, 'تنفيذ مباشر من نقطة البيع', @userId)
            """, new
        {
            id = executionId, branchId = input.BranchId, docNo, date = input.DocDate.Date,
            bookingId, stockLineId = stockDocumentLineId,
            journalEntryId = revenueEntryId ?? journalEntryId, userId
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_execution_lines
                (id, execution_id, booking_line_id, item_id, unit_id, quantity, unit_cost,
                 cost_total, stock_document_line_id, employee_id)
            VALUES (@id, @executionId, @bookingLineId, @itemId, @unitId, @quantity, @unitCost,
                    @cost, @stockLineId, @employeeId)
            """, new
        {
            id = executionLineId, executionId, bookingLineId = line.BookingLineId,
            itemId = line.ItemId, unitId = line.UnitId, quantity = line.Quantity,
            unitCost = line.Quantity == 0 ? 0 : Math.Round(cost / line.Quantity, 6), cost,
            stockLineId = stockDocumentLineId, employeeId = line.EmployeeId
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE booking_lines
            SET delivered_qty = delivered_qty + @quantity,
                reserved_qty = GREATEST(reserved_qty - @quantity, 0),
                warehouse_id = COALESCE(@warehouseId, warehouse_id)
            WHERE id = @id
            """, new
            {
                quantity = line.Quantity, id = line.BookingLineId,
                warehouseId = stockWarehouseId == Guid.Empty ? (Guid?)null : stockWarehouseId
            }, tx, cancellationToken: ct));
    }
}