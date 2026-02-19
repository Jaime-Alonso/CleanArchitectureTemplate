using CleanTemplate.Application.Products.Queries.GetProductById;
using CleanTemplate.Application.Products.Queries.GetProducts;

namespace CleanTemplate.Application.Abstractions;

public interface IProductReadRepository
{
    Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductListItemDto>> GetPagedAsync(
        int page,
        int pageSize,
        string sortBy,
        string sortDirection,
        CancellationToken cancellationToken = default);
}
