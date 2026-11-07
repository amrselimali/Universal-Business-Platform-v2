using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Expenses;

public partial class OperatingExpenses
{
    [Inject] private BranchService BranchApi { get; set; } = default!;

    private IReadOnlyList<OperatingExpense> _items = Array.Empty<OperatingExpense>();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private Guid? _branchId;
    private DateTime? _from;
    private DateTime? _to;
    private string? _status;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.OperatingExpenses;

    protected override async Task OnLoadAsync()
    {
        _branches = await BranchApi.OptionsAsync(Actor!);
        _branchId = Actor!.CurrentBranchId ?? _branches.FirstOrDefault()?.Id;
        await ReloadAsync();
    }

    private async Task ReloadAsync() =>
        await RunAsync(async () =>
        {
            _items = await Expenses.ListAsync(Actor!, _branchId, null, _from, _to, _status);
        });

    private async Task OnBranchChanged(Guid? branchId)
    {
        _branchId = branchId;
        await ReloadAsync();
    }

    private string BranchLabel(Guid? id) =>
        id is null ? "—" : _branches.FirstOrDefault(b => b.Id == id)?.NameAr ?? "—";

    private async Task OnStatusChanged(string? status)
    {
        _status = status;
        await ReloadAsync();
    }

    private async Task AddAsync()
    {
        var dialog = await Dialogs.ShowAsync<OperatingExpenseDialog>(
            T.T("expenses.new"), new DialogParameters
            {
                ["BranchId"] = _branchId ?? Actor!.CurrentBranchId
            }, new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true });

        if ((await dialog.Result) is { Canceled: false })
        {
            await ReloadAsync();
            ShowSuccess("expenses.saved");
        }
    }

    /// <summary>تسليم نقدية من خزينة إلى حساب وسيط لنقدية الفرع (0080).</summary>
    private async Task HandoverAsync()
    {
        var branchId = _branchId ?? Actor!.CurrentBranchId;
        if (branchId is null) return;
        var dialog = await Dialogs.ShowAsync<CashHandoverDialog>(T.T("expenses.cash_handover"),
            new DialogParameters { ["BranchId"] = branchId.Value });
        if ((await dialog.Result) is { Canceled: false })
        {
            await ReloadAsync();
            ShowSuccess("common.saved");
        }
    }

    /// <summary>صرف سلفة لموظف على حساب مختار من المصروفات التشغيلية (0084).</summary>
    private async Task AdvanceAsync()
    {
        var branchId = _branchId ?? Actor!.CurrentBranchId;
        if (branchId is null) return;
        var dialog = await Dialogs.ShowAsync<EmployeeAdvanceDialog>("صرف سلفة لموظف",
            new DialogParameters { ["BranchId"] = branchId.Value });
        if ((await dialog.Result) is { Canceled: false })
            ShowSuccess("common.saved");
    }

    private async Task PostAsync(OperatingExpense expense) => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            await Expenses.PostAsync(Actor!, expense.Id);
            await ReloadAsync();
            ShowSuccess("expenses.posted");
        }
        finally { _busy = false; }
    });

    private async Task DeleteAsync(OperatingExpense expense) => await RunAsync(async () =>
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            $"{expense.DocNo ?? expense.DocDate.ToString("yyyy-MM-dd")} — {expense.TotalAmount:N2}",
            yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await Expenses.DeleteAsync(Actor!, expense.Id);
        await ReloadAsync();
        ShowSuccess("common.deleted");
    });
}
