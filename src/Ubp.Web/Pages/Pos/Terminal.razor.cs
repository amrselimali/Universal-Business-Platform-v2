using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Ubp.Application.Services;
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
        public Guid TreasuryMethodId { get; set; }
        public decimal Amount { get; set; }
    }

    private Guid _branchId;
    private Guid _customerId;
    private string? _customerName;
    private string? _customerPhone;
    private Guid _warehouseId;
    private Guid _treasuryId;
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
    private decimal Paid => _payments.Sum(p => p.Amount);
    private decimal Fees => _payments.Sum(FeeOf);

    private string? WhatsAppUrl
    {
        get
        {
            if (_receipt is null) return null;
            var phone = new string((_customerPhone ?? "").Where(char.IsDigit).ToArray());
            if (phone.Length < 8) return null;

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
        _customers = (await Partners.ListAsync(Actor!, PartnerKind.Customer,
            new PageRequest { Page = 1, PageSize = 25 }, null)).Items;
        OnCustomerChanged(_customers.FirstOrDefault()?.Id ?? Guid.Empty);
        await LoadBranchAsync();
    }

    private async Task LoadBranchAsync()
    {
        _warehouses = await Warehouses.OptionsAsync(Actor!);
        _warehouseId = _warehouses.FirstOrDefault()?.Id ?? Guid.Empty;
        var treasuries = await Treasury.ListTreasuriesAsync(Actor!, _branchId);
        _treasuryId = treasuries.FirstOrDefault()?.Id ?? Guid.Empty;
        _methods = _treasuryId == Guid.Empty ? Array.Empty<TreasuryMethod>()
            : await Treasury.ListMethodsAsync(Actor!, _treasuryId);
        await SearchAsync();
    }

    private async Task SearchAsync() =>
        _products = await Items.OptionsAsync(Actor!, _search, true);

    private static decimal PriceOf(ItemOption product) => product.DefaultPrice;

    private void OnCustomerChanged(Guid customerId)
    {
        _customerId = customerId;
        var customer = _customers.FirstOrDefault(c => c.Id == customerId);
        _customerName = customer?.NameAr;
        _customerPhone = customer?.Phone;
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
            : Math.Round(payment.Amount * method.FeePercent / 100m + method.FeeFixed, 2,
                MidpointRounding.AwayFromZero);
    }

    private void AddPayment()
    {
        var method = _methods.FirstOrDefault();
        if (method is null) return;
        _payments.Add(new PaymentModel
        {
            TreasuryMethodId = method.Id,
            Amount = Math.Max(Total - Paid, 0)
        });
        StateHasChanged();
    }

    private async Task CheckoutAsync() => await RunAsync(async () =>
    {
        if (_customerId == Guid.Empty || _branchId == Guid.Empty || _cart.Count == 0)
        {
            Snackbar.Add(T.T("validation.required_fields"), Severity.Warning);
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
                Lines = _cart.Select(l => new PosLineInput
                {
                    ItemId = l.ItemId, UnitId = l.UnitId, Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice
                }).ToList(),
                Payments = _payments.Where(p => p.Amount > 0).Select(p => new PosPaymentInput
                {
                    TreasuryId = _treasuryId, TreasuryMethodId = p.TreasuryMethodId,
                    Amount = p.Amount
                }).ToList()
            });
            _cart.Clear();
            _payments.Clear();
            await SearchAsync();
        }
        finally { _busy = false; }
    });

    private async Task PrintAsync() => await JS.InvokeVoidAsync("ubpPrint");
}
