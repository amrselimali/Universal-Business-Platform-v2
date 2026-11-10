using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Treasury;

/// <summary>
/// عرض/تحرير تقرير الشفت: كل الحفظ فوري عند الكتابة (ملاحظات وقراءات العدادات بلا أزرار حفظ)،
/// وعنوان عمود وحدات العدّاد من إعدادات الشركة، وزر تصدير PDF يفتح صفحة طباعة مستقلة — 0094.
/// </summary>
public partial class ShiftReportView : IDisposable
{
    [Parameter] public ShiftReport Report { get; set; } = new();
    /// <summary>وضع الإغلاق: النقد المعدود والملاحظات والقراءات قابلة للكتابة.</summary>
    [Parameter] public bool Editable { get; set; }
    /// <summary>وضع الطباعة/التصدير: كل الأجزاء مفتوحة لعرض البيانات بالكامل.</summary>
    [Parameter] public bool PrintMode { get; set; }

    [Inject] private TreasuryService Treasury { get; set; } = default!;
    [Inject] private SettingsService Settings { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private bool _saving;
    private string? _meterUnitsLabel;
    private readonly Dictionary<string, CancellationTokenSource> _debounce = new();

    /// <summary>رابط صفحة الطباعة/التصدير (اسم الملف الافتراضي = تاريخ الإقفال ومستخدم الإقفال).</summary>
    private string PrintUrl => $"/treasury/shifts/{Report.Shift.Id}/print";

    /// <summary>عنوان عمود وحدات العدّاد = الاسم المسجل في الإعدادات العامة.</summary>
    private string MeterUnitsLabel => string.IsNullOrWhiteSpace(_meterUnitsLabel)
        ? T.T("treasury.meter_units_default")
        : _meterUnitsLabel!;

    protected override async Task OnInitializedAsync()
    {
        var settings = await Settings.GetAsync();
        _meterUnitsLabel = settings.MeterUnitLabel;
    }

    public void Dispose()
    {
        foreach (var cts in _debounce.Values) cts.Cancel();
        _debounce.Clear();
    }

    // ---------------- حفظ فوري (بلا أزرار حفظ) ----------------

    private void OnNotesChanged(string? value)
    {
        Report.Notes = value;
        if (!Editable) return;
        Debounce("notes", () => Treasury.SaveNotesAsync(
            _actor!, Report.Shift.Id, Report.Notes));
    }

    private void OnMeterChanged(ShiftMeterMovementRow row, decimal value)
    {
        row.Actual = value;
        if (!Editable) return;
        var assetId = row.AssetId;
        Debounce($"meter:{assetId}", () => Treasury.SetMeterReadingAsync(_actor!, assetId, value));
    }

    private ActorContext? _actor;

    /// <summary>تأخير بسيط ثم حفظ تلقائي؛ أي كتابة جديدة تُلغي الحفظ السابق لنفس الحقل.</summary>
    private void Debounce(string key, Func<Task> save)
    {
        if (_debounce.TryGetValue(key, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }
        var cts = new CancellationTokenSource();
        _debounce[key] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700, cts.Token);
                await InvokeAsync(() => SaveNowAsync(cts, save));
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });
    }

    private async Task SaveNowAsync(CancellationTokenSource cts, Func<Task> save)
    {
        if (cts.IsCancellationRequested || !Report.Shift.IsOpen) return;
        _actor ??= await UserContext.RequireAsync();
        _saving = true;
        StateHasChanged();
        try { await save(); }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        catch (Exception) { Snackbar.Add(T.T("error.generic"), Severity.Error); }
        finally
        {
            _saving = false;
            StateHasChanged();
        }
    }

    private string TreasuryTypeText(string? type) => string.IsNullOrWhiteSpace(type)
        ? "—"
        : T.T("treasury.type_" + type);

    private static Color SourceColor(string? source) => source switch
    {
        "خدمة علاجية" => Color.Error,
        "جلسة هدية" => Color.Secondary,
        "خدمات محولة" => Color.Info,
        "تنفيذ خدمة محجوزة" => Color.Warning,
        "فاتورة بيع" => Color.Success,
        "تحصيل/قبض" => Color.Primary,
        _ => Color.Default
    };

    private string StatusText(string status) => T.T(status switch
    {
        "draft" => "accounting.status_draft",
        "posted" => "accounting.status_posted",
        "cancelled" => "accounting.status_cancelled",
        "confirmed" => "sales.status_confirmed",
        "partially_delivered" => "sales.status_partial",
        "completed" => "sales.status_completed",
        "expired" => "sales.status_expired",
        "scheduled" => "sales.appointment_scheduled",
        "in_progress" => "sales.appointment_in_progress",
        "done" => "sales.appointment_done",
        "no_show" => "sales.appointment_no_show",
        _ => "accounting.status_" + status
    });
}
