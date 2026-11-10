using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Partners;

public partial class PartnerDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public PartnerKind Kind { get; set; } = PartnerKind.Customer;
    [Parameter] public Partner? Model { get; set; }
    [Inject] private PartnerService Partners { get; set; } = default!;
    [Inject] private BranchService Branches { get; set; } = default!;
    [Inject] private CustomFieldService CustomFields { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private PartnerInput _input = new();
    private IReadOnlyList<PartnerCategory> _categories = Array.Empty<PartnerCategory>();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private IReadOnlyList<CustomFieldDefinition> _fields = Array.Empty<CustomFieldDefinition>();
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;
    private bool _canUseCompanyScope;
    private string? _nextCode;
    private bool IsEdit => Model is not null;

    /// <summary>كود العميل يُولَّد آليًا لكل فرع (بداية من 1) ولا يُعدَّل.</summary>
    private string CodeDisplay => IsEdit ? Model!.Code : _nextCode ?? "";

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _categories = await Partners.ListCategoriesAsync(actor, Kind, false);
        _branches = await Branches.OptionsAsync(actor);
        var entity = Kind == PartnerKind.Customer ? "customers" : "suppliers";
        _fields = (await CustomFields.ListActiveAsync(entity)).ToList();

        // نطاق «الشركة» متاح فقط لمن يملك كل الفروع؛ غيره يُقيَّد بفرع من فروعه.
        _canUseCompanyScope = actor.IsSuperAdmin;

        if (Model is null)
        {
            _input.ScopeType = "BRANCH";
            _input.BranchId = actor.CurrentBranchId ?? _branches.FirstOrDefault()?.Id
                              ?? actor.AllowedBranchIds.FirstOrDefault();
            await RefreshNextCodeAsync(actor);
            return;
        }

        _input = new PartnerInput
        {
            Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
            CategoryId = Model.CategoryId, ScopeType = Model.ScopeType, BranchId = Model.BranchId,
            Phone = Model.Phone, Email = Model.Email, Address = Model.Address,
            TaxNumber = Model.TaxNumber, CreditLimit = Model.CreditLimit, Notes = Model.Notes,
            IsActive = Model.IsActive
        };
        foreach (var pair in Model.CustomFields)
            _values[pair.Key] = pair.Value?.ToString();
    }

    /// <summary>يُحدِّث معاينة الكود التالي للفرع المختار (قراءة فقط).</summary>
    private async Task RefreshNextCodeAsync(ActorContext? actor = null)
    {
        if (Kind != PartnerKind.Customer || IsEdit) return;
        actor ??= await UserContext.RequireAsync();
        var scope = _input.ScopeType == "BRANCH" ? "BRANCH" : "COMPANY";
        _nextCode = await Partners.NextCodeAsync(actor, Kind,
            scope == "BRANCH" ? _input.BranchId : null, scope);
    }

    private async Task OnScopeChanged(string scopeType)
    {
        _input.ScopeType = scopeType == "COMPANY" && _canUseCompanyScope ? "COMPANY" : "BRANCH";
        if (_input.ScopeType == "BRANCH" && _input.BranchId is null)
            _input.BranchId = _branches.FirstOrDefault()?.Id;
        await RefreshNextCodeAsync();
    }

    private async Task OnBranchChanged(Guid? branchId)
    {
        _input.BranchId = branchId;
        await RefreshNextCodeAsync();
    }

    private string CategoryName(PartnerCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

    private string Label(CustomFieldDefinition field) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(field.LabelEn) ? field.LabelEn! : field.LabelAr;

    private string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    private void Set(string key, string? value) => _values[key] = value;
    private bool GetBool(string key) => bool.TryParse(Get(key), out var value) && value;
    private void SetBool(string key, bool value) => _values[key] = value.ToString().ToLowerInvariant();

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _input.CustomFields = _values.ToDictionary(p => p.Key, p => (object?)p.Value);
            var saved = Model is null
                ? await Partners.CreateAsync(actor, Kind, _input)
                : await Partners.UpdateAsync(actor, Kind, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
