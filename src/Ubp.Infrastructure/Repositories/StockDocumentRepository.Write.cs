using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StockDocumentRepository
{
    public async Task<StockDocument> CreateDraftAsync(StockDocumentInput input, Guid userId,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO stock_documents
                (doc_type, doc_date, branch_id, warehouse_id, to_branch_id, to_warehouse_id,
                 notes, created_by, idempotency_key)
            VALUES
                (@DocType, @DocDate, @BranchId, @WarehouseId, @ToBranchId, @ToWarehouseId,
                 @Notes, @userId, @IdempotencyKey)
            ON CONFLICT (branch_id, idempotency_key) WHERE idempotency_key IS NOT NULL
            DO NOTHING
            RETURNING id
            """;
        Guid? id;
        Guid? existing = null;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
            {
                input.DocType, input.DocDate, input.BranchId, input.WarehouseId,
                input.ToBranchId, input.ToWarehouseId, input.Notes, userId,
                IdempotencyKey = Normalize(input.IdempotencyKey)
            }, tx, cancellationToken: ct));

            if (id is null)
            {
                // إعادة محاولة بنفس المفتاح: نعيد المستند القائم بدل إنشاء نسخة ثانية.
                await tx.RollbackAsync(ct);
                existing = await GetByIdempotencyKeyAsync(input.BranchId,
                    Normalize(input.IdempotencyKey)!, ct);
            }
            else
            {
                await InsertLinesAsync(connection, tx, id.Value, input, ct);
                await tx.CommitAsync(ct);
            }
        }

        if (existing is null && id is null)
            throw new ConflictException("error.idempotency_conflict");
        // إغلاق اتصال الكتابة قبل القراءة يمنع استهلاك اتصالين لكل عملية تحت الحمل.
        return (await GetAsync((existing ?? id)!.Value, ct))!;
    }

    public async Task<Guid?> GetByIdempotencyKeyAsync(Guid branchId, string idempotencyKey,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT id FROM stock_documents
            WHERE branch_id = @branchId AND idempotency_key = @idempotencyKey
            """, new { branchId, idempotencyKey }, cancellationToken: ct));
    }

    private static string? Normalize(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    public async Task<StockDocument?> UpdateDraftAsync(Guid id, int version, StockDocumentInput input,
        Guid? userId = null, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE stock_documents SET
                doc_date = @DocDate, branch_id = @BranchId, warehouse_id = @WarehouseId,
                to_branch_id = @ToBranchId, to_warehouse_id = @ToWarehouseId, notes = @Notes,
                updated_by = coalesce(@userId, updated_by)
            WHERE id = @id AND version = @version AND status = 'DRAFT'
            RETURNING id
            """;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
            {
                id, version, input.DocDate, input.BranchId, input.WarehouseId,
                input.ToBranchId, input.ToWarehouseId, input.Notes, userId
            }, tx, cancellationToken: ct));

            if (updated is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM stock_document_lines WHERE document_id = @id", new { id }, tx, cancellationToken: ct));
            await InsertLinesAsync(connection, tx, id, input, ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM stock_documents WHERE id = @id AND status = 'DRAFT'",
            new { id }, cancellationToken: ct)) > 0;
    }

    public async Task CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE stock_documents SET status = 'CANCELLED' WHERE id = @id AND status = 'DRAFT'",
            new { id }, cancellationToken: ct));
    }

    private static string DirectionFor(string docType, string? explicitDirection) => docType switch
    {
        StockDocumentTypes.Receipt or StockDocumentTypes.Opening => "IN",
        StockDocumentTypes.Issue or StockDocumentTypes.Transfer => "OUT",
        _ => string.Equals(explicitDirection, "OUT", StringComparison.OrdinalIgnoreCase) ? "OUT" : "IN"
    };

    private static async Task InsertLinesAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid documentId, StockDocumentInput input, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO stock_document_lines
                (document_id, line_no, direction, item_id, unit_id, conversion_factor,
                 quantity, base_quantity, unit_cost, lot_number, expiry_date, notes)
            VALUES
                (@documentId, @LineNo, @Direction, @ItemId, @UnitId, @ConversionFactor,
                 @Quantity, @BaseQuantity, @UnitCost, @LotNumber, @ExpiryDate, @Notes)
            """;

        short lineNo = 1;
        foreach (var line in input.Lines)
        {
            var factor = line.ConversionFactor <= 0 ? 1 : line.ConversionFactor;
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                documentId, LineNo = lineNo++,
                Direction = DirectionFor(input.DocType, line.Direction),
                line.ItemId, line.UnitId, ConversionFactor = factor,
                line.Quantity, BaseQuantity = Math.Round(line.Quantity * factor, 4),
                line.UnitCost, line.LotNumber, line.ExpiryDate, line.Notes
            }, tx, cancellationToken: ct));
        }
    }
}
