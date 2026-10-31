using Dapper;
using Npgsql;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// الإشعارات الداخلية (الترحيل 0040): التوجيه لمستخدم أو دور، العدّاد، القراءة، والمسح،
/// وبثّ الاشتراكات بلا تسريب بعد التخلّص.
/// </summary>
public sealed class NotificationTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public NotificationTests(TestDb db) => _db = db;

    private NotificationRepository Repo() => new(_db.Factory);

    private NotificationService Service(NotificationBroadcaster broadcaster) =>
        new(Repo(), _db.Permissions(), broadcaster);

    private async Task<Guid> CreateRoleAsync(string code)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO roles (code, name_ar) VALUES (@code, @code) RETURNING id",
            new { code = code + Guid.NewGuid().ToString("N")[..6] });
    }

    private async Task AssignRoleAsync(Guid userId, Guid roleId)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO user_roles (user_id, role_id) VALUES (@userId, @roleId)",
            new { userId, roleId });
    }

    [Fact]
    public async Task Direct_notification_is_listed_and_counted_as_unread()
    {
        var actor = await _db.AdminActorAsync();
        var service = Service(new NotificationBroadcaster());

        await service.SendAsync(new NotificationInput
        {
            UserId = actor.UserId, TitleKey = "notifications.test", Severity = "WARNING"
        });

        Assert.Equal(1, await service.UnreadCountAsync(actor));
        var page = await service.ListAsync(actor, new PageRequest(), false);
        Assert.Single(page.Items);
        Assert.False(page.Items[0].IsRead);
        Assert.Equal(NotificationSeverities.Warning, page.Items[0].Severity);
    }

    [Fact]
    public async Task Mark_read_all_and_clear_stop_the_counter()
    {
        var actor = await _db.AdminActorAsync();
        var service = Service(new NotificationBroadcaster());
        await service.SendAsync(new NotificationInput
            { UserId = actor.UserId, TitleKey = "notifications.one" });
        await service.SendAsync(new NotificationInput
            { UserId = actor.UserId, TitleKey = "notifications.two" });

        var first = (await service.ListAsync(actor, new PageRequest(), true)).Items[0];
        await service.MarkReadAsync(actor, first.Id);
        Assert.Equal(1, await service.UnreadCountAsync(actor));

        await service.MarkAllReadAsync(actor);
        Assert.Equal(0, await service.UnreadCountAsync(actor));

        await service.ClearAsync(actor);
        Assert.Empty((await service.ListAsync(actor, new PageRequest(), false)).Items);
    }

    [Fact]
    public async Task Send_requires_exactly_one_target()
    {
        var service = Service(new NotificationBroadcaster());

        await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SendAsync(new NotificationInput { TitleKey = "notifications.none" }));

        await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SendAsync(new NotificationInput
            {
                UserId = Guid.NewGuid(), RoleId = Guid.NewGuid(), TitleKey = "notifications.both"
            }));

        await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SendAsync(new NotificationInput
            {
                UserId = Guid.NewGuid(), TitleKey = "   "
            }));
    }

    [Fact]
    public async Task Role_notification_is_visible_only_to_members()
    {
        var branch = await _db.CreateBranchAsync("NT-B", "فرع الإشعارات");
        var member = await _db.CreateUserAsync("notif.member", "Str0ng!Member2026", [branch]);
        var outsider = await _db.CreateUserAsync("notif.outside", "Str0ng!Outside2026", [branch]);
        var roleId = await CreateRoleAsync("NOTIF");
        await AssignRoleAsync(member, roleId);

        await Repo().CreateAsync(new NotificationInput
        {
            RoleId = roleId, TitleKey = "notifications.role_broadcast"
        });

        Assert.Single((await Repo().ListAsync(member, new PageRequest(), false)).Items);
        Assert.Empty((await Repo().ListAsync(outsider, new PageRequest(), false)).Items);
        Assert.Equal(1, await Repo().CountUnreadAsync(member));
        Assert.Equal(0, await Repo().CountUnreadAsync(outsider));
    }

    [Fact]
    public async Task Broadcaster_delivers_and_releases_subscribers_on_dispose()
    {
        var broadcaster = new NotificationBroadcaster();
        var userId = Guid.NewGuid();
        var received = new List<Notification>();

        var subscription = broadcaster.Subscribe(userId, notification =>
        {
            received.Add(notification);
            return Task.CompletedTask;
        });
        Assert.Equal(1, broadcaster.SubscriberCount);

        await broadcaster.PublishAsync(userId, new Notification { TitleKey = "first" });
        Assert.Single(received);

        subscription.Dispose();
        Assert.Equal(0, broadcaster.SubscriberCount);

        await broadcaster.PublishAsync(userId, new Notification { TitleKey = "second" });
        Assert.Single(received);
    }

    [Fact]
    public async Task Role_notification_broadcasts_to_every_member()
    {
        var branch = await _db.CreateBranchAsync("NT-B2", "فرع بث الدور");
        var first = await _db.CreateUserAsync("notif.f1", "Str0ng!First2026", [branch]);
        var second = await _db.CreateUserAsync("notif.f2", "Str0ng!Second2026", [branch]);
        var roleId = await CreateRoleAsync("NOTIFB");
        await AssignRoleAsync(first, roleId);
        await AssignRoleAsync(second, roleId);

        var broadcaster = new NotificationBroadcaster();
        var hits = new List<Guid>();
        using var sub1 = broadcaster.Subscribe(first,
            n => { lock (hits) hits.Add(n.Id); return Task.CompletedTask; });
        using var sub2 = broadcaster.Subscribe(second,
            n => { lock (hits) hits.Add(n.Id); return Task.CompletedTask; });

        var notification = await Service(broadcaster).SendAsync(new NotificationInput
        {
            RoleId = roleId, TitleKey = "notifications.role_broadcast"
        });

        Assert.Equal(2, hits.Count(id => id == notification.Id));
    }
}
