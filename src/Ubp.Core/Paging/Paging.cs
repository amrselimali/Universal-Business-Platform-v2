namespace Ubp.Core.Paging;

public sealed class PageRequest
{
    public const int MaxPageSize = 25;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = MaxPageSize;
    public string? Search { get; init; }
    public string SortBy { get; init; } = "code";
    public bool Descending { get; init; }
    public bool IncludeInactive { get; init; }

    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize is < 1 or > MaxPageSize ? MaxPageSize : PageSize;
    public int Offset => (SafePage - 1) * SafePageSize;
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.MaxPageSize;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new() { Page = page, PageSize = pageSize };
}

public sealed class KeysetPage<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public long? NextBeforeId { get; init; }
    public bool HasMore { get; init; }
    public int PageSize { get; init; } = PageRequest.MaxPageSize;
}
