using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>تنفيذ حجز مباشرة من شاشة الحجوزات مع تسجيل وحدات عدّاد الأصل.</summary>
public sealed class BookingExecutionService
{
    private readonly IBookingExecutionRepository _executions;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public BookingExecutionService(IBookingExecutionRepository executions,
        ISettingsService settings, PermissionService permissions, AuditService audit)
    {
        _executions = executions; _settings = settings; _permissions = permissions;
        _audit = audit;
    }

    public async Task<BookingExecutionPreview> GetPreviewAsync(ActorContext actor,
        Guid bookingLineId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Reservations, ActionCodes.Execute, ct: ct);
        var preview = await _executions.GetPreviewAsync(bookingLineId, ct);
        BranchScope.Ensure(actor, preview.BranchId);
        var settings = await _settings.GetAsync(ct);
        preview.UnitsColumnLabel = settings.MeterUnitLabel;
        return preview;
    }

    public async Task<BookingExecutionResult> ExecuteAsync(ActorContext actor,
        BookingExecutionInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Reservations, ActionCodes.Execute, ct: ct);
        var preview = await _executions.GetPreviewAsync(input.BookingLineId, ct);
        BranchScope.Ensure(actor, preview.BranchId);
        if (preview.ItemType == "SERVICE" && string.IsNullOrWhiteSpace(preview.EmployeeName))
            throw new ValidationAppException("validation.execution_employee_required");
        if (input.Quantity <= 0 || input.Quantity > preview.RemainingQty)
            throw new ValidationAppException("validation.booking_quantity_exceeds_remaining");

        var settings = await _settings.GetAsync(ct);
        input.UnitsColumnLabel = settings.MeterUnitLabel;
        try
        {
            var result = await _executions.ExecuteAsync(input, actor.UserId,
                settings.AllowNegativeStock, ct);
            await _audit.WriteAsync(actor, "booking_executions", result.ExecutionId.ToString(),
                "INSERT", null, result, ct);
            return result;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
