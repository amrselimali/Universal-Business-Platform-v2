namespace Ubp.Web.Services;

public sealed record ManualButtonDoc(
    string Label,
    string Permission,
    string Handler,
    string Behavior,
    string Destination);

public sealed record ManualScreenDoc(
    string Code,
    string Route,
    string Title,
    string GroupKey,
    string GroupTitle,
    string Purpose,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ManualButtonDoc> Buttons,
    IReadOnlyList<string> Actions,
    bool SourceAvailable);

public sealed record UserManualGeneration(
    string Markdown,
    DateTimeOffset GeneratedAt,
    int ScreenCount,
    int ButtonCount,
    int TableCount,
    bool SchemaAvailable,
    bool SourceAvailable);

public sealed record ManualColumnDoc(string Name, string DataType, bool Nullable);
public sealed record ManualTableDoc(string Name, long RowCount, IReadOnlyList<ManualColumnDoc> Columns);

public sealed record ManualDialogDoc(string Name, string Purpose, IReadOnlyList<ManualButtonDoc> Buttons);
