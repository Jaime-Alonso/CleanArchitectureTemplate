namespace CleanTemplate.Application.Abstractions;

public interface IApplicationDbContext
{
    IQueryable<TEntity> Set<TEntity>() where TEntity : class;
    void Add<TEntity>(TEntity entity) where TEntity : class;
    void Remove<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
