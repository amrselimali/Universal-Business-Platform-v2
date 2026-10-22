using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Ubp.Web.Pages.Accounting;

public sealed record TreeMoveOption(Guid Id, string Label, short Level);

public partial class TreeMoveDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public IReadOnlyList<TreeMoveOption> Options { get; set; } = Array.Empty<TreeMoveOption>();
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public bool AllowRoot { get; set; }

    private Guid? _selected;

    private void Confirm() => MudDialog.Close(DialogResult.Ok(_selected ?? Guid.Empty));
}
