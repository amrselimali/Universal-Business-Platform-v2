using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Admin;

public partial class RowActionDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter, EditorRequired] public string Table { get; set; } = "";
    [Parameter] public string RowJson { get; set; } = "{}";
    [Parameter] public string Mode { get; set; } = "update";
    [Inject] private DatabaseAdminService DatabaseService { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private string _json = "";
    private string _confirmation = "";
    private bool _busy;

    private string Expected => Mode == "delete" ? $"DELETE {Table}" : $"UPDATE {Table}";

    protected override void OnInitialized()
    {
        try
        {
            using var document = JsonDocument.Parse(RowJson);
            _json = JsonSerializer.Serialize(document.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            _json = RowJson;
        }
    }

    private void Cancel() => MudDialog.Cancel();

    private async Task ExecuteAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var id = ReadId(_json);
            if (string.IsNullOrWhiteSpace(id)) throw new ValidationAppException("validation.row_id_missing");

            if (Mode == "delete")
            {
                await DatabaseService.DeleteRowAsync(actor, Table, id, _confirmation);
            }
            else
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(_json)
                             ?? throw new ValidationAppException("validation.invalid_json");
                await DatabaseService.UpdateRowAsync(actor, Table, id, values, _confirmation);
            }
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (JsonException)
        {
            Snackbar.Add(T.T("validation.invalid_json"), Severity.Error);
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        catch (Exception)
        {
            Snackbar.Add(T.T("error.generic"), Severity.Error);
        }
        finally { _busy = false; }
    }

    private static string? ReadId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("id", out var id) ? id.ToString() : null;
        }
        catch { return null; }
    }
}
