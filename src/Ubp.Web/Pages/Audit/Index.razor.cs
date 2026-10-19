using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Audit;

public partial class Index
{
    [Inject] private AuditQueryService AuditService { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private static readonly string[] Actions =
    {
        "INSERT", "UPDATE", "DELETE", "LOGIN", "LOGOUT", "LOGIN_FAILED",
        "PERMISSION_CHANGE", "PASSWORD_RESET", "MIGRATION"
    };

    private static readonly string[] Tables =
    {
        "users", "user_permissions", "roles", "role_permissions", "branches", "warehouses",
        "currencies", "app_settings", "custom_field_definitions", "schemaversions", "qa_test_runs"
    };

    private readonly List<AuditEntry> _items = new();
    private List<UserListItem> _users = new();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private DateTime? _from;
    private DateTime? _to;
    private Guid? _userId;
    private Guid? _branchId;
    private string? _tableName;
    private string? _action;
    private long? _beforeId;
    private bool _hasMore;
    private bool _loadingMore;

    private string[] _actions = Actions;
    private string[] _tables = Tables;

    protected override string ScreenCode => ScreenCodes.AuditLog;

    protected override async Task OnLoadAsync()
    {
        _users = (await AuditService.GetFilterUsersAsync(Actor!)).ToList();
        _branches = await BranchService.OptionsAsync(Actor!);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _items.Clear();
        _beforeId = null;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        _loadingMore = true;
        var filter = new AuditFilter
        {
            From = _from is null ? null : new DateTimeOffset(_from.Value, TimeSpan.Zero),
            To = _to is null ? null : new DateTimeOffset(_to.Value.Date.AddDays(1), TimeSpan.Zero),
            UserId = _userId,
            BranchId = _branchId,
            TableName = _tableName,
            Action = _action,
            BeforeId = _beforeId
        };
        var page = await AuditService.ListAsync(Actor!, filter);
        _items.AddRange(page.Items);
        _beforeId = page.NextBeforeId;
        _hasMore = page.HasMore;
        _loadingMore = false;
    }

    private async Task LoadMoreAsync() => await RunAsync(LoadPageAsync);

    private async Task ClearAsync()
    {
        _from = null; _to = null; _userId = null; _branchId = null;
        _tableName = null; _action = null;
        await RefreshAsync();
    }

    private static string CairoTime(DateTimeOffset value)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
            return TimeZoneInfo.ConvertTime(value, zone).ToString("dd/MM/yyyy HH:mm:ss");
        }
        catch
        {
            return value.ToString("dd/MM/yyyy HH:mm:ss");
        }
    }

    private static Color ActionColor(string action) => action switch
    {
        "DELETE" or "LOGIN_FAILED" => Color.Error,
        "PERMISSION_CHANGE" or "PASSWORD_RESET" => Color.Warning,
        "LOGIN" or "LOGOUT" => Color.Info,
        "MIGRATION" => Color.Secondary,
        _ => Color.Default
    };

    private async Task ShowDetailAsync(AuditEntry entry) =>
        await DialogService.ShowAsync<AuditDetailDialog>(T.T("audit.detail"),
            new DialogParameters { ["Entry"] = entry },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
}
