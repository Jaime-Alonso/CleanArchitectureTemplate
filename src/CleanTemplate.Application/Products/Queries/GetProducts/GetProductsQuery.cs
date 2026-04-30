using Mediora;

namespace CleanTemplate.Application.Products.Queries.GetProducts;

public sealed record GetProductsQuery : IRequest<IReadOnlyList<ProductListItemDto>>
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;
    public const string DefaultSortBy = "id";
    public const string DefaultSortDirection = "asc";

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public string SortBy { get; init; } = DefaultSortBy;
    public string SortDirection { get; init; } = DefaultSortDirection;

    public static int NormalizePage(int page)
    {
        return page < DefaultPage ? DefaultPage : page;
    }

    public static int NormalizePageSize(int pageSize)
    {
        if (pageSize < 1)
        {
            return DefaultPageSize;
        }

        return pageSize > MaxPageSize ? MaxPageSize : pageSize;
    }

    public static string NormalizeSortBy(string? sortBy)
    {
        return sortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => "name",
            "price" => "price",
            "stock" => "stock",
            _ => DefaultSortBy
        };
    }

    public static string NormalizeSortDirection(string? sortDirection)
    {
        return sortDirection?.Trim().ToLowerInvariant() == "desc"
            ? "desc"
            : DefaultSortDirection;
    }
}
