using Ubp.Core.Security;
using Ubp.Web.Services;
using Xunit;

namespace Ubp.Tests;

public sealed class UserManualTests
{
    [Fact]
    public void User_manual_screen_is_registered_under_system_group()
    {
        var screen = AppRegistry.ByCode[ScreenCodes.UserManual];

        Assert.Equal("/system/user-manual", screen.Route);
        Assert.Equal(AppRegistry.GroupSystem, screen.GroupKey);
        Assert.Contains(screen.Actions, action => action.Code == ActionCodes.View);
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
