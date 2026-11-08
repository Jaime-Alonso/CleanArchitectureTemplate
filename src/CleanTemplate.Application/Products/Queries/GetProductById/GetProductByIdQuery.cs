using CleanTemplate.SharedKernel.Results;
using Mediora;

namespace CleanTemplate.Application.Products.Queries.GetProductById;

public sealed record GetProductByIdQuery : IRequest<Result<ProductDto>>
{
    public Guid Id { get; init; }
}
