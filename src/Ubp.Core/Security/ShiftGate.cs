namespace Ubp.Core.Security;

/// <summary>
/// بوابة الوردية: هل يوجد وردية مفتوحة للمستخدم/الفرع؟ تُستخدم لمنع أي تسجيل
/// في الشاشات لمستخدم مربوط بالوردية قبل فتحها (0074).
/// </summary>
public interface IShiftGate
{
    Task<bool> HasOpenShiftAsync(Guid userId, Guid? branchId, CancellationToken ct = default);
}
