using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class AccountService
{
    private readonly IAccountRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public AccountService(IAccountRepository repository, PermissionService permissions,
        AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Account>> ListAsync(ActorContext actor, AccountFilter filter,
        PageRequest request, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(filter, request, ct);
    }

    public async Task<IReadOnlyList<Account>> TreeAsync(ActorContext actor, bool includeInactive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.View, ct: ct);
        return await _repository.ListAllAsync(includeInactive, ct);
    }

    /// <summary>الحسابات القابلة للترحيل فقط — تُستخدم في قوائم اختيار بنود القيد.</summary>
    public async Task<IReadOnlyList<Account>> PostableAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.Accounts, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.JournalEntry, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.OpeningBalances, ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();
        return await _repository.ListPostableAsync(ct);
    }

    public async Task<Account> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.View, ct: ct);
        return await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<Account> CreateRootAsync(ActorContext actor, AccountInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.CreateRoot, ct: ct);
        input.ParentId = null;
        Normalize(input);
        Validate(input);
        return await CreateCoreAsync(actor, input, $"/{input.Code}/", 1, ct);
    }

    public async Task<Account> CreateChildAsync(ActorContext actor, Guid parentId, AccountInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.CreateChild, ct: ct);
        var parent = await _repository.GetAsync(parentId, ct) ?? throw new NotFoundAppException();
        if (!parent.IsGroup) throw new ValidationAppException("validation.parent_not_group");

        input.ParentId = parentId;
        Normalize(input);
        Validate(input);
        return await CreateCoreAsync(actor, input, parent.Path + input.Code + "/",
            (short)(parent.Level + 1), ct);
    }

    private async Task<Account> CreateCoreAsync(ActorContext actor, AccountInput input, string path,
        short level, CancellationToken ct)
    {
        if (await _repository.GetByCodeAsync(input.Code, ct) is not null)
            throw new ValidationAppException("validation.duplicate_code");

        try
        {
            var id = Guid.CreateVersion7();
            await _repository.CreateAsync(id, input, path, level, actor.UserId, ct);
            var created = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "accounts", id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
