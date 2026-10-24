using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>المواعيد وإذون التنفيذ: صلاحيات + تحقق + تدقيق (4D).</summary>
public sealed class SalesFulfillmentService
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IAppointmentRepository _appointments;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public SalesFulfillmentService(IDeliveryRepository deliveries,
        IAppointmentRepository appointments, ISettingsService settings,
        PermissionService permissions, AuditService audit)
    {
        _deliveries = deliveries; _appointments = appointments; _settings = settings;
        _permissions = permissions; _audit = audit;
    }

    public async Task<IReadOnlyList<DeliveryNote>> ListDeliveriesAsync(ActorContext actor,
        Guid? branchId, string? status, int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesDeliveries, ActionCodes.View,
            ct: ct);
        return await _deliveries.ListAsync(branchId, status, limit, ct);
    }

    public async Task<DeliveryNote> GetDeliveryAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesDeliveries, ActionCodes.View,
            ct: ct);
        return await _deliveries.GetAsync(id, ct) ?? throw new NotFoundAppException();
    }

    /// <summary>تجهيز إذن التنفيذ (مسودة) من بنود الحجز.</summary>
    public async Task<DeliveryNote> CreateDeliveryAsync(ActorContext actor, DeliveryInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesDeliveries, ActionCodes.Execute,
            ct: ct);
        var id = Guid.CreateVersion7();
        try { await _deliveries.CreateDraftAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var note = await _deliveries.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "delivery_notes", id.ToString(), "INSERT", null, note, ct);
        return note;
    }

    /// <summary>ترحيل التنفيذ: مخزون FEFO + تكلفة البضاعة المباعة + كميات الحجز.</summary>
    public async Task<DeliveryNote> PostDeliveryAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _deliveries.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenCodes.SalesDeliveries, ActionCodes.Post,
            ct: ct);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.document_not_draft");

        var settings = await _settings.GetAsync(ct);
        DeliveryNote note;
        var autoInvoice = settings.AutoInvoiceOnDelivery && settings.InvoiceGroupingMode == 0;
        try
        {
            note = await _deliveries.PostAsync(id, actor.UserId, settings.AllowNegativeStock,
                autoInvoice, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "delivery_notes", id.ToString(), "UPDATE",
            new { status = DeliveryStatuses.Draft },
            new { note.Status, note.DocNo, note.CostTotal }, ct);
        return note;
    }

    public async Task<IReadOnlyList<DeliveryNote>> ListByBookingAsync(ActorContext actor,
        Guid bookingId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesDeliveries, ActionCodes.View,
            ct: ct);
        return await _deliveries.ListByBookingAsync(bookingId, ct);
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
