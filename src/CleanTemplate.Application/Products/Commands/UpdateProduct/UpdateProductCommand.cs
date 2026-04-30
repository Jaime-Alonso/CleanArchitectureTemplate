using Mediora;
using CleanTemplate.Core.SharedKernel.Results;

namespace CleanTemplate.Application.Products.Commands.UpdateProduct;

public sealed record UpdateProductCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
