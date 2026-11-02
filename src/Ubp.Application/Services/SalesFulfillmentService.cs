using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>المواعيد: صلاحيات + تحقق + تدقيق.</summary>
public sealed class SalesFulfillmentService
{
    private readonly IAppointmentRepository _appointments;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public SalesFulfillmentService(IAppointmentRepository appointments,
        PermissionService permissions, AuditService audit)
    {
        _appointments = appointments; _permissions = permissions; _audit = audit;
    }

    public async Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(ActorContext actor,
        Guid? branchId, DateTime date, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesAppointments, ActionCodes.View,
            ct: ct);
        return await _appointments.ListDayAsync(branchId, date, ct);
    }

    public async Task<Appointment> CreateAppointmentAsync(ActorContext actor,
        AppointmentInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesAppointments, ActionCodes.Manage,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var id = Guid.CreateVersion7();
        await _appointments.CreateAsync(id, input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "appointments", id.ToString(), "INSERT", null, input, ct);
        return await _appointments.GetAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<Appointment> SetAppointmentStatusAsync(ActorContext actor, Guid id,
        int version, string status, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesAppointments, ActionCodes.Manage,
            ct: ct);
        var appointment = await _appointments.SetStatusAsync(id, version, status, actor.UserId, ct);
        await _audit.WriteAsync(actor, "appointments", id.ToString(), "UPDATE", null,
            new { status }, ct);
        return appointment;
    }
}
