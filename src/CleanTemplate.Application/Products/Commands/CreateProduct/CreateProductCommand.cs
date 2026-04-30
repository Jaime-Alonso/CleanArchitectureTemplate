using MediatR;
using CleanTemplate.Core.SharedKernel.Results;

namespace CleanTemplate.Application.Products.Commands.CreateProduct;

public sealed record CreateProductCommand : IRequest<Result<Guid>>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
