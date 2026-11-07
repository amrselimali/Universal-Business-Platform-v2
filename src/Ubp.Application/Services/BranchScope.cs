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
            var scoped = actor.IsSuperAdmin
                ? requested.ToArray()
                : requested.Where(actor.AllowedBranchIds.Contains).Distinct().ToArray();
            // إن لم يتقاطع الطلب مع الفلتر المعروض نستخدم نطاق المستخدم المعروض (لا فراغ).
            if (scoped.Length > 0) return scoped;
        }
        return actor.IsSuperAdmin ? Array.Empty<Guid>() : actor.AllowedBranchIds;
    }
}
