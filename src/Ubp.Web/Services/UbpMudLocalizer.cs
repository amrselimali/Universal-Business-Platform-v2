using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Ubp.Web.Services;

public sealed class UbpMudLocalizer : MudLocalizer
{
    private readonly UiCulture _culture;

    public UbpMudLocalizer(UiCulture culture) => _culture = culture;

    public override LocalizedString this[string key] =>
        new(key, _culture.T(key));
}
