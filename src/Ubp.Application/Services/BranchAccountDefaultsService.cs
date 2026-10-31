using System.Text.RegularExpressions;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// حسابات الفرع الافتراضية (قرار 8ج): الشجرة تبقى عامة، ولكل فرع حساباته الافتراضية
/// (خزينة/مخزون/إيراد/دفعات…) تُستخدم قبل الحساب العام عند الترحيل.
/// </summary>
public sealed class BranchAccountDefaultsService
{
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_.]{2,59}$", RegexOptions.Compiled);

    private readonly IBranchAccountDefaultsRepository _repository;
    private readonly IAccountRepository _accounts;
    private readonly PermissionService _permissions;

    public BranchAccountDefaultsService(IBranchAccountDefaultsRepository repository,
        IAccountRepository accounts, PermissionService permissions)
    {
        _repository = repository; _accounts = accounts; _permissions = permissions;
    }

    public async Task<IReadOnlyList<BranchAccountDefault>> ListAsync(ActorContext actor,
        Guid branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.ManageDefaults, ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _repository.ListAsync(branchId, ct);
    }

    public async Task<Guid> SetAsync(ActorContext actor, BranchAccountDefaultInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.ManageDefaults, ct: ct);
        BranchScope.Ensure(actor, input.BranchId);

        var key = (input.Key ?? "").Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(key))
            throw new ValidationAppException("validation.branch_account_key_invalid");

        // يجب أن يكون حساباً قابلاً للترحيل (ليس رأس مجموعة)
        var postable = await _accounts.ListPostableAsync(ct);
        if (postable.All(a => a.Id != input.AccountId))
            throw new ValidationAppException("validation.account_not_postable");

        return await _repository.UpsertAsync(new BranchAccountDefaultInput(input.BranchId, key,
            input.AccountId), actor.UserId, ct);
    }

    public async Task RemoveAsync(ActorContext actor, Guid branchId, string key,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.ManageDefaults, ct: ct);
        BranchScope.Ensure(actor, branchId);
        if (!await _repository.DeleteAsync(branchId, key.Trim().ToLowerInvariant(), ct))
            throw new NotFoundAppException();
    }

    /// <summary>يُستخدم عند الترحيل: حساب الفرع إن وُجد وإلا null (فيسقط على الإعداد العام).</summary>
    public Task<Guid?> ResolveAsync(Guid branchId, string key, CancellationToken ct = default) =>
        _repository.ResolveAsync(branchId, key.Trim().ToLowerInvariant(), ct);
}
