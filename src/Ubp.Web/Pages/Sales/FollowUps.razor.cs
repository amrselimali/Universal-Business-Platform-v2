using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Sales;

public partial class FollowUps
{
    [Inject] private FollowUpService FollowUpApi { get; set; } = default!;
    [Inject] private PartnerService Partners { get; set; } = default!;
    [Inject] private BranchService BranchApi { get; set; } = default!;
    [Inject] private IDisplayFormatter Fmt { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;

    private IReadOnlyList<FollowUp> _items = Array.Empty<FollowUp>();
    private IReadOnlyList<Partner> _customers = Array.Empty<Partner>();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private FollowUpInput _input = new();
    private DateTime? _dueDate = DateTime.Today.AddDays(1);
    private DateTime? _from = DateTime.Today;
    private DateTime? _to = DateTime.Today;
    private string? _status;
    private string? _customerSearch;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.FollowUps;

    protected override async Task OnLoadAsync()
    {
        _customers = (await Partners.ListAsync(Actor!, PartnerKind.Customer,
            new PageRequest { Page = 1, PageSize = 50 }, null)).Items;
        _input.CustomerId = _customers.FirstOrDefault()?.Id ?? Guid.Empty;
        _branches = await BranchApi.OptionsAsync(Actor!);
        await ReloadAsync();
    }

    private async Task ReloadAsync() =>
        await RunAsync(async () =>
            _items = await FollowUpApi.ListAsync(Actor!, _status, _from, _to, _customerSearch));

    private async Task OnStatusChanged(string? status)
    {
        _status = status;
        await ReloadAsync();
    }

    private async Task AddAsync() => await RunAsync(async () =>
    {
        if (_input.CustomerId == Guid.Empty)
        {
            Snackbar.Add(T.T("validation.required_fields"), Severity.Warning);
            return;
        }
        _busy = true;
        try
        {
            _input.DueAt = new DateTimeOffset((_dueDate ?? DateTime.Today).Date.AddHours(10));
            await FollowUpApi.CreateAsync(Actor!, _input);
            _input = new FollowUpInput { CustomerId = _input.CustomerId };
            await ReloadAsync();
            ShowSuccess("common.saved");
        }
        finally { _busy = false; }
    });

    private async Task CompleteAsync(FollowUp followUp) => await RunAsync(async () =>
    {
        await FollowUpApi.CompleteAsync(Actor!, followUp.Id, followUp.Version);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    /// <summary>بحث شامل عن عملاء بلا حجز/موعد/متابعة في فترة (بديل «توليد متابعات متأخرة»).</summary>
    private async Task FindIdleAsync()
    {
        var dialog = await Dialogs.ShowAsync<FindIdleCustomersDialog>(T.T("sales.idle_title"),
            new DialogParameters
            {
                ["Branches"] = _branches,
                ["DefaultBranchId"] = Actor?.CurrentBranchId
            }, new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });

        if ((await dialog.Result) is { Canceled: false }) await ReloadAsync();
    }

    private async Task BookAsync(FollowUp followUp)
    {
        var dialog = await Dialogs.ShowAsync<AppointmentDialog>(T.T("sales.new_appointment"),
            new DialogParameters
            {
                ["Branches"] = _branches,
                ["DefaultBranchId"] = Actor?.CurrentBranchId,
                ["DefaultCustomerId"] = followUp.CustomerId
            });
        if ((await dialog.Result) is { Canceled: false })
        {
            await ReloadAsync();
            ShowSuccess("common.saved");
        }
    }

    private async Task AddFollowUpAsync(FollowUp followUp)
    {
        var dialog = await Dialogs.ShowAsync<QuickFollowUpDialog>(T.T("sales.followup_add"),
            new DialogParameters
            {
                ["CustomerId"] = followUp.CustomerId,
                ["CustomerName"] = followUp.CustomerName,
                ["BookingId"] = followUp.BookingId
            });
        if ((await dialog.Result) is { Canceled: false })
        {
            await ReloadAsync();
            ShowSuccess("common.saved");
        }
    }
}
