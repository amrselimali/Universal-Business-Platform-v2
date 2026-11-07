using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>فلتر فروع الجلسة (0092): كل الفروع افتراضيًا، ويمكن إلغاء بعضها لا كلها.</summary>
public sealed class SessionBranchFilterTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public SessionBranchFilterTests(TestDb db) => _db = db;

    [Fact]
    public async Task Filter_keeps_at_least_one_branch_and_persists_the_subset()
    {
        var admin = await _db.AdminActorAsync();
        var a = await _db.CreateBranchAsync("SF-A", "فرع فلتر 1");
        var b = await _db.CreateBranchAsync("SF-B", "فرع فلتر 2");
        var userId = await _db.CreateUserAsync("sf-user", "Passw0rd!", new[] { a, b });

        Guid sessionId;
        await using (var c = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await c.OpenAsync();
            sessionId = await c.ExecuteScalarAsync<Guid>(
                "INSERT INTO user_sessions (user_id, branch_id, expires_at) " +
                "VALUES (@userId, @a, now() + interval '1 hour') RETURNING id",
                new { userId, a });
        }

        var auth = _db.Auth();
        Assert.True(await auth.SetBranchFilterAsync(sessionId, userId, new[] { a }));
        await using (var c = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await c.OpenAsync();
            var filter = await c.ExecuteScalarAsync<Guid[]>(
                "SELECT filter_branch_ids FROM user_sessions WHERE id = @id", new { id = sessionId });
            Assert.NotNull(filter);
            Assert.Equal(new[] { a }, filter);
        }

        // إلغاء كل الفروع لا يترك الصفحة بلا فلتر: يُعاد ضبطها على كل فروع المستخدم (حد أدنى فرع).
        Assert.True(await auth.SetBranchFilterAsync(sessionId, userId, Array.Empty<Guid>()));
        await using (var c = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await c.OpenAsync();
            var filter = await c.ExecuteScalarAsync<Guid[]>(
                "SELECT filter_branch_ids FROM user_sessions WHERE id = @id", new { id = sessionId });
            Assert.Null(filter); // كل فروع المستخدم
        }
        _ = admin;
    }
}