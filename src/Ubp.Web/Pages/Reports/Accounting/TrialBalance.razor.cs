using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Reports.Accounting;

public partial class TrialBalance
{
    [Inject] private AccountingReportService Reports { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private TrialBalanceReport? _report;
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private DateTime? _from = new(DateTime.Today.Year, 1, 1);
    private DateTime? _to = DateTime.Today;
    private IReadOnlyList<Guid> _branchIds = Array.Empty<Guid>();
    private bool _busy;
    private bool IsEnglish => T.Language == "en";

    protected override string ScreenCode => ScreenCodes.TrialBalanceReport;

    protected override async Task OnLoadAsync()
    {
        _branches = await BranchService.OptionsAsync(Actor!);
        await SearchAsync();
    }

    /// <summary>نطاق الفروع: لا اختيار = كل فروع المستخدم (يقصّها السيرفر).</summary>
    private async Task OnBranchScope(IReadOnlyList<Guid> branchIds)
    {
        _branchIds = branchIds;
        await SearchAsync();
    }

    private async Task SearchAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            _report = await Reports.TrialBalanceForBranchesAsync(Actor!,
                _from ?? new DateTime(DateTime.Today.Year, 1, 1), _to ?? DateTime.Today, _branchIds);
        }
        finally { _busy = false; }
    });

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");
}
