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
if (!string.IsNullOrWhiteSpace(configuredUrls) &&
    configuredUrls.Contains("https://", StringComparison.OrdinalIgnoreCase) &&
    !hasHttpsCertificate)
{
    var httpOnly = configuredUrls
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (httpOnly.Length > 0)
        builder.WebHost.UseUrls(httpOnly);
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
app.Run();
