using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Products.Queries.GetProductById;
using CleanTemplate.Application.Products.Queries.GetProducts;

namespace CleanTemplate.Application.Tests.Products.Queries;

public sealed class ProductQueryHandlersTests
{
    [Fact]
    public async Task GetProductsQuery_ReturnsRepositoryPage()
    {
        var expected = new List<ProductListItemDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Keyboard", Price = 20m, Stock = 10 },
            new() { Id = Guid.NewGuid(), Name = "Mouse", Price = 15m, Stock = 12 }
        };

        var repository = new FakeProductReadRepository
        {
            PagedProducts = expected
        };

        var handler = new GetProductsQueryHandler(repository);

        var result = await handler.Handle(new GetProductsQuery { Page = 2, PageSize = 5 }, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(2, repository.LastPage);
        Assert.Equal(5, repository.LastPageSize);
        Assert.Equal("id", repository.LastSortBy);
        Assert.Equal("asc", repository.LastSortDirection);
    }

    [Fact]
    public async Task GetProductsQuery_WhenPageSizeExceedsMaximum_ClampsToMaxPageSize()
    {
        var repository = new FakeProductReadRepository();
        var handler = new GetProductsQueryHandler(repository);

        _ = await handler.Handle(
            new GetProductsQuery { Page = 1, PageSize = 10_000 },
            CancellationToken.None);

        Assert.Equal(GetProductsQuery.MaxPageSize, repository.LastPageSize);
    }

    [Fact]
    public async Task GetProductsQuery_WhenSortIsProvided_NormalizesSortParameters()
    {
        var repository = new FakeProductReadRepository();
        var handler = new GetProductsQueryHandler(repository);

        _ = await handler.Handle(
            new GetProductsQuery { SortBy = "NAME", SortDirection = "DESC" },
            CancellationToken.None);

        Assert.Equal("name", repository.LastSortBy);
        Assert.Equal("desc", repository.LastSortDirection);
    }

    [Fact]
    public async Task GetProductByIdQuery_ReturnsRepositoryItem()
    {
        var productId = Guid.NewGuid();
        var expected = new ProductDto
        {
            Id = productId,
            Name = "Monitor",
            Description = "4K",
            Price = 350m,
            Stock = 4
        };

        var repository = new FakeProductReadRepository
        {
            Product = expected
        };

        var handler = new GetProductByIdQueryHandler(repository);

        var result = await handler.Handle(new GetProductByIdQuery { Id = productId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected.Id, result.Value.Id);
        Assert.Equal(productId, repository.LastRequestedId);
    }

    private sealed class FakeProductReadRepository : IProductReadRepository
    {
        public ProductDto? Product { get; set; }
        public IReadOnlyList<ProductListItemDto> PagedProducts { get; set; } = [];
        public Guid LastRequestedId { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public string LastSortBy { get; private set; } = string.Empty;
        public string LastSortDirection { get; private set; } = string.Empty;

        public Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastRequestedId = id;
            return Task.FromResult(Product);
        }

        public Task<IReadOnlyList<ProductListItemDto>> GetPagedAsync(
            int page,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default)
        {
            LastPage = page;
            LastPageSize = pageSize;
            LastSortBy = sortBy;
            LastSortDirection = sortDirection;
            return Task.FromResult(PagedProducts);
        }
    }
}
