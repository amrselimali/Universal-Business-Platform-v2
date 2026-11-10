using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;
using TreasuryModel = Ubp.Core.Models.Treasury;

namespace Ubp.Web.Pages.Pos;

public partial class CollectBookingsDialog
{
    private sealed class PayLine
    {
        public Guid TreasuryId { get; set; }
        public decimal Total { get; set; }
        public decimal Fee { get; set; }
        public decimal Collected => Math.Max(Total - Fee, 0);
    }

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid BranchId { get; set; }
    [Parameter] public Guid CustomerId { get; set; }
    [Parameter] public string? CustomerName { get; set; }
    [Parameter] public IReadOnlyList<TreasuryModel> Treasuries { get; set; } =
        Array.Empty<TreasuryModel>();

    private List<BookingCollectionRow> _rows = new();
    private List<PayLine> _payments = new();
    private bool _loading = true;
    private bool _busy;
    private bool _canDraft;
    private bool _canWhatsApp;
    private bool _canComplete;

    private decimal SelectedTotal => Math.Round(_rows.Where(r => r.Selected)
        .Sum(r => Math.Round(r.Amount, 2, MidpointRounding.AwayFromZero)), 2,
        MidpointRounding.AwayFromZero);
    private decimal FeeTotal => Math.Round(_payments.Sum(p => p.Fee), 2,
        MidpointRounding.AwayFromZero);
    private decimal PaidTotal => Math.Round(_payments.Sum(p => p.Total), 2,
        MidpointRounding.AwayFromZero);
    private decimal CollectedTotal => Math.Round(_payments.Sum(p => p.Collected), 2,
        MidpointRounding.AwayFromZero);
    private decimal GrandTotal => Math.Round(CollectedTotal + FeeTotal, 2,
        MidpointRounding.AwayFromZero);
    private decimal RemainingOnServices => Math.Max(Math.Round(SelectedTotal - CollectedTotal, 2,
        MidpointRounding.AwayFromZero), 0);

    /// <summary>مجموع «المسدد سابقًا» للحجوزات المعلَّمة — 0095.</summary>
    private decimal SelectedPaidBefore => Math.Round(_rows.Where(r => r.Selected)
        .Sum(r => r.PaidAmount), 2, MidpointRounding.AwayFromZero);

    /// <summary>اسم الخزينة لعرضه بجانب المبلغ المحصل — 0095.</summary>
    private string TreasuryName(Guid treasuryId) => Treasuries
        .FirstOrDefault(t => t.Id == treasuryId)?.NameAr ?? "—";

    /// <summary>إجمالي القيمة المستحقة = مجموع القيم الأصلية للحجوزات المعلَّمة — 0095.</summary>
    private decimal SelectedBookingTotal => Math.Round(_rows.Where(r => r.Selected)
        .Sum(r => r.TotalAmount), 2, MidpointRounding.AwayFromZero);

    /// <summary>مجموع المتبقي قبل التحصيل للحجوزات المعلَّمة (لكل إيصال في المعاينة) — 0095.</summary>
    private decimal SelectedRemainingBefore => Math.Round(_rows.Where(r => r.Selected)
        .Sum(r => r.RemainingAmount), 2, MidpointRounding.AwayFromZero);

    /// <summary>الرصيد المتبقي بعد التحصيل الحالي (مطابق لإيصال التسجيل) — 0095.</summary>
    private decimal ReceiptRemaining => Math.Max(Math.Round(_rows.Where(r => r.Selected)
        .Sum(r => r.RemainingAmount) - CollectedTotal, 2, MidpointRounding.AwayFromZero), 0);

    private async Task ReloadRowsAsync(ActorContext actor)
    {
        _rows = (await Collections.ListOutstandingAsync(actor, BranchId, CustomerId)).ToList();
        _payments.Clear();
        AddPayment();
        StateHasChanged();
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var actor = await UserContext.GetAsync();
            if (actor is null) { MudDialog.Cancel(); return; }

            // صلاحيات أزرار النافذة: تسجيل / حفظ واتساب / اتمام.
            // كل أزرار التحصيل مشروطة أيضاً بصلاحية «تحصيل الخدمات المحجوزة» (COLLECT) — 0095.
            var canCollect = await Permissions.HasAsync(actor, ScreenCodes.PosTerminal,
                ActionCodes.Collect);
            _canDraft = canCollect && await Permissions.HasAsync(actor, ScreenCodes.PosTerminal,
                ActionCodes.SaveDraft);
            _canWhatsApp = canCollect && await Permissions.HasAsync(actor,
                ScreenCodes.PosTerminal, ActionCodes.SendWhatsApp);
            _canComplete = canCollect && await Permissions.HasAsync(actor,
                ScreenCodes.PosTerminal, ActionCodes.CompleteCollection);

            // بلا شفت مفتوح تُغلق النافذة برسالة واضحة بدلاً من التعليق بلا بيانات.
            if (!await Collections.HasOpenShiftAsync(actor, BranchId))
            {
                Snackbar.Add(T.T("pos.open_shift_first"), Severity.Warning);
                MudDialog.Cancel();
                return;
            }

            _rows = (await Collections.ListOutstandingAsync(actor, BranchId, CustomerId))
                .ToList();
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
            MudDialog.Cancel();
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private void Toggle(BookingCollectionRow row, bool value)
    {
        row.Selected = value;
        row.Amount = value ? row.RemainingAmount : 0m;
        if (value && _payments.Count == 0) AddPayment();
        StateHasChanged();
    }

    private decimal FeeFor(Guid treasuryId, decimal total)
    {
        var treasury = Treasuries.FirstOrDefault(t => t.Id == treasuryId);
        return treasury is null ? 0m
            : Math.Round(total * treasury.ProviderFeePercent / 100m + treasury.ProviderFeeAmount,
                2, MidpointRounding.AwayFromZero);
    }

    private void AddPayment()
    {
        var treasury = Treasuries.FirstOrDefault();
        if (treasury is null) return;
        var remaining = Math.Max(SelectedTotal - _payments.Sum(p => p.Collected), 0);
        var fee = FeeFor(treasury.Id, remaining);
        _payments.Add(new PayLine
        {
            TreasuryId = treasury.Id,
            Total = Math.Round(remaining + fee, 2, MidpointRounding.AwayFromZero), Fee = fee
        });
        StateHasChanged();
    }

    private void PickTreasury(PayLine line, Guid treasuryId)
    {
        line.TreasuryId = treasuryId;
        line.Fee = FeeFor(treasuryId, line.Total);
        StateHasChanged();
    }

    private void SetTotal(PayLine line, decimal total)
    {
        line.Total = Math.Max(total, 0);
        line.Fee = FeeFor(line.TreasuryId, line.Total);
        StateHasChanged();
    }

    private void Cancel() => MudDialog.Cancel();

    private bool CannotSave => _busy || _loading || SelectedTotal <= 0 || CollectedTotal <= 0 ||
                               CollectedTotal > SelectedTotal;

    private Task DraftAsync() => SaveAsync(draftOnly: true, closeAfter: false);
    private Task CompleteAsync() => SaveAsync(draftOnly: false, closeAfter: true);
    private Task WhatsAppAsync() => SaveAsync(draftOnly: false, closeAfter: true);

    private async Task SaveAsync(bool draftOnly = false, bool closeAfter = true)
    {
        var lines = _rows.Where(r => r.Selected && r.Amount > 0)
            .Select(r => new BookingCollectionLineInput
            { BookingId = r.BookingId, Kind = r.Kind, Amount = r.Amount })
            .ToList();
        var payments = _payments.Where(p => p.Total > 0).Select(p => new PosPaymentInput
        {
            TreasuryId = p.TreasuryId, TreasuryMethodId = Guid.Empty, Amount = p.Total
        }).ToList();
        if (lines.Count == 0 || payments.Count == 0) return;

        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var result = await Collections.CollectAsync(actor, new BookingCollectionInput
            {
                BranchId = BranchId, CustomerId = CustomerId, DocDate = DateTime.Today,
                Lines = lines, Payments = payments, DraftOnly = draftOnly
            });
            Snackbar.Add(T.T(draftOnly ? "common.saved" : "pos.collected") +
                         (result.VoucherNo is null ? "" : $" — {result.VoucherNo}"),
                Severity.Success);
            if (closeAfter) MudDialog.Close(DialogResult.Ok(result));
            else await ReloadRowsAsync(actor);
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
