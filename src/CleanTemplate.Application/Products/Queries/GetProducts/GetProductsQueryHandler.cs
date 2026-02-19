using CleanTemplate.Application.Abstractions;
using MediatR;

namespace CleanTemplate.Application.Products.Queries.GetProducts;

public sealed class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, IReadOnlyList<ProductListItemDto>>
{
    private readonly IProductReadRepository _productReadRepository;

    public GetProductsQueryHandler(IProductReadRepository productReadRepository)
    {
        _productReadRepository = productReadRepository;
    }

    public async Task<IReadOnlyList<ProductListItemDto>> Handle(
        GetProductsQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedPage = GetProductsQuery.NormalizePage(request.Page);
        var normalizedPageSize = GetProductsQuery.NormalizePageSize(request.PageSize);
        var normalizedSortBy = GetProductsQuery.NormalizeSortBy(request.SortBy);
        var normalizedSortDirection = GetProductsQuery.NormalizeSortDirection(request.SortDirection);

        return await _productReadRepository
            .GetPagedAsync(normalizedPage, normalizedPageSize, normalizedSortBy, normalizedSortDirection, cancellationToken)
            .ConfigureAwait(false);
    }
}
