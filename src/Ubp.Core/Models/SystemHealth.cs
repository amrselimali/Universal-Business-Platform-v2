namespace Ubp.Core.Models;

public static class BackupStatuses
{
    public const string Running = "RUNNING";
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
}

public sealed class BackupLogEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = BackupStatuses.Running;
    public string Kind { get; set; } = "FULL";
    public string? FileName { get; set; }
    public long? SizeBytes { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Error { get; set; }
}

/// <summary>حقائق صحية من قاعدة البيانات (بلا صلاحيات عمل).</summary>
public sealed record SystemHealthFacts(
    bool DatabaseOk,
    string? ServerVersion,
    string? DatabaseName,
    string? CurrentUser,
    string? Error,
    long DatabaseSizeBytes,
    int TableCount,
    int LongRunningQueries,
    BackupLogEntry? LastBackup);

/// <summary>لقطة شاشة صحة النظام: حقائق القاعدة + حالة الترحيلات + آخر نسخة احتياطية.</summary>
public sealed record SystemHealthSnapshot(
    SystemHealthFacts Facts,
    int AppliedMigrations,
    int PendingMigrations,
    IReadOnlyList<string> PendingScripts);
