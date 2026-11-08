namespace Ubp.Core.Security;

public sealed class ActorContext
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = "";
    public string? Name { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsSuperAdmin { get; init; }
    /// <summary>يُمنع كل وصول آخر حتى تُغيَّر كلمة المرور (0036).</summary>
    public bool MustChangePassword { get; init; }
    /// <summary>مستخدم شفت: لا تسجيل في أي شاشة إلا بشفت مفتوحة (0074).</summary>
    public bool RequiresShift { get; init; }
    public string Language { get; init; } = "ar";
    public Guid? CurrentBranchId { get; init; }
    /// <summary>الفروع المختارة في الجلسة (null = كل فروع المستخدم) — 0092.</summary>
    public Guid[]? FilterBranchIds { get; init; }
    /// <summary>كل الفروع المملوكة للمستخدم (استحقاق كامل) — للكتابة/الصلاحيات، 0093.</summary>
    public IReadOnlyCollection<Guid> AllBranchIds { get; init; } = Array.Empty<Guid>();
    /// <summary>الفروع المصرح بها (نطاق القراءة/الفلتر المعروض).</summary>
    public IReadOnlyCollection<Guid> AllowedBranchIds { get; init; } = Array.Empty<Guid>();
    public string? IpAddress { get; init; }
    public string TraceId { get; init; } = "";

    /// <summary>
    /// استحقاق الوصول للفرع = امتلاكه (كل الفروع)، لا الفلتر المعروض؛ حتى لا تمنع الكتابة
    /// على فرع مملوك لكنه غير محدد في فلتر العرض. (يرجع لـ AllowedBranchIds توافقًا للاختبارات.)
    /// </summary>
    public bool CanAccessBranch(Guid branchId) =>
        IsSuperAdmin || (AllBranchIds.Count > 0 ? AllBranchIds : AllowedBranchIds).Contains(branchId);
    public bool HasAnyBranchAccess(IEnumerable<Guid> branchIds) =>
        IsSuperAdmin || branchIds.Any(CanAccessBranch);
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Username : Name;
}
