namespace CleanTemplate.Application.Products.ReadModels;

public sealed record ProductListItemReadModel
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
