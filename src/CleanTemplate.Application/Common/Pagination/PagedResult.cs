namespace CleanTemplate.Application.Common.Pagination;

public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public required int TotalPages { get; init; }

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        int safePageSize = pageSize < 1 ? 1 : pageSize;
        int totalPages = totalCount <= 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)safePageSize);

        return new PagedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }
}
