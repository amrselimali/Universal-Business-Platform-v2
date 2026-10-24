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
}
