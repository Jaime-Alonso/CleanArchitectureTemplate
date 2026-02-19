using CleanTemplate.Application.Abstractions;
using CleanTemplate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Application.Products;

internal static class ProductDbContextExtensions
{
    internal static Task<Product?> FindByIdAsync(
        this IApplicationDbContext context,
        Guid id,
        CancellationToken cancellationToken = default)
        => context.Set<Product>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
}
