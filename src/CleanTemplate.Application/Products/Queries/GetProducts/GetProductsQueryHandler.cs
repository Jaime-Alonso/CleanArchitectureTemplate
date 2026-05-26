using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Products.ReadModels;
using Mediora;

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
        var criteria = new ProductSearchCriteria
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Sorting = new ProductSortingCriteria
            {
                SortBy = request.SortBy,
                SortDirection = request.SortDirection
            }
        };

        var products = await _productReadRepository
            .GetPagedAsync(criteria, cancellationToken)
            .ConfigureAwait(false);

        return products
            .Select(product => new ProductListItemDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock
            })
            .ToList();
    }
}
