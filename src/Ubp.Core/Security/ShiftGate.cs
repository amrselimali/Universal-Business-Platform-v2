namespace Ubp.Core.Security;

/// <summary>
/// بوابة الوردية: هل يوجد وردية مفتوحة للمستخدم أو في أحد فروعه المصرح بها؟
/// تُستخدم لمنع التسجيل في الشاشات لمستخدم مربوط بالوردية قبل فتحها (0074).
/// </summary>
public interface IShiftGate
{
    Task<bool> HasOpenShiftAsync(Guid userId, Guid? branchId,
        IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default);
}

