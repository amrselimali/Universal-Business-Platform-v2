using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CommercialDocumentRepository
{
    public async Task<PagedResult<CommercialDocument>> ListAsync(CommercialDocFilter filter,
        PageRequest request, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.DocType)) where.Add("d.doc_type = @docType");
        if (!string.IsNullOrWhiteSpace(filter.Status)) where.Add("d.status = @status");
        if (filter.BranchId.HasValue) where.Add("d.branch_id = @branchId");
        if (filter.SupplierId.HasValue) where.Add("d.supplier_id = @supplierId");
        if (filter.CustomerId.HasValue) where.Add("d.customer_id = @customerId");
        if (filter.From.HasValue) where.Add("d.doc_date >= @from");
        if (filter.To.HasValue) where.Add("d.doc_date <= @to");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("(d.doc_no ILIKE '%' || @search || '%' OR normalize_search(d.party_name) " +
                      "LIKE '%' || normalize_search(@search) || '%')");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["doc_no"] = "d.doc_no", ["doc_date"] = "d.doc_date", ["total"] = "d.total"
            }, "d.doc_date", request.Descending);

        var sql = $"""
            SELECT {HeaderColumns} {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM commercial_documents d {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset,
            docType = filter.DocType, status = filter.Status, branchId = filter.BranchId,
            supplierId = filter.SupplierId, customerId = filter.CustomerId,
            from = filter.From, to = filter.To
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<CommercialDocument>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<CommercialDocument>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<CommercialDocument?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CommercialDocument>(new CommandDefinition(
            $"SELECT {HeaderColumns} WHERE d.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<CommercialDocumentDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            $"SELECT {HeaderColumns} WHERE d.id = @id; SELECT {LineColumns} " +
            "WHERE l.document_id = @id ORDER BY l.line_no;", new { id }, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<CommercialDocument>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<CommercialDocumentLine>()).ToList();
        return new CommercialDocumentDetail { Header = header, Lines = lines };
    }

    public async Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM commercial_documents WHERE id = @id AND status = 'draft'",
            new { id }, cancellationToken: ct)) > 0;
    }

    /// <summary>أعمار الموردين على مستوى الشركة: رصيد كل فاتورة غير مسددة موزّعاً على الفترات.</summary>
    public async Task<IReadOnlyList<SupplierAgingRow>> SupplierAgingAsync(Guid? branchId, DateTime asOf,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<SupplierAgingRow>(new CommandDefinition("""
            SELECT d.supplier_id AS SupplierId, coalesce(s.name_ar, '') AS SupplierName,
                   COALESCE(sum(CASE WHEN @asOf::date - d.due_date <= 0
                                     THEN d.total - d.allocated_total ELSE 0 END), 0) AS Current,
                   COALESCE(sum(CASE WHEN @asOf::date - d.due_date BETWEEN 1 AND 30
                                     THEN d.total - d.allocated_total ELSE 0 END), 0) AS Days30,
                   COALESCE(sum(CASE WHEN @asOf::date - d.due_date BETWEEN 31 AND 60
                                     THEN d.total - d.allocated_total ELSE 0 END), 0) AS Days60,
                   COALESCE(sum(CASE WHEN @asOf::date - d.due_date BETWEEN 61 AND 90
                                     THEN d.total - d.allocated_total ELSE 0 END), 0) AS Days90,
                   COALESCE(sum(CASE WHEN @asOf::date - d.due_date > 90
                                     THEN d.total - d.allocated_total ELSE 0 END), 0) AS Over90
            FROM commercial_documents d
            LEFT JOIN suppliers s ON s.id = d.supplier_id
            WHERE d.doc_type = 'purchase_invoice' AND d.status = 'posted'
              AND (@branchId::uuid IS NULL OR d.branch_id = @branchId::uuid)
            GROUP BY d.supplier_id, s.name_ar
            HAVING sum(d.total - d.allocated_total) <> 0
            ORDER BY s.name_ar
            """, new { branchId, asOf }, cancellationToken: ct));
        return rows.ToList();
    }
}
