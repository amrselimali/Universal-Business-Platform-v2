using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class CurrencyService
{
    private readonly ICurrencyRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public CurrencyService(ICurrencyRepository repository, PermissionService permissions,
        AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Currency>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Currencies, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(request, ct);
    }

    public async Task<IReadOnlyList<Currency>> ListAllAsync(CancellationToken ct = default) =>
        await _repository.ListAllAsync(ct);

    public async Task<Currency> CreateAsync(ActorContext actor, CurrencyInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Currencies, ActionCodes.Add, ct: ct);
        Normalize(input);
        Validate(input);
        try
        {
            var created = await _repository.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "currencies", created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Currency> UpdateAsync(ActorContext actor, Guid id, int version,
        CurrencyInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Currencies, ActionCodes.Edit, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        Normalize(input);
        Validate(input);
        if (old.IsBase && !input.IsBase)
            throw new ValidationAppException("validation.base_currency_required");
        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "currencies", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task DeleteAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Currencies, ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.IsBase)
            throw new ValidationAppException("validation.base_currency_required");
        if (!await _repository.DeleteAsync(id, ct))
            throw new ValidationAppException("validation.base_currency_required");
        await _audit.WriteAsync(actor, "currencies", id.ToString(), "DELETE", old, null, ct);
    }

    private static void Normalize(CurrencyInput input)
    {
        input.CurrencyCode = (input.CurrencyCode ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        input.Symbol = string.IsNullOrWhiteSpace(input.Symbol) ? null : input.Symbol.Trim();
    }

    private static void Validate(CurrencyInput input)
    {
        if (input.CurrencyCode.Length != 3 || !input.CurrencyCode.All(char.IsLetter))
            throw new ValidationAppException("validation.currency_code_invalid");
        if (string.IsNullOrWhiteSpace(input.NameAr))
            throw new ValidationAppException("validation.name_required");
        if (input.Decimals is < 0 or > 6)
            throw new ValidationAppException("validation.decimals_invalid");
    }
}
