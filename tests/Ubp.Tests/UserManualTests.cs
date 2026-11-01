using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Services;
using Xunit;

namespace Ubp.Tests;

public sealed class UserManualTests
{
    private sealed class EmptyPermissionRepository : IPermissionRepository
    {
        public Task<IReadOnlyList<PermissionEntry>> GetRoleGrantsForUserAsync(Guid userId,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PermissionEntry>>([]);

        public Task<IReadOnlyList<PermissionEntry>> GetUserOverridesAsync(Guid userId,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PermissionEntry>>([]);

        public Task ReplaceUserPermissionsAsync(Guid userId, IEnumerable<PermissionEntry> permissions,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task CopyUserPermissionsAsync(Guid sourceUserId, Guid targetUserId,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Ubp.Tests";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = root;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    [Fact]
    public async Task Generator_reads_screens_buttons_tables_and_workflows()
    {
        var repo = FindRepoRoot();
        var environment = new FakeEnvironment(Path.Combine(repo, "src", "Ubp.Web"));
        var translator = new Translator(environment, NullLogger<Translator>.Instance);
        var reader = new UserManualSourceReader(environment, NullLogger<UserManualSourceReader>.Instance);
        var generator = new UserManualGenerator(reader, new FakeSchemaProvider(), translator,
            NullLogger<UserManualGenerator>.Instance);
        var actor = new ActorContext
        {
            UserId = Guid.NewGuid(), Username = "admin", IsAdmin = true,
            Language = "ar", TraceId = "test"
        };

        var result = await generator.GenerateAsync(actor, force: true);

        Assert.Contains("## 1. كيف يعمل النظام", result.Markdown);
        Assert.Contains("## 3. دورات العمل", result.Markdown);
        Assert.Contains("```flow", result.Markdown);
        Assert.Contains("## 4. دليل الشاشات والأزرار", result.Markdown);
        Assert.Contains("### الحوارات", result.Markdown);
        Assert.Contains("AccountDialog", result.Markdown);
        Assert.Contains("STOCK_DOCUMENTS/POST", result.Markdown);
        Assert.Contains("#### `/inventory/items` — الأصناف", result.Markdown);
        Assert.Contains("| الزر | الصلاحية |", result.Markdown);
        Assert.Contains("## 5. قاعدة البيانات", result.Markdown);
        Assert.Contains("`users`", result.Markdown);
        Assert.Contains("## 6. قيود الأزرار واستخدامها", result.Markdown);
        Assert.Contains("## 7. مصفوفة الصلاحيات", result.Markdown);
        Assert.Contains("## 8. سجل التدقيق", result.Markdown);
        Assert.True(result.ScreenCount > 50);
        Assert.True(result.ButtonCount > 200);
        Assert.Equal(1, result.TableCount);
        Assert.True(result.SchemaAvailable);
        Assert.True(result.SourceAvailable);

        var html = MarkdownRenderer.Render(result.Markdown);
        Assert.Contains("ubp-flow", html);
        Assert.Equal(Count(html, "<table"), Count(html, "</table>"));
    }

    private sealed class FakeSchemaProvider : IManualSchemaProvider
    {
        public Task<IReadOnlyList<TableInfo>> GetTablesAsync(ActorContext actor,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TableInfo>>(
        [
            new TableInfo { Schema = "public", Name = "users", RowCount = 2, ColumnCount = 2 }
        ]);

        public Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ActorContext actor, string table,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
        [
            new("id", "uuid", false),
            new("username", "character varying", false)
        ]);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "UniversalBusinessPlatform.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public async Task User_manual_view_requires_permission()
    {
        var service = new PermissionService(new EmptyPermissionRepository(),
            new MemoryCache(new MemoryCacheOptions()));
        var actor = new ActorContext
        {
            UserId = Guid.NewGuid(), Username = "user", IsAdmin = false,
            Language = "ar", TraceId = "test"
        };

        var snapshot = await service.GetSnapshotAsync(actor.UserId, actor.IsAdmin);
        Assert.False(snapshot[$"{ScreenCodes.UserManual}/{ActionCodes.View}"].Allowed);
        Assert.False(snapshot[$"{ScreenCodes.Users}/{ActionCodes.View}"].Allowed);

        var visible = await service.GetVisibleScreenCodesAsync(actor);
        Assert.DoesNotContain(ScreenCodes.UserManual, visible);
        Assert.DoesNotContain(ScreenCodes.Users, visible);
    }

    [Fact]
    public void User_manual_screen_is_registered_under_system_group()
    {
        var screen = AppRegistry.ByCode[ScreenCodes.UserManual];

        Assert.Equal("/system/user-manual", screen.Route);
        Assert.Equal(AppRegistry.GroupSystem, screen.GroupKey);
        Assert.Contains(screen.Actions, action => action.Code == ActionCodes.View);
    }

    [Fact]
    public async Task Source_reads_the_latest_file_from_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ubp-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "USER-MANUAL-AR.md");
        await File.WriteAllTextAsync(file, "# Manual v2\n\nlatest");
        try
        {
            var source = new UserManualSource(new FakeEnvironment(dir),
                NullLogger<UserManualSource>.Instance);
            var content = await source.LoadAsync(searchPaths: new[] { file });

            Assert.Contains("Manual v2", content.Markdown);
            Assert.Equal(file, content.SourcePath);
            Assert.NotNull(content.UpdatedAt);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Manual_resource_exists_and_renders()
    {
        var assembly = typeof(MarkdownRenderer).Assembly;
        var name = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith("USER-MANUAL-AR.md", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(name);
        using var stream = assembly.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var markdown = reader.ReadToEnd();

        Assert.Contains("# دليل المستخدم الشامل", markdown);
        var html = MarkdownRenderer.Render(markdown);
        Assert.Contains("<h1>", html);
        Assert.Contains("<table", html);
        Assert.Contains("<code>", html);
        Assert.Contains("</table>", html);
        Assert.Equal(Count(html, "<table"), Count(html, "</table>"));
        Assert.Equal(Count(html, "<tr>"), Count(html, "</tr>"));
        Assert.Equal(Count(html, "<td>"), Count(html, "</td>"));
        Assert.Equal(Count(html, "<code>"), Count(html, "</code>"));
        Assert.Equal(Count(html, "<strong>"), Count(html, "</strong>"));
        Assert.DoesNotContain("<script>", html);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [Fact]
    public void Renderer_escapes_html_and_renders_tables_lists_and_code()
    {
        const string markdown = """
            # Title

            | A | B |
            |---|---|
            | 1 | **2** |

            - one
            - two

            `x < y`

            <script>alert(1)</script>
            """;

        var html = MarkdownRenderer.Render(markdown);

        Assert.Contains("<h1>Title</h1>", html);
        Assert.Contains("<table", html);
        Assert.Contains("<strong>2</strong>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<code>x &lt; y</code>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);

        var plain = MarkdownRenderer.Render("#not a heading\ntext");
        Assert.Contains("<p>#not a heading text</p>", plain);
    }
}
