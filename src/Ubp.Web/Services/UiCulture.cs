namespace Ubp.Web.Services;

public sealed class UiCulture
{
    private readonly Translator _translator;
    private readonly IHttpContextAccessor _http;

    public UiCulture(Translator translator, IHttpContextAccessor http)
    {
        _translator = translator; _http = http;
        var cookie = http.HttpContext?.Request.Cookies["ubp_lang"];
        Language = cookie == "en" ? "en" : "ar";
    }

    public string Language { get; set; }
    public bool IsRtl => Language == "ar";
    public string Dir => IsRtl ? "rtl" : "ltr";

    public string T(string key) => _translator.Get(Language, key);
    public string T(string key, params object[] args)
    {
        var text = T(key);
        try { return string.Format(text, args); }
        catch (FormatException) { return text; }
    }
}
