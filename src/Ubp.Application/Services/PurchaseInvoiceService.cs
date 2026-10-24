using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>فواتير ومرتجعات الشراء: صلاحيات + تحقق + ترحيل ذري + تدقيق.</summary>
public sealed partial class PurchaseInvoiceService
{
    private readonly ICommercialDocumentRepository _documents;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public PurchaseInvoiceService(ICommercialDocumentRepository documents,
        ISettingsService settings, PermissionService permissions, AuditService audit)
    {
        _documents = documents; _settings = settings; _permissions = permissions; _audit = audit;
    }

    private static string ScreenOf(string docType) =>
        CommercialDocTypes.IsReturn(docType) ? ScreenCodes.PurchaseReturns : ScreenCodes.PurchaseInvoices;

    public async Task<PagedResult<CommercialDocument>> ListAsync(ActorContext actor,
        CommercialDocFilter filter, PageRequest request, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PurchaseInvoices, ActionCodes.View, ct: ct);
        return await _documents.ListAsync(filter, request, ct);
    }

    public async Task<CommercialDocumentDetail> GetAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PurchaseInvoices, ActionCodes.View, ct: ct);
        return await _documents.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<CommercialDocumentDetail> SaveAsync(ActorContext actor, Guid? id, int version,
        CommercialDocumentInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenOf(input.DocType),
            id is null ? ActionCodes.Create : ActionCodes.Edit, ct: ct);
        Validate(input);

        try
        {
            Guid documentId;
            if (id is null)
            {
                documentId = Guid.CreateVersion7();
                await _documents.CreateDraftAsync(documentId, input, actor.UserId, ct);
            }
            else
            {
                await _documents.UpdateDraftAsync(id.Value, version, input, actor.UserId, ct);
                documentId = id.Value;
            }

            var detail = await _documents.GetDetailAsync(documentId, ct)
                ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "commercial_documents", documentId.ToString(),
                id is null ? "INSERT" : "UPDATE", null, detail.Header, ct);
            return detail;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<CommercialDocument> PostAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _documents.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenOf(existing.DocType), ActionCodes.Post, ct: ct);
        if (!existing.IsDraft) throw new ValidationAppException("validation.document_not_draft");

        var settings = await _settings.GetAsync(ct);
        try
        {
            var posted = await _documents.PostAsync(id, actor.UserId, settings.AllowNegativeStock, ct);
            await _audit.WriteAsync(actor, "commercial_documents", id.ToString(), "UPDATE",
                new { status = CommercialStatuses.Draft }, new { status = posted.Status }, ct);
            return posted;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _documents.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenOf(existing.DocType), ActionCodes.Delete, ct: ct);
        if (!existing.IsDraft) throw new ValidationAppException("validation.document_not_draft");
        if (!await _documents.DeleteDraftAsync(id, ct)) throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "commercial_documents", id.ToString(), "DELETE", existing, null, ct);
        return new MutationOutcome(true, false, "common.deleted");
    }
}
