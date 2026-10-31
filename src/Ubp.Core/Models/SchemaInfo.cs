namespace Ubp.Core.Models;

/// <summary>وصف جدول لعرض شاشة قاعدة البيانات (نُقل من طبقة المستودعات إلى النماذج).</summary>
public sealed class TableInfo
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
}

public sealed record ColumnInfo(string Name, string DataType, bool Nullable);

public sealed record RowPage(IReadOnlyList<string> Columns, IReadOnlyList<string> RowsJson, int Total);
