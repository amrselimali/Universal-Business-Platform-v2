using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class StockDocumentService
{
    public async Task<StockDocumentDetail> PostAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments, ActionCodes.Post, ct: ct);
        var document = await LoadDocumentAsync(actor, id, ct);
        if (document.Status != StockStatuses.Draft)
            throw new ValidationAppException("validation.document_not_draft");

        var settings = await _settings.GetAsync(ct);
        try
        {
            await _posting.PostAsync(id, actor.UserId, settings.AllowNegativeStock, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "stock_documents", id.ToString(), "UPDATE",
            new { status = StockStatuses.Draft },
            new { status = StockStatuses.Posted }, ct);

        if (_accounting is not null)
            await _accounting.TryAutoPostAsync(actor, id, ct);

        return await LoadAsync(actor, id, ct);
    }

    public async Task<StockDocumentDetail> ReverseAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments, ActionCodes.Reverse, ct: ct);
        var document = await LoadDocumentAsync(actor, id, ct);

        var settings = await _settings.GetAsync(ct);
        StockDocument reversal;
        try
        {
            reversal = await _posting.ReverseAsync(id, actor.UserId, settings.AllowNegativeStock, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "stock_documents", reversal.Id.ToString(), "INSERT",
            new { reversalOf = id, originalType = document.DocType }, reversal, ct);

        if (_accounting is not null)
            await _accounting.TryAutoReverseAsync(actor, id, ct);

        return await LoadAsync(actor, reversal.Id, ct);
    }

    public async Task<StockDocumentDetail> ReceiveAsync(ActorContext actor, TransferReceiptInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockDocuments, ActionCodes.Receive, ct: ct);
        var document = await LoadDocumentAsync(actor, input.DocumentId, ct);
        if (document.DocType != StockDocumentTypes.Transfer)
            throw new ValidationAppException("validation.not_a_transfer");
        if (document.ToBranchId.HasValue && !actor.CanAccessBranch(document.ToBranchId.Value))
            throw new ForbiddenException("error.branch_not_allowed");

        var settings = await _settings.GetAsync(ct);
        try
        {
            await _posting.ReceiveTransferAsync(input, actor.UserId, settings.AllowNegativeStock, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "stock_transfer_receipts", input.DocumentId.ToString(), "INSERT",
            null, new { Lines = input.Lines.Count, input.Notes }, ct);
        return await LoadAsync(actor, input.DocumentId, ct);
    }
}
