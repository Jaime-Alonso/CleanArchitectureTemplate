using CleanTemplate.Application.Products.ReadModels;

namespace CleanTemplate.Application.Abstractions;

public interface IProductReadRepository
{
    Task<ProductReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductListItemReadModel>> GetPagedAsync(
        int page,
        int pageSize,
        string sortBy,
        string sortDirection,
        CancellationToken cancellationToken = default);
}
