namespace Ubp.Core.Security;

/// <summary>
/// بوابة الشفت: هل يوجد شفت مفتوحة للمستخدم أو في أحد فروعه المصرح بها؟
/// تُستخدم لمنع التسجيل في الشاشات لمستخدم مربوط بالشفت قبل فتحها (0074).
/// </summary>
public interface IShiftGate
{
    Task<bool> HasOpenShiftAsync(Guid userId, Guid? branchId,
        IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default);
}

