using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Pages.Admin;

public partial class Database
{
    [Inject] private DatabaseAdminService DatabaseService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private DatabaseStatus? _status;
    private string? _tableSearch;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.DatabaseAdmin;

    private IEnumerable<TableInfo> FilteredTables =>
        (_status?.Tables ?? Array.Empty<TableInfo>())
        .Where(t => string.IsNullOrWhiteSpace(_tableSearch) ||
                    t.Name.Contains(_tableSearch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(t => t.Name);

    protected override async Task OnLoadAsync()
    {
        if (!Actor!.IsSuperAdmin) return;
        _status = await DatabaseService.GetStatusAsync(Actor);
    }

    private async Task RunMigrationsAsync()
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("database.run_confirm_title"),
            T.T("database.run_confirm"), yesText: T.T("database.run_migrations"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        _busy = true;
        await RunAsync(async () =>
        {
            var outcome = await DatabaseService.RunMigrationsAsync(Actor!);
            if (outcome.Success)
            {
                ShowSuccess("database.migrations_done");
                _status = await DatabaseService.GetStatusAsync(Actor!);
            }
            else
            {
                Snackbar.Add(outcome.Error ?? T.T("error.generic"), Severity.Error);
            }
        });
        _busy = false;
    }

    private async Task OpenTableAsync(TableInfo table)
    {
        var dialog = await DialogService.ShowAsync<TableRowsDialog>(table.Name,
            new DialogParameters { ["Table"] = table.Name },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        await dialog.Result;
        _status = await DatabaseService.GetStatusAsync(Actor!);
    }
}
