using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    public async Task<Guid> AddReadingAsync(Guid assetId, decimal value, decimal delta, string source,
        string? note, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO asset_meter_readings (asset_id, value, delta, source, note, created_by)
            VALUES (@assetId, @value, @delta, @source, @note, @userId)
            RETURNING id
            """, new { assetId, value, delta, source, note, userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AssetMeterReading>> ListReadingsAsync(Guid assetId,
        int take = 50, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<AssetMeterReading>(new CommandDefinition("""
            SELECT id AS Id, asset_id AS AssetId, reading_at AS ReadingAt, value AS Value,
                   delta AS Delta, source AS Source, note AS Note
            FROM asset_meter_readings WHERE asset_id = @assetId
            ORDER BY reading_at DESC LIMIT @take
            """, new { assetId, take = Math.Clamp(take, 1, 200) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SetMeterValueAsync(Guid assetId, decimal value, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE fixed_assets SET current_meter_value = @value, updated_by = @userId,
                   updated_at = now(), version = version + 1
            WHERE id = @assetId
            """, new { assetId, value, userId }, cancellationToken: ct));
    }
}
