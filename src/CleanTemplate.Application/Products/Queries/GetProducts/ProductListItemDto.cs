namespace CleanTemplate.Application.Products.Queries.GetProducts;

public sealed record ProductListItemDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
