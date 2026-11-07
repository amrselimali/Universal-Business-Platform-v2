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

    /// <summary>
    /// يضمن ألا يبقى الفلتر فارغًا لغير السوبر أدمن: عند تمرير null (إلغاء الفلتر) يُستخدم الفرع
    /// الحالي أو أول فرع مصرّح به، ويُتحقق دائمًا من ملكية الفرع — 0096.
    /// </summary>
    public static Guid? Effective(ActorContext actor, Guid? branchId)
    {
        if (actor.IsSuperAdmin) return branchId;
        if (branchId is { } id && id != Guid.Empty)
        {
            Ensure(actor, id);
            return id;
        }
        return actor.CurrentBranchId
               ?? (actor.AllowedBranchIds.Count > 0
                   ? actor.AllowedBranchIds.First()
                   : actor.AllBranchIds.FirstOrDefault());
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
