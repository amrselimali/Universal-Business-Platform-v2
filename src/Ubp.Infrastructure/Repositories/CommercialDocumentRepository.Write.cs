using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CommercialDocumentRepository
{
    public async Task<Guid> CreateDraftAsync(Guid id, CommercialDocumentInput input, Guid userId,
        CancellationToken ct = default) =>
        await SaveDraftAsync(id, null, input, userId, ct);

    public async Task<CommercialDocument?> UpdateDraftAsync(Guid id, int version,
        CommercialDocumentInput input, Guid userId, CancellationToken ct = default)
    {
        var result = await SaveDraftAsync(id, version, input, userId, ct);
        return result == Guid.Empty ? null : await GetAsync(id, ct);
    }

    private async Task<Guid> SaveDraftAsync(Guid id, int? version, CommercialDocumentInput input,
        Guid userId, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var totals = await ComputeTotalsAsync(connection, tx, input.Lines, ct);

        if (version is null)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO commercial_documents
                    (id, branch_id, warehouse_id, doc_date, doc_type, supplier_id, customer_id, payment_terms_id,
                     due_date, original_document_id, notes, subtotal, discount_total, tax_total,
                     total, created_by)
                VALUES (@id, @BranchId, @WarehouseId, @DocDate, @DocType, @SupplierId, @CustomerId, @PaymentTermsId,
                        @DueDate, @OriginalDocumentId, @Notes, @Subtotal, @DiscountTotal, @TaxTotal,
                        @Total, @userId)
                """, new
            {
                id, input.BranchId, input.WarehouseId, input.DocDate, input.DocType, input.SupplierId, input.CustomerId,
                input.PaymentTermsId, input.DueDate, input.OriginalDocumentId, input.Notes,
                totals.Subtotal, totals.DiscountTotal, totals.TaxTotal, totals.Total, userId
            }, tx, cancellationToken: ct));
        }
        else
        {
            var rows = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE commercial_documents SET
                    branch_id = @BranchId, warehouse_id = @WarehouseId, doc_date = @DocDate, supplier_id = @SupplierId,
                    customer_id = @CustomerId, payment_terms_id = @PaymentTermsId,
                    due_date = @DueDate, original_document_id = @OriginalDocumentId,
                    notes = @Notes, subtotal = @Subtotal, discount_total = @DiscountTotal,
                    tax_total = @TaxTotal, total = @Total, updated_by = @userId
                WHERE id = @id AND version = @version AND status = 'draft'
                """, new
            {
                id, version = version.Value, input.BranchId, input.WarehouseId, input.DocDate, input.SupplierId,
                input.CustomerId, input.PaymentTermsId, input.DueDate, input.OriginalDocumentId,
                input.Notes, totals.Subtotal, totals.DiscountTotal, totals.TaxTotal, totals.Total,
                userId
            }, tx, cancellationToken: ct));

            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                return Guid.Empty;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM commercial_document_lines WHERE document_id = @id",
                new { id }, tx, cancellationToken: ct));
        }

        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            lineNo++;
            var computed = totals.Lines[lineNo - 1];
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO commercial_document_lines
                    (id, document_id, line_no, item_id, unit_id, conversion_factor, quantity,
                     unit_price, discount_percent, discount_amount, net_amount, tax_id, tax_rate,
                     tax_amount, total_amount, lot_number, expiry_date, description)
                VALUES (@lineId, @id, @lineNo, @ItemId, @UnitId, @ConversionFactor, @Quantity,
                        @UnitPrice, @DiscountPercent, @DiscountAmount, @NetAmount, @TaxId, @TaxRate,
                        @TaxAmount, @TotalAmount, @LotNumber, @ExpiryDate, @Description)
                """, new
            {
                lineId = Guid.CreateVersion7(), id, lineNo, line.ItemId, line.UnitId,
                line.ConversionFactor, line.Quantity, line.UnitPrice, line.DiscountPercent,
                computed.DiscountAmount, computed.NetAmount, line.TaxId, computed.TaxRate,
                computed.TaxAmount, computed.TotalAmount, line.LotNumber, line.ExpiryDate,
                line.Description
            }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return id;
    }
}
