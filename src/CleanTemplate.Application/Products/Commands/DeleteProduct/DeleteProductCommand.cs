using MediatR;
using CleanTemplate.Core.SharedKernel.Results;

namespace CleanTemplate.Application.Products.Commands.DeleteProduct;

public sealed record DeleteProductCommand : IRequest<Result>
{
    public Guid Id { get; init; }
}
