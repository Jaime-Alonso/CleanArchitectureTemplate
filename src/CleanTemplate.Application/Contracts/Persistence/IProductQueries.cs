using CleanTemplate.Application.Common.Pagination;
using CleanTemplate.Application.Products.ReadModels;

namespace CleanTemplate.Application.Contracts.Persistence;

public interface IProductQueries
{
    Task<ProductReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<ProductListItemReadModel>> GetPagedAsync(
        ProductSearchCriteria criteria,
        CancellationToken cancellationToken = default);
}
