using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class CustomFieldService
{
    public async Task<PagedResult<CustomFieldDefinition>> ListAsync(ActorContext actor,
        PageRequest request, string? entityName, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CustomFields, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(request, entityName, ct);
    }

    public async Task<IReadOnlyList<CustomFieldDefinition>> ListActiveAsync(string entityName,
        CancellationToken ct = default) =>
        await _repository.ListActiveAsync(entityName, ct);

    public async Task<CustomFieldDefinition?> GetAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CustomFields, ActionCodes.View, ct: ct);
        return await _repository.GetAsync(id, ct);
    }

    public async Task<CustomFieldDefinition> CreateAsync(ActorContext actor, CustomFieldInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CustomFields, ActionCodes.Add, ct: ct);
        await ValidateDefinitionAsync(input, ct);
        try
        {
            var created = await _repository.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "custom_field_definitions", created.Id.ToString(),
                "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<CustomFieldDefinition> UpdateAsync(ActorContext actor, Guid id, int version,
        CustomFieldInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CustomFields, ActionCodes.Edit, ct: ct);
        await ValidateDefinitionAsync(input, ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "custom_field_definitions", id.ToString(),
                "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task DeleteAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CustomFields, ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "custom_field_definitions", id.ToString(), "DELETE", old, null, ct);
    }
}
