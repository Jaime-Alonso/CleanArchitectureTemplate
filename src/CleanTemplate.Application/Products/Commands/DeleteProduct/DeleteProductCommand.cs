using CleanTemplate.SharedKernel.Results;
using Mediora;

namespace CleanTemplate.Application.Products.Commands.DeleteProduct;

public sealed record DeleteProductCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public long ExpectedVersion { get; init; }
}
