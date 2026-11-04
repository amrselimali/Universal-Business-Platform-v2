using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// مصروفات تشغيلية (غير مرتبطة بدورة الحجز): مستند حر السطور — مدين أي حساب من الشجرة،
/// بنود مخزنية اختيارية، وسداد فوري من خزينة/طريقة دفع أو آجل على حساب المورد.
/// </summary>
public sealed class OperatingExpenseService
{
    private readonly IOperatingExpenseRepository _expenses;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public OperatingExpenseService(IOperatingExpenseRepository expenses, ISettingsService settings,
        PermissionService permissions, AuditService audit)
    {
        _expenses = expenses; _settings = settings; _permissions = permissions; _audit = audit;
    }

    public async Task<IReadOnlyList<OperatingExpense>> ListAsync(ActorContext actor,
        Guid? branchId, Guid? supplierId, DateTime? from, DateTime? to, string? status,
        int limit = 200, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.View,
            ct: ct);
        if (branchId is { } id) BranchScope.Ensure(actor, id);

        // المدير العام يرى الكل، وغيره يرى فروعه فقط (القص في الخدمة لا في الواجهة).
        var branchIds = actor.IsSuperAdmin
            ? Array.Empty<Guid>()
            : actor.AllowedBranchIds.Distinct().ToArray();
        try
        {
            return await _expenses.ListAsync(branchId, branchIds, supplierId, from, to, status,
                limit, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<OperatingExpense> GetAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.View,
            ct: ct);
        var expense = await _expenses.GetAsync(id, ct) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, expense.BranchId);
        return expense;
    }

    public async Task<OperatingExpense> CreateAsync(ActorContext actor, OperatingExpenseInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.Create,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);

        var id = Guid.CreateVersion7();
        try { await _expenses.CreateAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var expense = await _expenses.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "operating_expenses", id.ToString(), "INSERT", null,
            expense, ct);
        return expense;
    }

    /// <summary>ترحيل ذري: قيد المصروف على الشجرة + استلام البنود المخزنية + سند السداد إن وُجد.</summary>
    public async Task<OperatingExpense> PostAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _expenses.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.Post,
            ct: ct);
        BranchScope.Ensure(actor, existing.BranchId);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.expense_not_draft");

        var settings = await _settings.GetAsync(ct);
        try { await _expenses.PostAsync(id, actor.UserId, settings.AllowNegativeStock, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var posted = await _expenses.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "operating_expenses", id.ToString(), "UPDATE",
            new { status = existing.Status }, new { status = posted.Status, posted.DocNo }, ct);
        return posted;
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _expenses.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.Delete,
            ct: ct);
        BranchScope.Ensure(actor, existing.BranchId);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.expense_not_draft");

        if (!await _expenses.DeleteAsync(id, ct)) throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "operating_expenses", id.ToString(), "DELETE", existing,
            null, ct);
        return new MutationOutcome(true, false, "common.deleted");
    }
}
