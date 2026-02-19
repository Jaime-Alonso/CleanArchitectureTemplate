using MediatR;
using CleanTemplate.SharedKernel.Results;

namespace CleanTemplate.Application.Products.Queries.GetProductById;

public sealed record GetProductByIdQuery : IRequest<Result<ProductDto>>
{
    public Guid Id { get; init; }
}
