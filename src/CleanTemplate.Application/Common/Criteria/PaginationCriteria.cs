namespace CleanTemplate.Application.Common.Criteria;

public abstract record PaginationCriteria
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => NormalizePage(Page);
    public int NormalizedPageSize => NormalizePageSize(PageSize);

    public static int NormalizePage(int page)
    {
        return page < DefaultPage ? DefaultPage : page;
    }

    public static int NormalizePageSize(int pageSize)
    {
        return pageSize < 1 ? DefaultPageSize : pageSize > MaxPageSize ? MaxPageSize : pageSize;
    }
}
