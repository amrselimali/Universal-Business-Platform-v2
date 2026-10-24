using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Core.Models;

namespace Ubp.Web.Pages.Accounting;

public partial class CompletenessDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public CompletenessReport Report { get; set; } = new();
}
