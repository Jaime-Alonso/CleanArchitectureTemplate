using CleanTemplate.Application.Abstractions;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Infrastructure.Repositories.Writes;

public sealed class ProductWriteRepository : IProductWriteRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ProductWriteRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Set<Product>().FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
    }

    public void Add(Product product)
    {
        _dbContext.Add(product);
    }

    public void Remove(Product product)
    {
        _dbContext.Remove(product);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
