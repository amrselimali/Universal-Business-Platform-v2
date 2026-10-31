using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IAssetRepository
{
    Task<PagedResult<FixedAsset>> ListAsync(PageRequest request, IReadOnlyCollection<Guid> branchIds,
        string? search, string? status, bool meterOnly, CancellationToken ct = default);
    Task<FixedAsset?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAsync(AssetInput input, Guid userId, CancellationToken ct = default);
    Task<FixedAsset?> UpdateAsync(Guid id, int version, AssetInput input, Guid userId,
        CancellationToken ct = default);
    Task<bool> SetStatusAsync(Guid id, string status, Guid userId, CancellationToken ct = default);
    Task<Guid> AddReadingAsync(Guid assetId, decimal value, decimal delta, string source,
        string? note, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<AssetMeterReading>> ListReadingsAsync(Guid assetId, int take = 50,
        CancellationToken ct = default);
    Task SetMeterValueAsync(Guid assetId, decimal value, Guid userId, CancellationToken ct = default);
    /// <summary>خطط/أوامر الصيانة (8أ).</summary>
    Task<Guid> CreatePlanAsync(Guid assetId, string kind, int? intervalDays, decimal? intervalUnits,
        DateTime? nextDueDate, decimal? nextDueMeter, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<MaintenanceOrder>> ListOrdersAsync(Guid assetId, int take = 50,
        CancellationToken ct = default);
    Task<Guid> CreateOrderAsync(MaintenanceOrderInput input, Guid userId,
        CancellationToken ct = default);
    Task<MaintenanceOrder?> GetOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<bool> CloseOrderAsync(Guid orderId, decimal? resetMeterValue, Guid userId,
        CancellationToken ct = default);
    /// <summary>الإهلاك (8أ): الأصول القابلة للإهلاك في فرع، ومنع تكرار شهر واحد.</summary>
    Task<IReadOnlyList<DepreciableAsset>> ListDepreciableAsync(Guid branchId, DateOnly monthEnd,
        CancellationToken ct = default);
    Task<bool> RunExistsAsync(DateOnly periodMonth, Guid branchId, CancellationToken ct = default);
    Task<Guid> InsertRunAsync(DateOnly periodMonth, Guid branchId, decimal total,
        Guid? journalEntryId, Guid userId, CancellationToken ct = default);
    Task InsertLineAsync(Guid runId, Guid assetId, DateOnly periodMonth, decimal amount,
        decimal accumulatedAfter, CancellationToken ct = default);
    /// <summary>تصنيفات الأصول (8أ).</summary>
    Task<IReadOnlyList<AssetCategory>> ListCategoriesAsync(CancellationToken ct = default);
    Task<Guid> CreateCategoryAsync(AssetCategoryInput input, Guid userId,
        CancellationToken ct = default);
}

/// <summary>
/// الأصول والعدادات (8أ): الفرع NULL = أصل مشترك يظهر لكل الفروع،
/// والمستخدم لا يرى إلا أصول فروعه + المشتركة.
/// </summary>
public sealed partial class AssetRepository : IAssetRepository
{
    internal const string AssetColumns = """
        a.id AS Id, a.code AS Code, a.name_ar AS NameAr, a.category_id AS CategoryId,
        c.name_ar AS CategoryName, a.branch_id AS BranchId, b.name_ar AS BranchName,
        a.purchase_date AS PurchaseDate, a.in_service_date AS InServiceDate,
        a.cost AS Cost, a.salvage_value AS SalvageValue, a.status AS Status,
        a.is_metered AS IsMetered, a.meter_type AS MeterType,
        a.current_meter_value AS CurrentMeterValue,
        a.meter_resets_on_maintenance AS MeterResetsOnMaintenance, a.version AS Version
        """;

    internal const string AssetJoin = """
        fixed_assets a
        JOIN asset_categories c ON c.id = a.category_id
        LEFT JOIN branches b ON b.id = a.branch_id
        """;

    private readonly IDbConnectionFactory _factory;

    public AssetRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<FixedAsset>> ListAsync(PageRequest request,
        IReadOnlyCollection<Guid> branchIds, string? search, string? status, bool meterOnly,
        CancellationToken ct = default)
    {
        const string filter = """
            (@allBranches OR a.branch_id IS NULL OR a.branch_id = ANY(@branchIds))
            AND (@status IS NULL OR a.status = @status)
            AND (NOT @meterOnly OR a.is_metered)
            AND (@search IS NULL OR a.code ILIKE '%' || @search || '%'
                 OR a.name_ar ILIKE '%' || @search || '%')
            """;
        var sql = $"""
            SELECT {AssetColumns} FROM {AssetJoin}
            WHERE {filter}
            ORDER BY a.code
            LIMIT @take OFFSET @offset;
            SELECT count(*)::int FROM {AssetJoin} WHERE {filter};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            branchIds = branchIds.ToArray(),
            allBranches = branchIds.Count == 0,
            status,
            meterOnly,
            search,
            take = request.SafePageSize,
            offset = request.Offset
        }, cancellationToken: ct));

        var items = (await multi.ReadAsync<FixedAsset>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<FixedAsset>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<FixedAsset?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<FixedAsset>(new CommandDefinition(
            $"SELECT {AssetColumns} FROM {AssetJoin} WHERE a.id = @id", new { id },
            cancellationToken: ct));
    }
}
