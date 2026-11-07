using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

/// <summary>
/// حماية نطاق الفرع على الخادم: أي مستند/حركة تُحفظ يجب أن يكون فرعها ضمن فروع المستخدم،
/// إلا للمدير الأعلى (IsSuperAdmin). الواجهة تُخفي الخيارات، وهذه الحماية تمنع تجاوزها.
/// </summary>
public static class BranchScope
{
    public static void Ensure(ActorContext actor, Guid branchId)
    {
        if (branchId != Guid.Empty && !actor.CanAccessBranch(branchId))
            throw new ForbiddenException();
    }

    /// <summary>يتحقق أن كل الفروع المطلوبة ضمن فروع المستخدم (لتحديد أكثر من فرع).</summary>
    public static void EnsureAll(ActorContext actor, IEnumerable<Guid> branchIds)
    {
        foreach (var branchId in branchIds) Ensure(actor, branchId);
    }

    /// <summary>
    /// يحل نطاق الفروع: المُحدَّد من المستخدم مقيّد بفروعه المسموحة؛ وإن لم يحدد شيئًا يُستخدم
    /// كل فروعه المسموحة (والمدير الأعلى بلا قيد). يُعيد قائمة الفروع الفعّالة.
    /// </summary>
    public static IReadOnlyCollection<Guid> Resolve(ActorContext actor, IReadOnlyCollection<Guid>? requested)
    {
        if (requested is { Count: > 0 })
        {
            EnsureAll(actor, requested);
            return actor.IsSuperAdmin
                ? requested
                : requested.Where(actor.AllowedBranchIds.Contains).Distinct().ToArray();
        }
        return actor.IsSuperAdmin ? Array.Empty<Guid>() : actor.AllowedBranchIds;
    }
}
