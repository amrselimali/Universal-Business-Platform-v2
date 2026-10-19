using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Ubp.Web.Components;
using Ubp.Web.Endpoints;
using Ubp.Web.Middleware;
using Ubp.Web.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddUbpServices(builder.Configuration);

var app = builder.Build();

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
app.UseSerilogRequestLogging();

app.MapAuthEndpoints();
app.MapSystemEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await DatabaseBootstrapper.InitializeAsync(app);
app.Run();
