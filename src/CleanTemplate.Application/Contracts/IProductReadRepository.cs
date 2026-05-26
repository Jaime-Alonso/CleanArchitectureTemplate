using CleanTemplate.Application.Products.ReadModels;
using CleanTemplate.Application.Common.Pagination;

namespace CleanTemplate.Application.Contracts;

public interface IProductReadRepository
{
    Task<ProductReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<ProductListItemReadModel>> GetPagedAsync(
        ProductSearchCriteria criteria,
        CancellationToken cancellationToken = default);
}
