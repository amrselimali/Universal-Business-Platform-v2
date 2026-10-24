using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Reports.Accounting;

public partial class Ledger
{
    [Inject] private AccountingReportService Reports { get; set; } = default!;
    [Inject] private AccountService AccountsService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    private List<LedgerRow> _rows = new();
    private LedgerPage? _page;
    private Guid? _accountId;
    private DateTime? _from = new(DateTime.Today.Year, 1, 1);
    private DateTime? _to = DateTime.Today;
    private bool _includeChildren = true;
    private bool _busy;
    private bool IsEnglish => T.Language == "en";

    protected override string ScreenCode => ScreenCodes.LedgerReport;

    protected override async Task OnLoadAsync()
    {
        _accounts = await AccountsService.PostableAsync(Actor!);
        _accountId = _accounts.FirstOrDefault()?.Id;
    }

    private async Task SearchAsync() => await RunAsync(async () =>
    {
        if (_accountId is null) return;
        _busy = true;
        try
        {
            _page = await Reports.LedgerAsync(Actor!, new LedgerRequest
            {
                AccountId = _accountId.Value,
                From = _from ?? new DateTime(DateTime.Today.Year, 1, 1),
                To = _to ?? DateTime.Today,
                IncludeChildren = _includeChildren,
                PageSize = 50
            });
            _rows = _page.Rows.ToList();
        }
        finally { _busy = false; }
    });

    private async Task LoadMoreAsync() => await RunAsync(async () =>
    {
        if (_page?.NextCursor is null || _accountId is null) return;
        _busy = true;
        try
        {
            var next = await Reports.LedgerAsync(Actor!, new LedgerRequest
            {
                AccountId = _accountId.Value,
                From = _page.From,
                To = _page.To,
                IncludeChildren = _includeChildren,
                PageSize = 50,
                Cursor = LedgerCursor.From(_page.NextCursor)
            });
            _rows.AddRange(next.Rows);
            _page.NextCursor = next.NextCursor;
            _page.HasMore = next.HasMore;
        }
        finally { _busy = false; }
    });

    private async Task PrintAsync()
    {
        await JS.InvokeVoidAsync("print");
    }
}
