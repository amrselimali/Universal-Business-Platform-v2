using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class StockDocumentService
{
    private static readonly string[] EditableTypes =
    {
        StockDocumentTypes.Receipt, StockDocumentTypes.Issue, StockDocumentTypes.Transfer,
        StockDocumentTypes.Adjustment, StockDocumentTypes.Opening
    };

    private readonly IStockDocumentRepository _docs;
    private readonly IStockPostingRepository _posting;
    private readonly IItemRepository _items;
    private readonly IWarehouseRepository _warehouses;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public StockDocumentService(IStockDocumentRepository docs, IStockPostingRepository posting,
        IItemRepository items, IWarehouseRepository warehouses, ISettingsService settings,
        PermissionService permissions, AuditService audit)
    {
        _docs = docs; _posting = posting; _items = items; _warehouses = warehouses;
        _settings = settings; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<StockDocument>> ListAsync(ActorContext actor, PageRequest request,
        StockDocumentFilter filter, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments, ActionCodes.View, ct: ct);
        return await _docs.ListAsync(request, filter, actor.IsAdmin, actor.AllowedBranchIds, ct);
    }

    public async Task<StockDocumentDetail> GetDetailAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments, ActionCodes.View, ct: ct);
        return await LoadAsync(actor, id, ct);
    }

    public async Task<StockDocumentDetail> SaveAsync(ActorContext actor, Guid? id, int version,
        StockDocumentInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments,
            id.HasValue ? ActionCodes.Edit : ActionCodes.Add, ct: ct);
        await ValidateAsync(actor, input, ct);

        try
        {
            var document = id.HasValue
                ? await _docs.UpdateDraftAsync(id.Value, version, input, ct)
                  ?? throw new ConflictException("error.version_conflict")
                : await _docs.CreateDraftAsync(input, actor.UserId, ct);

            await _audit.WriteAsync(actor, "stock_documents", document.Id.ToString(),
                id.HasValue ? "UPDATE" : "INSERT", null, new
                {
                    document.DocType, document.DocDate, document.BranchId, document.WarehouseId,
                    document.ToWarehouseId, Lines = input.Lines.Count
                }, ct);
            return await LoadAsync(actor, document.Id, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task DeleteAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments, ActionCodes.Delete, ct: ct);
        var document = await LoadDocumentAsync(actor, id, ct);
        if (!await _docs.DeleteDraftAsync(id, ct))
            throw new ValidationAppException("validation.document_not_draft");
        await _audit.WriteAsync(actor, "stock_documents", id.ToString(), "DELETE", document, null, ct);
    }

    public async Task<StockDocumentDetail> CancelAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments, ActionCodes.Edit, ct: ct);
        var detail = await LoadAsync(actor, id, ct);
        if (detail.Document.Status != StockStatuses.Draft)
            throw new ValidationAppException("validation.document_not_draft");
        await _docs.CancelAsync(id, ct);
        await _audit.WriteAsync(actor, "stock_documents", id.ToString(), "UPDATE",
            new { status = detail.Document.Status }, new { status = StockStatuses.Cancelled }, ct);
        return await LoadAsync(actor, id, ct);
    }

    internal async Task<StockDocument> LoadDocumentAsync(ActorContext actor, Guid id,
        CancellationToken ct)
    {
        var document = await _docs.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, document);
        return document;
    }

    internal static void EnsureAccess(ActorContext actor, StockDocument document)
    {
        if (!actor.CanAccessBranch(document.BranchId))
            throw new ForbiddenException("error.branch_not_allowed");
        if (document.ToBranchId.HasValue && !actor.CanAccessBranch(document.ToBranchId.Value))
            throw new ForbiddenException("error.branch_not_allowed");
    }

    private async Task<StockDocumentDetail> LoadAsync(ActorContext actor, Guid id, CancellationToken ct)
    {
        var detail = await _docs.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, detail.Document);
        return detail;
    }

    private async Task ValidateAsync(ActorContext actor, StockDocumentInput input, CancellationToken ct)
    {
        input.DocType = (input.DocType ?? "").Trim().ToUpperInvariant();
        if (!EditableTypes.Contains(input.DocType, StringComparer.Ordinal))
            throw new ValidationAppException("validation.document_type_invalid");
        input.DocDate = input.DocDate.Date;
        input.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        if (input.BranchId == Guid.Empty)
            throw new ValidationAppException("validation.branch_required");
        if (!actor.CanAccessBranch(input.BranchId))
            throw new ForbiddenException("error.branch_not_allowed");

        var warehouse = await _warehouses.GetAsync(input.WarehouseId, ct)
            ?? throw new ValidationAppException("validation.warehouse_required");
        if (!warehouse.IsActive)
            throw new ValidationAppException("validation.warehouse_inactive");
        if (!warehouse.BranchIds.Contains(input.BranchId))
            throw new ValidationAppException("validation.warehouse_not_in_branch");

        if (input.DocType == StockDocumentTypes.Transfer)
        {
            if (!input.ToBranchId.HasValue || !input.ToWarehouseId.HasValue)
                throw new ValidationAppException("validation.transfer_destination_required");
            if (input.ToWarehouseId == input.WarehouseId)
                throw new ValidationAppException("validation.transfer_same_warehouse");
            if (!actor.CanAccessBranch(input.ToBranchId.Value))
                throw new ForbiddenException("error.branch_not_allowed");
            var destination = await _warehouses.GetAsync(input.ToWarehouseId.Value, ct)
                ?? throw new ValidationAppException("validation.warehouse_required");
            if (!destination.IsActive || !destination.BranchIds.Contains(input.ToBranchId.Value))
                throw new ValidationAppException("validation.warehouse_not_in_branch");
        }
        else
        {
            input.ToBranchId = null;
            input.ToWarehouseId = null;
        }

        input.Lines = (input.Lines ?? new List<StockDocumentLineInput>())
            .Where(l => l.ItemId != Guid.Empty).ToList();
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        if (input.Lines.Count > 100)
            throw new ValidationAppException("validation.too_many_lines");

        var cache = new Dictionary<Guid, ItemDetail>();
        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.quantity_invalid");
            if (line.UnitCost < 0)
                throw new ValidationAppException("validation.price_invalid");

            if (!cache.TryGetValue(line.ItemId, out var item))
            {
                item = await _items.GetDetailAsync(line.ItemId, ct)
                    ?? throw new ValidationAppException("validation.item_not_found");
                cache[line.ItemId] = item;
            }

            if (!item.Item.IsActive)
                throw new ValidationAppException("validation.item_inactive");
            if (item.Item.ItemType != "GOODS")
                throw new ValidationAppException("validation.service_item_not_stockable");

            var unit = item.Units.FirstOrDefault(u => u.UnitId == line.UnitId && u.IsActive)
                ?? throw new ValidationAppException("validation.item_unit_invalid");
            line.ConversionFactor = unit.ConversionFactor;
            line.Notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim();
            if (input.DocType is StockDocumentTypes.Adjustment)
                line.Direction = string.Equals(line.Direction, "OUT", StringComparison.OrdinalIgnoreCase)
                    ? "OUT" : "IN";
            else
                line.Direction = null;
        }
    }
}
