using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Products.Commands.CreateProduct;
using CleanTemplate.Application.Products.Commands.DeleteProduct;
using CleanTemplate.Application.Products.Commands.UpdateProduct;
using CleanTemplate.Domain.Entities;
using CleanTemplate.SharedKernel.Errors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanTemplate.Application.Tests.Products.Commands;

public sealed class ProductCommandHandlersTests
{
    [Fact]
    public async Task CreateProductCommand_CreatesEntityAndReturnsId()
    {
        IProductWriteRepository repository = new FakeProductWriteRepository();
        var handler = new CreateProductCommandHandler(repository, NullLogger<CreateProductCommandHandler>.Instance);

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

        var persisted = await repository.GetByIdAsync(result.Value, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("Mouse", persisted.Name);
    }

    [Fact]
    public async Task UpdateProductCommand_WhenProductMissing_ReturnsNotFoundFailure()
    {
        IProductWriteRepository repository = new FakeProductWriteRepository();
        var handler = new UpdateProductCommandHandler(repository, NullLogger<UpdateProductCommandHandler>.Instance);

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
        IProductWriteRepository repository = new FakeProductWriteRepository();
        var product = new Domain.Entities.Product("Monitor", "4K", 300m, 3);
        repository.Add(product);
        await repository.SaveChangesAsync();

        var handler = new DeleteProductCommandHandler(repository, NullLogger<DeleteProductCommandHandler>.Instance);
        var command = new DeleteProductCommand { Id = product.Id };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var removed = await repository.GetByIdAsync(product.Id, CancellationToken.None);
        Assert.Null(removed);
    }

    private sealed class FakeProductWriteRepository : IProductWriteRepository
    {
        private readonly List<Product> _products = [];

        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_products.SingleOrDefault(product => product.Id == id));
        }

        public void Add(Product product)
        {
            _products.Add(product);
        }

        public void Remove(Product product)
        {
            _products.Remove(product);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }
}
