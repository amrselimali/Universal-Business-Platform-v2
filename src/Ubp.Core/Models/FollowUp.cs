namespace Ubp.Core.Models;

public static class FollowUpChannels
{
    public const string Call = "call";
    public const string WhatsApp = "whatsapp";
    public const string Sms = "sms";
    public const string Visit = "visit";

    public static bool IsValid(string? v) => v is Call or WhatsApp or Sms or Visit;
}

public static class FollowUpStatuses
{
    public const string Pending = "pending";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
}

public sealed class FollowUp
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? BookingId { get; set; }
    public string? BookingNo { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string Channel { get; set; } = FollowUpChannels.Call;
    public string? Note { get; set; }
    public string Status { get; set; } = FollowUpStatuses.Pending;
    public DateTimeOffset? CompletedAt { get; set; }
    public int Version { get; set; }

    public bool IsPending => Status == FollowUpStatuses.Pending;
    public bool IsOverdue => IsPending && DueAt < DateTimeOffset.Now;
}

public sealed class FollowUpInput
{
    public Guid CustomerId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.Now.AddDays(1);
    public string Channel { get; set; } = FollowUpChannels.Call;
    public string? Note { get; set; }
}

/// <summary>
/// عميل بلا حجز/موعد/متابعة داخل الفترة المطلوبة، مع آخر نشاط مسجّل له (للعرض المجمّع في خلية واحدة).
/// </summary>
public sealed class CustomerGapRow
{
    public Guid CustomerId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? Phone { get; set; }
    public string? BranchName { get; set; }

    public DateTimeOffset? LastBookingAt { get; set; }
    public string? LastBookingNo { get; set; }
    public string? LastBookingStatus { get; set; }
    public DateTimeOffset? LastAppointmentAt { get; set; }
    public DateTimeOffset? LastFollowUpAt { get; set; }
    public string? LastFollowUpNote { get; set; }
    public string? LastFollowUpChannel { get; set; }
    public string? LastFollowUpStatus { get; set; }

    /// <summary>أحدث نشاط مسجّل (حجز/موعد/متابعة) لعرضه وترتيبه.</summary>
    public DateTimeOffset? LastActivityAt =>
        new[] { LastBookingAt, LastAppointmentAt, LastFollowUpAt }
            .Where(x => x is not null).Max();
}
