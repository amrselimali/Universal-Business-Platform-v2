using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class AccountService
{
    public async Task<Account> UpdateAsync(ActorContext actor, Guid id, int version, AccountInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.Edit, ct: ct);
        Normalize(input);
        Validate(input);

        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.IsSystem && (!input.IsActive || input.AccountType != old.AccountType))
            throw new ValidationAppException("validation.system_account");
        if (old.LinesCount > 0 && input.AccountType != old.AccountType)
            throw new ValidationAppException("validation.account_used_type_change");
        if (old.ChildrenCount > 0 && !string.Equals(old.Code, input.Code, StringComparison.Ordinal))
            throw new ValidationAppException("validation.account_has_children");
        if (old.ChildrenCount > 0 && old.IsGroup && !input.IsGroup)
            throw new ValidationAppException("validation.account_has_children");

        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, actor.UserId, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "accounts", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Account> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.ToggleActive, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (!isActive && old.ChildrenCount > 0)
            throw new ValidationAppException("validation.account_has_children");

        var updated = await _repository.SetActiveAsync(id, isActive, actor.UserId, ct)
            ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "accounts", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.Edit, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.IsSystem) throw new ValidationAppException("validation.system_account");
        if (old.ChildrenCount > 0) throw new ValidationAppException("validation.account_has_children");
        if (old.LinesCount > 0) throw new ValidationAppException("validation.account_in_use");

        try
        {
            if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "accounts", id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "common.deleted");
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Account> MoveAsync(ActorContext actor, Guid id, Guid? newParentId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.Move, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();

        if (newParentId.HasValue)
        {
            var parent = await _repository.GetAsync(newParentId.Value, ct)
                ?? throw new NotFoundAppException("validation.parent_not_found");
            if (!parent.IsGroup) throw new ValidationAppException("validation.parent_not_group");
        }

        try
        {
            var moved = await _repository.MoveAsync(id, newParentId, actor.UserId, ct)
                ?? throw new ValidationAppException("validation.move_blocked");
            await _audit.WriteAsync(actor, "accounts", id.ToString(), "UPDATE", old, moved, ct);
            return moved;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    private static void Normalize(AccountInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        input.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
    }

    private static void Validate(AccountInput input)
    {
        if (input.Code.Length is 0 or > 20 ||
            !input.Code.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length is 0 or > 200)
            throw new ValidationAppException("validation.name_required");
        if (!AccountTypes.All.Contains(input.AccountType))
            throw new ValidationAppException("validation.account_type");
        if (input.CostCenterPolicy is < 0 or > 2)
            throw new ValidationAppException("validation.cost_center_policy");
    }
}
