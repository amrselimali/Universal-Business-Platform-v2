using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    /// <summary>كل قراءات العدّاد حتى تاريخ معيّن (تصاعديًا) لحساب بداية المدة والرصيد.</summary>
    public async Task<IReadOnlyList<AssetMeterReading>> ListReadingsUpToAsync(Guid assetId,
        DateTime to, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<AssetMeterReading>(new CommandDefinition("""
            SELECT id AS Id, asset_id AS AssetId, reading_at AS ReadingAt, value AS Value,
                   delta AS Delta, source AS Source, note AS Note
            FROM asset_meter_readings
            WHERE asset_id = @assetId AND reading_at < @to
            ORDER BY reading_at
            """, new { assetId, to }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>تسويات العدّاد حتى تاريخ معيّن (تصاعديًا).</summary>
    public async Task<IReadOnlyList<AssetMeterAdjustment>> ListAdjustmentsUpToAsync(Guid assetId,
        DateTime to, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<AssetMeterAdjustment>(new CommandDefinition("""
            SELECT id AS Id, asset_id AS AssetId, adjusted_at AS AdjustedAt, units AS Units,
                   note AS Note, created_at AS CreatedAt
            FROM asset_meter_adjustments
            WHERE asset_id = @assetId AND adjusted_at < @to
            ORDER BY adjusted_at
            """, new { assetId, to }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid> AddAdjustmentAsync(Guid assetId, DateTime adjustedAt, decimal units,
        string? note, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO asset_meter_adjustments (asset_id, adjusted_at, units, note, created_by)
            VALUES (@assetId, @adjustedAt, @units, @note, @userId)
            RETURNING id
            """, new { assetId, adjustedAt, units, note, userId }, cancellationToken: ct));
    }
}
