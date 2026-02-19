namespace CleanTemplate.Application.Products.Queries.GetProductById;

public sealed record ProductDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
