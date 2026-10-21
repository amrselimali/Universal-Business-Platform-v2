using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class StocktakeService
{
    private readonly IStocktakeRepository _stocktakes;
    private readonly IStockDocumentRepository _docs;
    private readonly IStockPostingRepository _posting;
    private readonly IItemRepository _items;
    private readonly IWarehouseRepository _warehouses;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public StocktakeService(IStocktakeRepository stocktakes, IStockDocumentRepository docs,
        IStockPostingRepository posting, IItemRepository items, IWarehouseRepository warehouses,
        ISettingsService settings, PermissionService permissions, AuditService audit)
    {
        _stocktakes = stocktakes; _docs = docs; _posting = posting; _items = items;
        _warehouses = warehouses; _settings = settings; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Stocktake>> ListAsync(ActorContext actor, PageRequest request,
        string? status, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Stocktake, ActionCodes.View, ct: ct);
        return await _stocktakes.ListAsync(request, status, actor.IsAdmin, actor.AllowedBranchIds, ct);
    }

    public async Task<StocktakeDetail> GetDetailAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Stocktake, ActionCodes.View, ct: ct);
        return await LoadAsync(actor, id, ct);
    }

    public async Task<StocktakeDetail> CreateAsync(ActorContext actor, StocktakeInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Stocktake, ActionCodes.Add, ct: ct);
        if (!actor.CanAccessBranch(input.BranchId))
            throw new ForbiddenException("error.branch_not_allowed");

        var warehouse = await _warehouses.GetAsync(input.WarehouseId, ct)
            ?? throw new ValidationAppException("validation.warehouse_required");
        if (!warehouse.IsActive || !warehouse.BranchIds.Contains(input.BranchId))
            throw new ValidationAppException("validation.warehouse_not_in_branch");

        try
        {
            var code = await _stocktakes.NextCodeAsync(input.BranchId, DateTime.UtcNow.Date, ct);
            var created = await _stocktakes.CreateAsync(input, code, actor.UserId, ct);
            await _audit.WriteAsync(actor, "stocktakes", created.Id.ToString(), "INSERT", null, created, ct);
            return await LoadAsync(actor, created.Id, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<StocktakeDetail> UpdateLinesAsync(ActorContext actor, Guid id,
        IReadOnlyList<StocktakeLineInput> lines, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Stocktake, ActionCodes.Edit, ct: ct);
        var detail = await LoadAsync(actor, id, ct);
        if (detail.Header.Status != "DRAFT")
            throw new ValidationAppException("validation.stocktake_not_draft");

        foreach (var line in lines)
        {
            if (line.CountedQty < 0)
                throw new ValidationAppException("validation.quantity_invalid");
            if (line.UnitCost < 0)
                throw new ValidationAppException("validation.price_invalid");
        }

        await _stocktakes.UpdateLinesAsync(id, lines, ct);
        await _audit.WriteAsync(actor, "stocktake_lines", id.ToString(), "UPDATE", null,
            new { Count = lines.Count }, ct);
        return await LoadAsync(actor, id, ct);
    }

    public async Task DeleteAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Stocktake, ActionCodes.Delete, ct: ct);
        var detail = await LoadAsync(actor, id, ct);
        if (!await _stocktakes.DeleteAsync(id, ct))
            throw new ValidationAppException("validation.stocktake_not_draft");
        await _audit.WriteAsync(actor, "stocktakes", id.ToString(), "DELETE", detail.Header, null, ct);
    }

    public async Task<StocktakeDetail> PostAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Stocktake, ActionCodes.Post, ct: ct);
        var detail = await LoadAsync(actor, id, ct);
        if (detail.Header.Status != "DRAFT")
            throw new ValidationAppException("validation.stocktake_not_draft");

        var differences = detail.Lines.Where(l => l.Difference != 0).ToList();
        if (differences.Count == 0)
            throw new ValidationAppException("validation.stocktake_no_differences");

        var input = new StockDocumentInput
        {
            DocType = StockDocumentTypes.Adjustment,
            DocDate = DateTime.UtcNow.Date,
            BranchId = detail.Header.BranchId,
            WarehouseId = detail.Header.WarehouseId,
            Notes = $"تسوية جرد {detail.Header.Code}"
        };

        foreach (var line in differences)
        {
            var item = await _items.GetAsync(line.ItemId, ct)
                ?? throw new ValidationAppException("validation.item_not_found");
            input.Lines.Add(new StockDocumentLineInput
            {
                ItemId = line.ItemId,
                UnitId = item.BaseUnitId,
                ConversionFactor = 1,
                Quantity = Math.Abs(line.Difference),
                UnitCost = line.UnitCost,
                Direction = line.Difference > 0 ? "IN" : "OUT",
                Notes = $"فرق جرد: {line.Difference}"
            });
        }

        var settings = await _settings.GetAsync(ct);
        try
        {
            var document = await _docs.CreateDraftAsync(input, actor.UserId, ct);
            await _docs.LinkStocktakeAsync(document.Id, id, ct);
            await _posting.PostAsync(document.Id, actor.UserId, settings.AllowNegativeStock, ct);
            await _stocktakes.MarkPostedAsync(id, document.Id, actor.UserId, ct);
            await _audit.WriteAsync(actor, "stocktakes", id.ToString(), "UPDATE",
                new { status = "DRAFT" }, new { status = "POSTED", documentId = document.Id }, ct);
            return await LoadAsync(actor, id, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    private async Task<StocktakeDetail> LoadAsync(ActorContext actor, Guid id, CancellationToken ct)
    {
        var detail = await _stocktakes.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        if (!actor.CanAccessBranch(detail.Header.BranchId))
            throw new ForbiddenException("error.branch_not_allowed");
        return detail;
    }
}
