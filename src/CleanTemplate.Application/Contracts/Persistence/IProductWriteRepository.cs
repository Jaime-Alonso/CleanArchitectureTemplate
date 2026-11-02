using CleanTemplate.Domain.Entities;

namespace CleanTemplate.Application.Contracts.Persistence;

public interface IProductWriteRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(Product product);
    void Remove(Product product);
}
