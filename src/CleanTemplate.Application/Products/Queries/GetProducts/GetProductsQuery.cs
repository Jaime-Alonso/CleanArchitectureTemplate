using CleanTemplate.Application.Common.Criteria;
using CleanTemplate.Application.Common.Pagination;
using CleanTemplate.Application.Products.ReadModels;
using Mediora;

namespace CleanTemplate.Application.Products.Queries.GetProducts;

public sealed record GetProductsQuery : IRequest<PagedResult<ProductListItemDto>>
{
    public int Page { get; init; } = PaginationCriteria.DefaultPage;
    public int PageSize { get; init; } = PaginationCriteria.DefaultPageSize;
    public string SortBy { get; init; } = ProductSortingCriteria.DefaultSortBy;
    public string SortDirection { get; init; } = SortingCriteria.DefaultSortDirection;
}
