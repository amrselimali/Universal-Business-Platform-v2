using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Serilog;
using Ubp.Web.Components;
using Ubp.Web.Endpoints;
using Ubp.Web.Middleware;
using Ubp.Web.Startup;

var builder = WebApplication.CreateBuilder(args);

// حماية الإقلاع: إذا طُلب عنوان HTTPS بلا شهادة مُهيّأة (بيئة تطوير بلا dev-cert)
// نحذف عناوين https من قائمة العناوين ونبقي http، حتى لا يفشل app.Run() بخطأ ربط العنوان.
var configuredUrls = builder.Configuration["urls"]
                     ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
var hasHttpsCertificate =
    !string.IsNullOrWhiteSpace(builder.Configuration["Kestrel:Certificates:Default:Path"]) ||
    !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("ASPNETCORE_Kestrel__Certificates__Default__Path"));
if (!string.IsNullOrWhiteSpace(configuredUrls) && !hasHttpsCertificate)
{
    // بلا شهادة HTTPS: نستخدم عناوين http إن وُجدت، وإلا نحوّل https إلى http على نفس المضيف/المنفذ.
    var httpUrls = new List<string>();
    var httpsUrls = new List<string>();
    foreach (var raw in configuredUrls.Split(';',
                 StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) httpUrls.Add(raw);
        else if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) httpsUrls.Add(raw);
    }
    var chosen = httpUrls.Count > 0
        ? httpUrls
        : httpsUrls.Select(h => "http://" + h["https://".Length..]).ToList();
    if (chosen.Count > 0)
        builder.WebHost.UseUrls(chosen.ToArray());
}


builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddUbpServices(builder.Configuration);

var app = builder.Build();

// التحقق المعماري القياسي: يفشل الإقلاع إذا كانت الإعدادات ناقصة أو قيمة افتراضية غير آمنة.
// يجب أن يسبق أي اتصال بقاعدة البيانات (الترحيل والتهيئة).
app.Services.GetRequiredService<IStartupValidator>().Validate();

// تشخيص الإعدادات بلا كشف أسرار (مضيف/منفذ/قاعدة/مستخدم + وجود user-secrets + اتصال فعلي).
if (DiagnosticsCli.IsRequested(args))
{
    Environment.ExitCode = DiagnosticsCli.Run(app.Services, args);
    return;
}

// مسار النشر: تنفيذ الترحيلات من CI/CD ثم الخروج (بلا تشغيل الويب).
if (MigrationCli.IsRequested(args))
{
    Environment.ExitCode = MigrationCli.Run(app.Services);
    return;
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseMiddleware<TraceMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseSerilogRequestLogging();

app.MapAuthEndpoints();
app.MapSystemEndpoints();
app.MapWhatsAppWebhookEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await DatabaseBootstrapper.InitializeAsync(app);

try
{
    app.Run();
}
catch (IOException ex)
{
    // خطأ ربط العنوان غالبًا لأن نسخة سابقة من التطبيق ما تزال ماسكة للمنفذ.
    Console.Error.WriteLine("تعذّر إقلاع التطبيق (خطأ في ربط العنوان): " + ex.Message);
    Console.Error.WriteLine(
        "إن كان المنفذ مستخدمًا بالفعل، أغلق النسخة السابقة من التطبيق أو غيّر المنفذ " +
        "عبر متغيّر البيئة ASPNETCORE_URLS (مثال: http://localhost:50900).");
    Environment.ExitCode = 1;
}
