using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Admin;

public partial class TableRowsDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter, EditorRequired] public string Table { get; set; } = "";
    [Inject] private DatabaseAdminService DatabaseService { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;

    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal)
    {
        "audit_log", "refresh_tokens", "user_sessions", "schemaversions",
        "qa_test_definitions", "qa_test_runs", "qa_test_results", "qa_test_artifacts"
    };

    private RowPage? _pageData;
    private List<string> _rows = new();
    private int _page = 1;
    private bool _loading = true;

    private bool Editable => !ReadOnly.Contains(Table);
    private int TotalPages => _pageData is null ? 1 : Math.Max(1, (int)Math.Ceiling(_pageData.Total / 25.0));

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        var actor = await UserContext.RequireAsync();
        _pageData = await DatabaseService.GetRowsAsync(actor, Table, _page);
        _rows = _pageData.RowsJson.ToList();
        _loading = false;
    }

    private async Task PrevAsync() { if (_page > 1) { _page--; await LoadAsync(); } }
    private async Task NextAsync() { if (_page < TotalPages) { _page++; await LoadAsync(); } }

    private static string RowId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("id", out var id) ? id.ToString() : "row";
        }
        catch { return "row"; }
    }

    private static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    private async Task EditAsync(string row)
    {
        var dialog = await DialogService.ShowAsync<RowActionDialog>(T.T("database.edit_row"),
            new DialogParameters { ["Table"] = Table, ["RowJson"] = row, ["Mode"] = "update" },
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false }) await LoadAsync();
    }

    private async Task DeleteAsync(string row)
    {
        var dialog = await DialogService.ShowAsync<RowActionDialog>(T.T("database.delete_row"),
            new DialogParameters { ["Table"] = Table, ["RowJson"] = row, ["Mode"] = "delete" },
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false }) await LoadAsync();
    }

    private void Close() => MudDialog.Close();
}
