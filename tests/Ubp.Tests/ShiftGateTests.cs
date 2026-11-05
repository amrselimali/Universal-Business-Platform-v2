using Microsoft.Extensions.Caching.Memory;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

/// <summary>بوابة الوردية: مستخدم بلا وردية مفتوحة لا يسجّل في أي شاشة، ولا يملك الترحيل.</summary>
public sealed class ShiftGateTests
{
    private sealed class EmptyPermissions : IPermissionRepository
    {
        public Task<IReadOnlyList<PermissionEntry>> GetRoleGrantsForUserAsync(Guid userId,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PermissionEntry>>([]);

        public Task<IReadOnlyList<PermissionEntry>> GetUserOverridesAsync(Guid userId,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PermissionEntry>>([]);

        public Task ReplaceUserPermissionsAsync(Guid userId,
            IEnumerable<PermissionEntry> permissions, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CopyUserPermissionsAsync(Guid sourceUserId, Guid targetUserId,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedGate(bool open) : IShiftGate
    {
        public Task<bool> HasOpenShiftAsync(Guid userId, Guid? branchId,
            CancellationToken ct = default) => Task.FromResult(open);
    }

    private static PermissionService Build(bool openShift) =>
        new(new EmptyPermissions(), new MemoryCache(new MemoryCacheOptions()),
            new FixedGate(openShift));

    private static ActorContext ShiftUser() => new()
    {
        UserId = Guid.NewGuid(), Username = "cashier", IsAdmin = true, RequiresShift = true
    };

    [Fact]
    public async Task Shift_user_cannot_register_on_any_screen_without_open_shift()
    {
        var service = Build(openShift: false);
        var actor = ShiftUser();

        await service.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.View);
        await service.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.View);

        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Create));
        Assert.Equal("validation.shift_required", error.Code);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.Create));
    }

    [Fact]
    public async Task Shift_user_registrations_are_never_posted_to_accounts()
    {
        var service = Build(openShift: true);
        var actor = ShiftUser();

        await service.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Create);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Post));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.RequireAsync(actor, ScreenCodes.TreasuryReconciliation, ActionCodes.Reconcile));
    }

    [Fact]
    public async Task Regular_user_is_not_gated_by_shifts()
    {
        var service = Build(openShift: false);
        var actor = new ActorContext { UserId = Guid.NewGuid(), Username = "accountant", IsAdmin = true };

        await service.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Create);
        await service.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Post);
    }
}
