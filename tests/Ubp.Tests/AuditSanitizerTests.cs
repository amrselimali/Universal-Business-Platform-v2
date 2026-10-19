using Ubp.Infrastructure.Audit;
using Xunit;

namespace Ubp.Tests;

public sealed class AuditSanitizerTests
{
    [Fact]
    public void Password_and_token_hashes_are_never_written_to_audit()
    {
        var json = AuditSanitizer.ToJson(new
        {
            Username = "user",
            PasswordHash = "secret-hash",
            TokenHash = "secret-token",
            Nested = new { password = "plain", RefreshToken = "refresh" },
            Items = new[] { new { AccessToken = "access", Name = "ok" } }
        });

        Assert.NotNull(json);
        Assert.Contains("user", json);
        Assert.Contains("ok", json);
        Assert.DoesNotContain("secret-hash", json);
        Assert.DoesNotContain("secret-token", json);
        Assert.DoesNotContain("plain", json);
        Assert.DoesNotContain("refresh", json);
        Assert.DoesNotContain("access", json);
    }
}
