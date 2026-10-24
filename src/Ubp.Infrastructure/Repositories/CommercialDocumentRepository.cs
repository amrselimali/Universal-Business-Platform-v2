using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ICommercialDocumentRepository
{
    Task<PagedResult<CommercialDocument>> ListAsync(CommercialDocFilter filter, PageRequest request,
        CancellationToken ct = default);
    Task<CommercialDocumentDetail?> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<CommercialDocument?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateDraftAsync(Guid id, CommercialDocumentInput input, Guid userId,
        CancellationToken ct = default);
    Task<CommercialDocument?> UpdateDraftAsync(Guid id, int version, CommercialDocumentInput input,
        Guid userId, CancellationToken ct = default);
    Task<CommercialDocument> PostAsync(Guid id, Guid userId, bool allowNegative,
        CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierAgingRow>> SupplierAgingAsync(Guid? branchId, DateTime asOf,
        CancellationToken ct = default);
}

public sealed partial class CommercialDocumentRepository : ICommercialDocumentRepository
{
    private const string HeaderColumns = """
        d.id AS Id, d.doc_type AS DocType, d.doc_no AS DocNo, d.doc_date AS DocDate,
        d.status AS Status, d.branch_id AS BranchId, b.name_ar AS BranchName,
        d.warehouse_id AS WarehouseId, w.name_ar AS WarehouseName,
        d.supplier_id AS SupplierId, d.customer_id AS CustomerId, d.party_name AS PartyName,
        d.payment_terms_id AS PaymentTermsId, d.due_date AS DueDate,
        d.subtotal AS Subtotal, d.discount_total AS DiscountTotal, d.tax_total AS TaxTotal,
        d.total AS Total, d.allocated_total AS AllocatedTotal,
        d.journal_entry_id AS JournalEntryId, e.entry_no AS EntryNo,
        d.stock_document_id AS StockDocumentId, d.original_document_id AS OriginalDocumentId,
        d.notes AS Notes, d.posted_at AS PostedAt, u.name AS PostedByName,
        d.reversed_at AS ReversedAt, d.created_at AS CreatedAt, d.version AS Version,
        (SELECT count(*)::int FROM commercial_document_lines x WHERE x.document_id = d.id) AS LinesCount
        FROM commercial_documents d
        JOIN branches b ON b.id = d.branch_id
        LEFT JOIN warehouses w ON w.id = d.warehouse_id
        LEFT JOIN journal_entries e ON e.id = d.journal_entry_id
        LEFT JOIN users u ON u.id = d.posted_by
        """;

    private const string LineColumns = """
        l.id AS Id, l.document_id AS DocumentId, l.line_no AS LineNo, l.item_id AS ItemId,
        i.code AS ItemCode, i.name_ar AS ItemName, l.unit_id AS UnitId, un.name_ar AS UnitName,
        l.conversion_factor AS ConversionFactor, l.quantity AS Quantity,
        l.unit_price AS UnitPrice, l.discount_percent AS DiscountPercent,
        l.discount_amount AS DiscountAmount, l.net_amount AS NetAmount, l.tax_id AS TaxId,
        l.tax_rate AS TaxRate, l.tax_amount AS TaxAmount, l.total_amount AS TotalAmount,
        l.unit_cost AS UnitCost, l.cost_total AS CostTotal, l.lot_number AS LotNumber,
        l.expiry_date AS ExpiryDate, l.returned_qty AS ReturnedQty, l.description AS Description,
        l.booking_line_id AS BookingLineId
        FROM commercial_document_lines l
        JOIN items i ON i.id = l.item_id
        JOIN units un ON un.id = l.unit_id
        """;

    private readonly IDbConnectionFactory _factory;

    public CommercialDocumentRepository(IDbConnectionFactory factory) => _factory = factory;
}
