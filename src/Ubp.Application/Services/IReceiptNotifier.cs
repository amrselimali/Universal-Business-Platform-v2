using Ubp.Core.Models;

namespace Ubp.Application.Services;

/// <summary>
/// إشعار العميل بإيصال (بريد/واتساب) بعد تسجيله — عقد تطبيقي يُنفَّذ في طبقة الويب
/// (مزوّد واتساب/صندوق الرسائل) حتى تبقى الخدمات مستقلة عن تفاصيل الإرسال.
/// </summary>
public interface IReceiptNotifier
{
    Task NotifyAsync(Guid customerId, string docNo, decimal total, decimal paid,
        decimal? remaining, CancellationToken ct = default);
}

/// <summary>تنفيذ محايد للاختبارات/بيئات بلا مزوّد.</summary>
public sealed class NullReceiptNotifier : IReceiptNotifier
{
    public Task NotifyAsync(Guid customerId, string docNo, decimal total, decimal paid,
        decimal? remaining, CancellationToken ct = default) => Task.CompletedTask;
}
