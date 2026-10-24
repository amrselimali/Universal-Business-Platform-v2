using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class PartnerService
{
    private readonly IPartnerRepository _repository;
    private readonly CustomFieldService _customFields;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public PartnerService(IPartnerRepository repository, CustomFieldService customFields,
        PermissionService permissions, AuditService audit)
    {
        _repository = repository; _customFields = customFields;
        _permissions = permissions; _audit = audit;
    }

    private static string Screen(PartnerKind kind) =>
        kind == PartnerKind.Customer ? ScreenCodes.Customers : ScreenCodes.Suppliers;

    private static string Table(PartnerKind kind) => kind == PartnerKind.Customer ? "customers" : "suppliers";

    public async Task<PagedResult<Partner>> ListAsync(ActorContext actor, PartnerKind kind,
        PageRequest request, Guid? categoryId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.View, ct: ct);
        return await _repository.ListAsync(kind, request, categoryId, actor.IsSuperAdmin,
            actor.AllowedBranchIds, ct);
    }

    public async Task<IReadOnlyList<PartnerCategory>> ListCategoriesAsync(ActorContext actor,
        PartnerKind kind, bool includeInactive, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.View, ct: ct);
        return await _repository.ListCategoriesAsync(kind, includeInactive, ct);
    }

    public async Task<Partner> CreateAsync(ActorContext actor, PartnerKind kind, PartnerInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.Add, ct: ct);
        await NormalizeAndValidateAsync(actor, kind, input, ct);
        try
        {
            var created = await _repository.CreateAsync(kind, input, ct);
            await _audit.WriteAsync(actor, Table(kind), created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Partner> UpdateAsync(ActorContext actor, PartnerKind kind, Guid id, int version,
        PartnerInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.Edit, ct: ct);
        var old = await _repository.GetAsync(kind, id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, old);
        await NormalizeAndValidateAsync(actor, kind, input, ct);

        try
        {
            var updated = await _repository.UpdateAsync(kind, id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, Table(kind), id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, PartnerKind kind, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(kind, id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, old);
        try
        {
            if (!await _repository.DeleteAsync(kind, id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, Table(kind), id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "common.deleted");
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Partner> SetActiveAsync(ActorContext actor, PartnerKind kind, Guid id,
        bool isActive, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.ToggleActive, ct: ct);
        var old = await _repository.GetAsync(kind, id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, old);
        var updated = await _repository.SetActiveAsync(kind, id, isActive, ct)
            ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, Table(kind), id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    public async Task<PartnerCategory> CreateCategoryAsync(ActorContext actor, PartnerKind kind,
        PartnerCategoryInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.Add, ct: ct);
        NormalizeCategory(input);
        try
        {
            var created = await _repository.CreateCategoryAsync(kind, input, ct);
            await _audit.WriteAsync(actor, kind == PartnerKind.Customer ? "customer_categories" : "supplier_categories",
                created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<PartnerCategory> UpdateCategoryAsync(ActorContext actor, PartnerKind kind, Guid id,
        int version, PartnerCategoryInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.Edit, ct: ct);
        NormalizeCategory(input);
        var old = await _repository.GetCategoryAsync(kind, id, ct) ?? throw new NotFoundAppException();
        try
        {
            var updated = await _repository.UpdateCategoryAsync(kind, id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, kind == PartnerKind.Customer ? "customer_categories" : "supplier_categories",
                id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task DeleteCategoryAsync(ActorContext actor, PartnerKind kind, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, Screen(kind), ActionCodes.Delete, ct: ct);
        var old = await _repository.GetCategoryAsync(kind, id, ct) ?? throw new NotFoundAppException();
        if (await _repository.CountCategoryUsageAsync(kind, id, ct) > 0)
            throw new ValidationAppException("validation.category_in_use");
        try
        {
            if (!await _repository.DeleteCategoryAsync(kind, id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, kind == PartnerKind.Customer ? "customer_categories" : "supplier_categories",
                id.ToString(), "DELETE", old, null, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    private async Task NormalizeAndValidateAsync(ActorContext actor, PartnerKind kind, PartnerInput input,
        CancellationToken ct)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        input.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        input.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim().ToLowerInvariant();
        input.Address = string.IsNullOrWhiteSpace(input.Address) ? null : input.Address.Trim();
        input.TaxNumber = string.IsNullOrWhiteSpace(input.TaxNumber) ? null : input.TaxNumber.Trim();
        input.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        input.ScopeType = string.Equals(input.ScopeType, "BRANCH", StringComparison.OrdinalIgnoreCase)
            ? "BRANCH" : "COMPANY";

        if (input.Code.Length == 0 || input.Code.Length > 50)
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length == 0 || input.NameAr.Length > 255)
            throw new ValidationAppException("validation.name_required");
        if (input.CreditLimit < 0)
            throw new ValidationAppException("validation.credit_limit_invalid");
        if (input.Email is { Length: > 255 } || (input.Email is not null && !input.Email.Contains('@')))
            throw new ValidationAppException("validation.email_invalid");

        if (input.ScopeType == "COMPANY")
        {
            input.BranchId = null;
        }
        else
        {
            if (!input.BranchId.HasValue)
                throw new ValidationAppException("validation.branch_required");
            if (!actor.CanAccessBranch(input.BranchId.Value))
                throw new ForbiddenException("error.branch_not_allowed");
        }

        if (input.CategoryId.HasValue)
        {
            var category = await _repository.GetCategoryAsync(kind, input.CategoryId.Value, ct)
                ?? throw new ValidationAppException("validation.category_not_found");
            if (!category.IsActive)
                throw new ValidationAppException("validation.category_inactive");
        }

        var entity = kind == PartnerKind.Customer ? "customers" : "suppliers";
        input.CustomFields = await _customFields.ValidateValuesAsync(entity, input.CustomFields, ct);
    }

    private static void EnsureAccess(ActorContext actor, Partner partner)
    {
        if (partner.ScopeType == "BRANCH" && partner.BranchId.HasValue &&
            !actor.CanAccessBranch(partner.BranchId.Value))
            throw new ForbiddenException("error.branch_not_allowed");
    }

    private static void NormalizeCategory(PartnerCategoryInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        if (input.Code.Length == 0 || input.Code.Length > 30)
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length == 0 || input.NameAr.Length > 255)
            throw new ValidationAppException("validation.name_required");
    }
}
