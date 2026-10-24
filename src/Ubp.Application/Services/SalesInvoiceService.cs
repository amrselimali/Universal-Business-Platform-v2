using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>فواتير ومرتجعات المبيعات: ربط الحجز (D3)، الإيراد عند الفاتورة، ومقاصة المقدم (D5).</summary>
public sealed class SalesInvoiceService
{
    private readonly ISalesInvoiceRepository _sales;
    private readonly ICommercialDocumentRepository _documents;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public SalesInvoiceService(ISalesInvoiceRepository sales,
        ICommercialDocumentRepository documents, PermissionService permissions, AuditService audit)
    {
        _sales = sales; _documents = documents; _permissions = permissions; _audit = audit;
    }

    private static string ScreenOf(string docType) =>
        CommercialDocTypes.IsReturn(docType) ? ScreenCodes.SalesReturns : ScreenCodes.SalesInvoices;

    public async Task<PagedResult<CommercialDocument>> ListAsync(ActorContext actor,
        CommercialDocFilter filter, PageRequest request, CancellationToken ct = default)
    {
        var screen = string.IsNullOrWhiteSpace(filter.DocType) ||
                     filter.DocType == CommercialDocTypes.SalesInvoice
            ? ScreenCodes.SalesInvoices : ScreenOf(filter.DocType);
        await _permissions.RequireAsync(actor, screen, ActionCodes.View, ct: ct);
        return await _documents.ListAsync(filter, request, ct);
    }

    public async Task<CommercialDocumentDetail> GetAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var detail = await _documents.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenOf(detail.Header.DocType), ActionCodes.View,
            ct: ct);
        return detail;
    }

    public async Task<CommercialDocumentDetail> CreateAsync(ActorContext actor,
        CommercialDocumentInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenOf(input.DocType), ActionCodes.Create, ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var id = Guid.CreateVersion7();
        try { await _sales.CreateDraftAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var detail = await _documents.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "commercial_documents", id.ToString(), "INSERT", null,
            detail.Header, ct);
        return detail;
    }

    public async Task<CommercialDocument> PostAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _documents.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenOf(existing.DocType), ActionCodes.Post,
            ct: ct);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.document_not_draft");

        try { await _sales.PostAsync(id, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var posted = await _documents.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "commercial_documents", id.ToString(), "UPDATE",
            new { status = CommercialStatuses.Draft },
            new { status = posted.Status, posted.DocNo }, ct);
        return posted;
    }
}
