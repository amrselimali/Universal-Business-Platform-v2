using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Core.Models;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Audit;

public partial class AuditDetailDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public AuditEntry Entry { get; set; } = new();
    [Inject] private UiCulture T { get; set; } = default!;

    private sealed record DiffRow(string Field, string OldValue, string NewValue, bool Changed);
    private readonly List<DiffRow> _rows = new();

    protected override void OnInitialized()
    {
        var oldValues = Parse(Entry.OldData);
        var newValues = Parse(Entry.NewData);
        foreach (var key in oldValues.Keys.Union(newValues.Keys).OrderBy(k => k))
        {
            oldValues.TryGetValue(key, out var oldValue);
            newValues.TryGetValue(key, out var newValue);
            _rows.Add(new DiffRow(key, oldValue ?? "—", newValue ?? "—",
                !string.Equals(oldValue, newValue, StringComparison.Ordinal)));
        }
    }

    private static Dictionary<string, string?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new();
            return document.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => p.Value.GetString(),
                    _ => p.Value.GetRawText()
                }, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private void Close() => MudDialog.Close();
}
