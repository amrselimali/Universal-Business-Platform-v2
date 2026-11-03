using Microsoft.Extensions.Options;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Services;

namespace Ubp.Web.Startup.Integrations;

/// <summary>
/// إشعار واتساب بالإيصال: يقرأ رقم العميل من بطاقته ويضع الرسالة في صندوق الصادر
/// (message_outbox) مع مفتاح منع تكرار لكل إيصال؛ الإرسال يتولاه المعالج الخلفي.
/// </summary>
public sealed class WhatsAppReceiptNotifier : IReceiptNotifier
{
    private readonly IMessageOutboxRepository _outbox;
    private readonly IPartnerRepository _partners;
    private readonly WhatsAppOptions _options;

    public WhatsAppReceiptNotifier(IMessageOutboxRepository outbox, IPartnerRepository partners,
        IOptions<WhatsAppOptions> options)
    {
        _outbox = outbox; _partners = partners; _options = options.Value;
    }

    public async Task<bool> NotifyAsync(Guid customerId, string docNo, decimal total,
        decimal paid, decimal? remaining, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(docNo)) return false;

        var customer = await _partners.GetAsync(PartnerKind.Customer, customerId, ct);
        var phone = Normalize(customer?.WhatsAppPhone) ?? Normalize(customer?.Phone)
                    ?? Normalize(customer?.Phone2);
        if (phone is null) return false;

        var body = $"إيصال {docNo}\nالإجمالي: {total:N2}\nالمدفوع: {paid:N2}"
                   + (remaining is > 0 ? $"\nالمتبقي: {remaining.Value:N2}" : "");
        try
        {
            await _outbox.EnqueueAsync(new OutboxMessageInput
            {
                Channel = MessageChannels.WhatsApp, Recipient = phone,
                TemplateKey = "pos_receipt", Body = body, DedupeKey = $"receipt:{docNo}"
            }, ct);
            return true;
        }
        catch (ValidationAppException)
        {
            // رقم غير صالح أو جسم رسالة غير مطابق: لا يُفشل تسجيل الإيصال.
            return false;
        }
    }

    private static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        return digits.Length >= 8 ? digits : null;
    }
}
