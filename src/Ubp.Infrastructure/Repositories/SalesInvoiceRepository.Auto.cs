using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesInvoiceRepository
{
    private sealed class DeliveryInvoiceHead
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public DateTime DocDate { get; set; }
        public string Status { get; set; } = DeliveryStatuses.Draft;
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid WarehouseId { get; set; }
    }

    private sealed class DeliveryInvoiceLine
    {
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public Guid BookingLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public Guid? TaxId { get; set; }
        public decimal TaxRate { get; set; }
    }

    /// <summary>
    /// فاتورة تلقائية عند التنفيذ (D4) داخل معاملة الإذن نفسها: تُنشأ مسودة ثم تُرحَّل
    /// بنفس محرك فواتير المبيعات، وتُربط بالإذن عبر delivery_notes.invoice_id.
    /// </summary>
    internal static async Task<Guid?> CreateFromDeliveryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid deliveryId, Guid userId, CancellationToken ct)
    {
        var head = await connection.QuerySingleOrDefaultAsync<DeliveryInvoiceHead>(
            new CommandDefinition("""
            SELECT d.id AS Id, d.branch_id AS BranchId, d.doc_date AS DocDate, d.status AS Status,
                   d.booking_id AS BookingId, b.customer_id AS CustomerId,
                   d.warehouse_id AS WarehouseId
            FROM delivery_notes d JOIN bookings b ON b.id = d.booking_id
            WHERE d.id = @deliveryId AND d.invoice_id IS NULL
            """, new { deliveryId }, tx, cancellationToken: ct));
        if (head is null || head.Status != DeliveryStatuses.Posted) return null;

        var lines = (await connection.QueryAsync<DeliveryInvoiceLine>(new CommandDefinition("""
            SELECT l.line_no AS LineNo, l.item_id AS ItemId, l.unit_id AS UnitId,
                   l.booking_line_id AS BookingLineId, l.quantity AS Quantity,
                   b.unit_price AS UnitPrice, b.discount_percent AS DiscountPercent,
                   b.tax_id AS TaxId, COALESCE(t.rate, 0) AS TaxRate
            FROM delivery_note_lines l
            JOIN booking_lines b ON b.id = l.booking_line_id
            LEFT JOIN taxes t ON t.id = b.tax_id
            WHERE l.delivery_note_id = @deliveryId ORDER BY l.line_no
            """, new { deliveryId }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0) return null;

        decimal subtotal = 0, taxTotal = 0, total = 0;
        var built = new List<(DeliveryInvoiceLine Line, decimal Net, decimal Tax, decimal Total,
            decimal Discount)>();
        foreach (var line in lines)
        {
            var gross = line.Quantity * line.UnitPrice;
            var discount = Math.Round(gross * line.DiscountPercent / 100m, 2,
                MidpointRounding.AwayFromZero);
            var net = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
            var tax = Math.Round(net * line.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            var lineTotal = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero);
            built.Add((line, net, tax, lineTotal, discount));
            subtotal += net;
            taxTotal += tax;
            total += lineTotal;
        }

        var invoiceId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO commercial_documents (id, branch_id, warehouse_id, doc_date, doc_type,
                customer_id, booking_id, notes, subtotal, discount_total, tax_total, total,
                created_by)
            VALUES (@invoiceId, @BranchId, @WarehouseId, @DocDate, 'sales_invoice', @CustomerId,
                    @BookingId, @notes, @subtotal, @discount, @tax, @total, @userId)
            """, new
        {
            invoiceId, head.BranchId, head.WarehouseId, docDate = head.DocDate, head.CustomerId,
            head.BookingId, notes = "فاتورة تلقائية من إذن التنفيذ", subtotal,
            discount = built.Sum(x => x.Discount), tax = taxTotal, total, userId
        }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var item in built)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO commercial_document_lines (document_id, line_no, item_id, unit_id,
                    conversion_factor, quantity, unit_price, discount_percent, discount_amount,
                    net_amount, tax_id, tax_rate, tax_amount, total_amount, booking_line_id)
                VALUES (@invoiceId, @lineNo, @ItemId, @UnitId, 1, @Quantity, @UnitPrice,
                        @DiscountPercent, @discount, @net, @TaxId, @TaxRate, @tax,
                        @total, @BookingLineId)
                """, new
            {
                invoiceId, lineNo, item.Line.ItemId, item.Line.UnitId, item.Line.Quantity,
                item.Line.UnitPrice, item.Line.DiscountPercent, discount = item.Discount,
                net = item.Net, item.Line.TaxId, item.Line.TaxRate, tax = item.Tax,
                total = item.Total, item.Line.BookingLineId
            }, tx, cancellationToken: ct));
        }

        var postHead = new PostHead
        {
            Id = invoiceId, DocType = CommercialDocTypes.SalesInvoice, BranchId = head.BranchId,
            DocDate = head.DocDate, Status = CommercialStatuses.Draft,
            CustomerId = head.CustomerId, BookingId = head.BookingId, Subtotal = subtotal,
            TaxTotal = taxTotal, Total = total
        };
        await PostInvoiceCoreAsync(connection, tx, postHead, userId, ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE delivery_notes SET invoice_id = @invoiceId WHERE id = @deliveryId",
            new { invoiceId, deliveryId }, tx, cancellationToken: ct));
        return invoiceId;
    }
}
