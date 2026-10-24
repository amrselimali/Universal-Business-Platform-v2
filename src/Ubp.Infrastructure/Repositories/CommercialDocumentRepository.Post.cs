using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CommercialDocumentRepository
{
    private sealed class PostContext
    {
        public Guid Id { get; set; }
        public string DocType { get; set; } = "";
        public DateTime DocDate { get; set; }
        public Guid BranchId { get; set; }
        public Guid? WarehouseId { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal Total { get; set; }
        public Guid? SupplierId { get; set; }
        public bool IsReturn { get; set; }
        public string DocNo { get; set; } = "";
        public Guid StockDocumentId { get; set; }
        public Guid EntryId { get; set; }
        public List<PostLine> Lines { get; set; } = new();
        public Dictionary<int, Guid> Inventory { get; set; } = new();
        public Dictionary<int, decimal> Costs { get; set; } = new();
        public Dictionary<string, Guid> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PostHead
    {
        public Guid Id { get; set; }
        public string DocType { get; set; } = "";
        public DateTime DocDate { get; set; }
        public Guid BranchId { get; set; }
        public Guid? WarehouseId { get; set; }
        public string Status { get; set; } = "";
        public Guid? SupplierId { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class PostLine
    {
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public decimal ConversionFactor { get; set; }
        public decimal Quantity { get; set; }
        public decimal NetAmount { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    /// <summary>ترحيل فاتورة/مرتجع شراء في معاملة واحدة (G8): مخزون داخلي (N7) ثم قيد الأستاذ.</summary>
    public async Task<CommercialDocument> PostAsync(Guid id, Guid userId, bool allowNegative,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var context = await PrepareAsync(connection, tx, id, userId, allowNegative, ct);
        await CreateEntryAsync(connection, tx, context, userId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE commercial_documents SET
                status = 'posted', doc_no = @DocNo, posted_at = now(), posted_by = @userId,
                journal_entry_id = @EntryId, stock_document_id = @StockDocumentId, updated_by = @userId
            WHERE id = @Id
            """, new
        {
            context.DocNo, context.EntryId, context.StockDocumentId, Id = id, userId
        }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }
}
