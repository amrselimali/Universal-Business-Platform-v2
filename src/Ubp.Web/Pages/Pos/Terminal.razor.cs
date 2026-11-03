using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;
using Ubp.Web.Components.Shared;

namespace Ubp.Web.Pages.Pos;

public partial class Terminal : SecurePageBase
{
    private sealed class CartLine
    {
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public string Name { get; set; } = "";
        public string ItemType { get; set; } = "GOODS";
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal TaxRate { get; set; }
        public bool PriceIncludesTax { get; set; }

        public decimal Gross => Math.Round(Quantity * UnitPrice, 2,
            MidpointRounding.AwayFromZero);
        public decimal Net => PriceIncludesTax && TaxRate > 0
            ? Math.Round(Gross / (1 + TaxRate / 100m), 2, MidpointRounding.AwayFromZero)
            : Gross;
        public decimal Tax => TaxRate <= 0 ? 0m
            : PriceIncludesTax
                ? Math.Round(Gross - Net, 2, MidpointRounding.AwayFromZero)
                : Math.Round(Net * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
        public decimal Total => Math.Round(Net + Tax, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class PaymentModel
    {
        public Guid TreasuryId { get; set; }
        public Guid TreasuryMethodId { get; set; }
        public decimal Total { get; set; }
        public decimal Fee { get; set; }
        public decimal Collected => Math.Max(Total - Fee, 0);
    }

    private Guid _branchId;
    private Guid _customerId;
    private string? _customerName;
    private string? _customerPhone;
    private string? _customerSearch;
    private Guid _warehouseId;
    private Guid _treasuryId;
    private DateTime? _expiryDate = DateTime.Today.AddMonths(6);
    private string? _search;
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private IReadOnlyList<Partner> _customers = Array.Empty<Partner>();
    private IReadOnlyList<Warehouse> _warehouses = Array.Empty<Warehouse>();
    private IReadOnlyList<ItemOption> _products = Array.Empty<ItemOption>();
    private IReadOnlyList<TreasuryMethod> _methods = Array.Empty<TreasuryMethod>();
    private List<CartLine> _cart = new();
    private List<PaymentModel> _payments = new();
    private List<CartLine> _lastLines = new();
    private decimal _lastNet;
    private PosReceipt? _receipt;
    private bool _busy;

    private decimal Net => _cart.Sum(l => l.Net);
    private decimal Tax => _cart.Sum(l => l.Tax);
    private decimal Total => Math.Round(Net + Tax, 2, MidpointRounding.AwayFromZero);
    private decimal Fees => Math.Round(_payments.Sum(p => p.Fee), 2,
        MidpointRounding.AwayFromZero);
    private decimal GrandTotal => Math.Round(Total + Fees, 2, MidpointRounding.AwayFromZero);
    private decimal Paid => Math.Round(_payments.Sum(p => p.Total), 2,
        MidpointRounding.AwayFromZero);
    private decimal Remaining => Math.Max(Math.Round(GrandTotal - Paid, 2,
        MidpointRounding.AwayFromZero), 0);

    private string? WhatsAppUrl
    {
        get
        {
            if (_receipt is null) return null;
            var phone = PhoneNumberFormatter.ToWhatsApp(_customerPhone);
            if (phone is null) return null;

            var text = $"إيصال بيع {_receipt.BookingNo} — الإجمالي {_receipt.Total:N2} — " +
                       $"الضريبة {_receipt.TaxTotal:N2} — المدفوع {_receipt.Paid:N2}";
            return $"https://wa.me/{phone}?text={Uri.EscapeDataString(text)}";
        }
    }

    protected override string ScreenCode => ScreenCodes.PosTerminal;

    protected override async Task OnLoadAsync()
    {
        _branches = await Branches.OptionsAsync(Actor!);
        _branchId = _branches.FirstOrDefault()?.Id ?? Guid.Empty;
        await SearchCustomersAsync();
        await LoadBranchAsync();
    }

    private async Task LoadBranchAsync()
    {
        _warehouses = await Warehouses.OptionsAsync(Actor!);
        if (BranchWarehouses.All(w => w.Id != _warehouseId))
            _warehouseId = BranchWarehouses.FirstOrDefault()?.Id ?? Guid.Empty;
        var treasuries = await Treasury.ListTreasuriesAsync(Actor!, _branchId);
        _treasuryId = treasuries.FirstOrDefault()?.Id ?? Guid.Empty;
        _methods = await Treasury.ListBranchMethodsAsync(Actor!, _branchId);
        await SearchAsync();
    }

    private async Task SearchAsync() =>
        _products = await Items.OptionsAsync(Actor!, _search, true);

    private static decimal PriceOf(ItemOption product) => product.DefaultPrice;

    private async Task SearchCustomersAsync()
    {
        _customers = (await Partners.ListAsync(Actor!, PartnerKind.Customer,
            new PageRequest { Page = 1, PageSize = 25, Search = _customerSearch }, null)).Items;
        if (_customers.All(c => c.Id != _customerId))
            await OnCustomerChanged(_customers.FirstOrDefault()?.Id ?? Guid.Empty);
        StateHasChanged();
    }

    private async Task OnCustomerChanged(Guid customerId)
    {
        _customerId = customerId;
        var customer = _customers.FirstOrDefault(c => c.Id == customerId);
        _customerName = customer?.NameAr;
        _customerPhone = customer?.WhatsAppPhone ?? customer?.Phone ?? customer?.Phone2;

        // تحديث طرق التحصيل المكوَّدة في شاشة الخزائن عند اختيار العميل (بدون إعادة تحميل الصفحة).
        if (_branchId != Guid.Empty)
            _methods = await Treasury.ListBranchMethodsAsync(Actor!, _branchId);
        StateHasChanged();
    }

    private IReadOnlyList<Warehouse> BranchWarehouses => _branchId == Guid.Empty
        ? _warehouses
        : _warehouses.Where(w => w.BranchIds.Count == 0 || w.BranchIds.Contains(_branchId))
            .ToList();

    private Task OnWarehouseChanged(Guid value)
    {
        _warehouseId = value;
        return Task.CompletedTask;
    }

    private async Task OnBranchChanged(Guid value)
    {
        _branchId = value;
        _cart.Clear();
        await LoadBranchAsync();
        StateHasChanged();
    }

    private void AddToCart(ItemOption product)
    {
        var line = _cart.FirstOrDefault(l => l.ItemId == product.Id);
        if (line is null)
        {
            _cart.Add(new CartLine
            {
                ItemId = product.Id, UnitId = product.BaseUnitId, Name = product.NameAr,
                ItemType = product.ItemType, UnitPrice = product.DefaultPrice,
                TaxRate = product.TaxRate, PriceIncludesTax = product.PriceIncludesTax
            });
        }
        else line.Quantity += 1;
        StateHasChanged();
    }

    private decimal FeeOf(PaymentModel payment)
    {
        var method = _methods.FirstOrDefault(m => m.Id == payment.TreasuryMethodId);
        return method is null ? 0m
            : Math.Round(payment.Total * method.FeePercent / 100m + method.FeeFixed, 2,
                MidpointRounding.AwayFromZero);
    }

    private void SetTotal(PaymentModel payment, decimal total)
    {
        payment.Total = Math.Max(total, 0);
        payment.Fee = FeeOf(payment);
        StateHasChanged();
    }

    private void PickMethod(PaymentModel payment, Guid methodId)
    {
        payment.TreasuryMethodId = methodId;
        var method = _methods.FirstOrDefault(m => m.Id == methodId);
        if (method is not null) payment.TreasuryId = method.TreasuryId;
        payment.Fee = FeeOf(payment);
        StateHasChanged();
    }

    private void AddPayment()
    {
        var method = _methods.FirstOrDefault();
        if (method is null) return;
        var remaining = Math.Max(Total - _payments.Sum(p => p.Collected), 0);
        var fee = Math.Round(remaining * method.FeePercent / 100m + method.FeeFixed, 2,
            MidpointRounding.AwayFromZero);
        _payments.Add(new PaymentModel
        {
            TreasuryId = method.TreasuryId, TreasuryMethodId = method.Id,
            Total = Math.Round(remaining + fee, 2, MidpointRounding.AwayFromZero), Fee = fee
        });
        StateHasChanged();
    }

    private async Task CollectBookingsAsync()
    {
        if (_customerId == Guid.Empty || _branchId == Guid.Empty) return;
        if (_methods.Count == 0)
        {
            Snackbar.Add(T.T("pos.no_collection_methods"), Severity.Warning);
            return;
        }

        var dialog = await Dialogs.ShowAsync<CollectBookingsDialog>(T.T("pos.collect_title"),
            new DialogParameters
            {
                ["BranchId"] = _branchId, ["CustomerId"] = _customerId,
                ["CustomerName"] = _customerName, ["Methods"] = _methods
            });
        if ((await dialog.Result) is { Canceled: false }) StateHasChanged();
    }

    private Task CheckoutAsync() => CheckoutCoreAsync(sendWhatsApp: false);

    private Task CheckoutAndSendAsync() => CheckoutCoreAsync(sendWhatsApp: true);

    private async Task CheckoutCoreAsync(bool sendWhatsApp) => await RunAsync(async () =>
    {
        if (_customerId == Guid.Empty || _branchId == Guid.Empty || _cart.Count == 0 ||
            _expiryDate is null)
        {
            Snackbar.Add(T.T("validation.required_fields"), Severity.Warning);
            return;
        }
        if (_payments.Sum(p => p.Collected) > Total)
        {
            Snackbar.Add(T.T("validation.pos_payment_exceeds_total"), Severity.Warning);
            return;
        }

        _busy = true;
        try
        {
            _lastLines = _cart.ToList();
            _lastNet = Net;
            _receipt = await Pos.CheckoutAsync(Actor!, new PosCheckoutInput
            {
                BranchId = _branchId, CustomerId = _customerId, WarehouseId = _warehouseId,
                DocDate = DateTime.Today,
                ExpiresAt = new DateTimeOffset(_expiryDate.Value.Date),
                Lines = _cart.Select(l => new PosLineInput
                {
                    ItemId = l.ItemId, UnitId = l.UnitId, Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice
                }).ToList(),
                Payments = _payments.Where(p => p.Total > 0).Select(p => new PosPaymentInput
                {
                    TreasuryId = p.TreasuryId, TreasuryMethodId = p.TreasuryMethodId,
                    Amount = p.Total
                }).ToList()
            });

            if (sendWhatsApp && _receipt is not null)
            {
                var sent = 0;
                foreach (var voucher in _receipt.Vouchers)
                    if (await Pos.SendReceiptWhatsAppAsync(Actor!, voucher.VoucherId)) sent++;
                Snackbar.Add(T.T(sent > 0 ? "pos.whatsapp_queued" : "pos.whatsapp_unavailable"),
                    sent > 0 ? Severity.Success : Severity.Warning);
            }

            _cart.Clear();
            _payments.Clear();
            await SearchAsync();
        }
        finally { _busy = false; }
    });

    private async Task PrintAsync() => await JS.InvokeVoidAsync("ubpPrint");
}
