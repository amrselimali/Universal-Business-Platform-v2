using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IUserFavoriteScreenRepository
{
    Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<bool> AddAsync(Guid userId, string screenCode, CancellationToken ct = default);
    Task RemoveAsync(Guid userId, string screenCode, CancellationToken ct = default);
}

/// <summary>مفضلة شاشات المستخدم (اختصارات الصفحة الرئيسية) — 0089.</summary>
public sealed class UserFavoriteScreenRepository : IUserFavoriteScreenRepository
{
    private readonly IDbConnectionFactory _factory;
    public UserFavoriteScreenRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT screen_code FROM user_favorite_screens WHERE user_id = @userId " +
            "ORDER BY created_at", new { userId }, cancellationToken: ct))).ToList();
    }

    public async Task<bool> AddAsync(Guid userId, string screenCode, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO user_favorite_screens (user_id, screen_code)
            VALUES (@userId, @screenCode)
            ON CONFLICT (user_id, screen_code) DO NOTHING
            """, new { userId, screenCode }, cancellationToken: ct)) > 0;
    }

    public async Task RemoveAsync(Guid userId, string screenCode, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM user_favorite_screens WHERE user_id = @userId AND screen_code = @screenCode",
            new { userId, screenCode }, cancellationToken: ct));
    }
}
