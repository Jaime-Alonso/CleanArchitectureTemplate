using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Products.Commands.CreateProduct;
using CleanTemplate.Application.Products.Commands.DeleteProduct;
using CleanTemplate.Application.Products.Commands.UpdateProduct;
using CleanTemplate.Application.Tests.Testing.AsyncQuerying;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Core.SharedKernel.Errors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanTemplate.Application.Tests.Products.Commands;

public sealed class ProductCommandHandlersTests
{
    [Fact]
    public async Task CreateProductCommand_CreatesEntityAndReturnsId()
    {
        IApplicationDbContext context = new FakeApplicationDbContext();
        var handler = new CreateProductCommandHandler(context, NullLogger<CreateProductCommandHandler>.Instance);

        var command = new CreateProductCommand
        {
            Name = "Mouse",
            Description = "Wireless",
            Price = 25m,
            Stock = 5
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);

        var persisted = context.Set<Product>().Single(p => p.Id == result.Value);
        Assert.Equal("Mouse", persisted.Name);
    }

    [Fact]
    public async Task UpdateProductCommand_WhenProductMissing_ReturnsNotFoundFailure()
    {
        IApplicationDbContext context = new FakeApplicationDbContext();
        var handler = new UpdateProductCommandHandler(context, NullLogger<UpdateProductCommandHandler>.Instance);

        var command = new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            Name = "Monitor",
            Description = "4K",
            Price = 300m,
            Stock = 3
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "Products.NotFound");
        Assert.Contains(result.Errors, error => error.Type == ErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteProductCommand_WhenProductExists_RemovesEntity()
    {
        IApplicationDbContext context = new FakeApplicationDbContext();
        var product = new Domain.Entities.Product("Monitor", "4K", 300m, 3);
        context.Add(product);
        await context.SaveChangesAsync();

        var handler = new DeleteProductCommandHandler(context, NullLogger<DeleteProductCommandHandler>.Instance);
        var command = new DeleteProductCommand { Id = product.Id };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(context.Set<Product>().Any(p => p.Id == product.Id));
    }

    private sealed class FakeApplicationDbContext : IApplicationDbContext
    {
        private readonly List<Product> _products = [];

        public IQueryable<TEntity> Set<TEntity>() where TEntity : class
        {
            if (typeof(TEntity) == typeof(Product))
            {
                return (IQueryable<TEntity>)(object)new TestAsyncEnumerable<Product>(_products);
            }

            throw new NotSupportedException($"Entity type '{typeof(TEntity).Name}' is not supported by this fake context.");
        }

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
            if (entity is Product product)
            {
                _products.Add(product);
                return;
            }

            throw new NotSupportedException($"Entity type '{typeof(TEntity).Name}' is not supported by this fake context.");
        }

        public void Remove<TEntity>(TEntity entity) where TEntity : class
        {
            if (entity is Product product)
            {
                _products.Remove(product);
                return;
            }

            throw new NotSupportedException($"Entity type '{typeof(TEntity).Name}' is not supported by this fake context.");
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }
}
