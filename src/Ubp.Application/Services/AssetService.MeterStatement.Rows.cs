using Ubp.Core.Models;

namespace Ubp.Application.Services;

public sealed partial class AssetService
{
    private static (DateTime Start, DateTime End) ResolveRange(string mode, DateTime from,
        DateTime? to)
    {
        var start = from.Date;
        var last = (to ?? from).Date;

        return mode switch
        {
            MeterStatementModes.Month => (
                new DateTime(start.Year, start.Month, 1),
                new DateTime(last.Year, last.Month, 1).AddMonths(1)),
            MeterStatementModes.SingleDay => (start, start.AddDays(1)),
            _ => start <= last ? (start, last.AddDays(1)) : (last, start.AddDays(1))
        };
    }

    private static List<AssetMeterStatementRow> BuildRows(string mode, DateTime start,
        DateTime end, IReadOnlyList<AssetMeterReading> readings,
        IReadOnlyList<AssetMeterAdjustment> adjustments)
    {
        var rows = new List<AssetMeterStatementRow>();
        var cursor = start;
        while (cursor < end)
        {
            var bucketEnd = mode == MeterStatementModes.Month
                ? cursor.AddMonths(1)
                : cursor.AddDays(1);
            if (bucketEnd > end) bucketEnd = end;

            var consumed = readings
                .Where(r => InRange(r.ReadingAt, cursor, bucketEnd))
                .Sum(r => r.Delta);
            var bucket = adjustments
                .Where(a => InRange(a.AdjustedAt, cursor, bucketEnd))
                .ToList();
            var notes = string.Join("؛ ", bucket
                .Where(a => !string.IsNullOrWhiteSpace(a.Note))
                .Select(a => $"{a.Units:+0.##;-0.##}: {a.Note}"));

            rows.Add(new AssetMeterStatementRow
            {
                Period = mode == MeterStatementModes.Month
                    ? cursor.ToString("yyyy-MM")
                    : cursor.ToString("yyyy-MM-dd"),
                Start = cursor, End = bucketEnd,
                Opening = BalanceBefore(cursor, readings, adjustments),
                Consumed = Math.Round(consumed, 2, MidpointRounding.AwayFromZero),
                Adjustments = Math.Round(bucket.Sum(a => a.Units), 2,
                    MidpointRounding.AwayFromZero),
                AdjustmentNotes = notes.Length == 0 ? null : notes
            });

            cursor = bucketEnd;
        }
        return rows;
    }

    private static List<AssetMeterMovement> BuildMovements(DateTime start, DateTime end,
        IReadOnlyList<AssetMeterReading> readings, IReadOnlyList<AssetMeterAdjustment> adjustments)
    {
        var movements = new List<AssetMeterMovement>();
        movements.AddRange(readings.Where(r => InRange(r.ReadingAt, start, end))
            .Select(r => new AssetMeterMovement
            {
                Kind = "READING", At = r.ReadingAt, Value = r.Value, Units = r.Delta,
                Source = r.Source, Note = r.Note
            }));
        movements.AddRange(adjustments.Where(a => InRange(a.AdjustedAt, start, end))
            .Select(a => new AssetMeterMovement
            {
                Kind = "ADJUSTMENT", At = a.AdjustedAt, Value = 0, Units = a.Units,
                Source = "ADJUSTMENT", Note = a.Note
            }));
        return movements.OrderBy(m => m.At).ToList();
    }

    private static bool InRange(DateTimeOffset at, DateTime start, DateTime end)
    {
        var local = at.ToLocalTime().DateTime;
        return local >= start && local < end;
    }

    /// <summary>رصيد بداية الفترة = آخر قراءة قبلها + مجموع التسويات قبلها.</summary>
    private static decimal BalanceBefore(DateTime at, IReadOnlyList<AssetMeterReading> readings,
        IReadOnlyList<AssetMeterAdjustment> adjustments)
    {
        var lastReading = readings
            .Where(r => r.ReadingAt.ToLocalTime().DateTime < at)
            .OrderBy(r => r.ReadingAt)
            .LastOrDefault();
        var opening = lastReading?.Value ?? 0m;
        var before = adjustments
            .Where(a => a.AdjustedAt.ToLocalTime().DateTime < at)
            .Sum(a => a.Units);
        return Math.Round(opening + before, 2, MidpointRounding.AwayFromZero);
    }
}
