namespace CleanTemplate.Api.Endpoints.Contracts.Products;

public sealed record CreateProductRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
