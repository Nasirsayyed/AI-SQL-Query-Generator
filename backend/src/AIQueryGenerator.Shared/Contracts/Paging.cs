namespace AIQueryGenerator.Shared.Contracts;

public class PagedRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    public int Page { get => _page; set => _page = value < 1 ? 1 : value; }

    /// <summary>Page size, clamped to 1..200.</summary>
    public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, 200); }

    public string? Search { get; set; }
    public string? SortBy { get; set; }

    /// <summary>"asc" or "desc".</summary>
    public string? SortDirection { get; set; }

    public bool Descending => !string.Equals(SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
    public int Skip => (Page - 1) * PageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
