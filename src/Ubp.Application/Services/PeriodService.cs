using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class PeriodService
{
    private readonly IPeriodRepository _repository;
    private readonly IBranchRepository _branches;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public PeriodService(IPeriodRepository repository, IBranchRepository branches,
        PermissionService permissions, AuditService audit)
    {
        _repository = repository; _branches = branches;
        _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<FiscalPeriod>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(request, ct);
    }

    public async Task<IReadOnlyList<FiscalPeriod>> OptionsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.View, ct: ct);
        return await _repository.ListOptionsAsync(ct);
    }

    public async Task<PeriodDetail> GetDetailAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.View, ct: ct);
        var period = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        return new PeriodDetail
        {
            Period = period,
            Closures = await _repository.ListClosuresAsync(id, ct),
            Checks = await _repository.ListChecksAsync(id, ct),
            Events = await _repository.ListEventsAsync(id, ct)
        };
    }

    public async Task<FiscalPeriod> CreateAsync(ActorContext actor, FiscalPeriodInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.Add, ct: ct);
        Normalize(input);
        await ValidateAsync(input, null, ct);
        try
        {
            var created = await _repository.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "fiscal_periods", created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<FiscalPeriod> UpdateAsync(ActorContext actor, Guid id, int version,
        FiscalPeriodInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.Edit, ct: ct);
        Normalize(input);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.Status != "OPEN")
            throw new ValidationAppException("validation.period_closed_edit");
        await ValidateAsync(input, id, ct);

        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "fiscal_periods", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task DeleteAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.Status == "CLOSED" || await _repository.CountClosuresAsync(id, ct) > 0)
            throw new ValidationAppException("validation.period_in_use");
        if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "fiscal_periods", id.ToString(), "DELETE", old, null, ct);
    }

    private async Task ValidateAsync(FiscalPeriodInput input, Guid? currentId, CancellationToken ct)
    {
        if (input.Code.Length == 0 || input.Code.Length > 30)
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length == 0)
            throw new ValidationAppException("validation.name_required");
        if (input.EndDate.Date < input.StartDate.Date)
            throw new ValidationAppException("validation.period_range_invalid");
        if (input.EndDate.Date.Subtract(input.StartDate.Date).TotalDays > 366 * 2)
            throw new ValidationAppException("validation.period_too_long");

        if (await _repository.CountOverlappingAsync(input.StartDate.Date, input.EndDate.Date, currentId, ct) > 0)
            throw new ValidationAppException("validation.period_overlap");

        if (currentId.HasValue)
        {
            var period = await _repository.GetAsync(currentId.Value, ct);
            if (period is not null && period.Status == "OPEN"
                && await _repository.CountDraftDocumentsAsync(period.StartDate, period.EndDate, null, ct) > 0
                && input.StartDate.Date > period.StartDate.Date)
                throw new ValidationAppException("validation.period_has_documents");
        }
    }

    private static void Normalize(FiscalPeriodInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        input.StartDate = input.StartDate.Date;
        input.EndDate = input.EndDate.Date;
    }
}
