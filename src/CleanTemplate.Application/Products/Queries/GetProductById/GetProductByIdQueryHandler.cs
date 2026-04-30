using CleanTemplate.Application.Abstractions;
using CleanTemplate.Core.SharedKernel.Errors;
using CleanTemplate.Core.SharedKernel.Results;
using MediatR;

namespace CleanTemplate.Application.Products.Queries.GetProductById;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly IProductReadRepository _productReadRepository;

    public GetProductByIdQueryHandler(IProductReadRepository productReadRepository)
    {
        _productReadRepository = productReadRepository;
    }

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _productReadRepository
            .GetByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false);

        return product is not null
            ? Result<ProductDto>.Success(product)
            : Result<ProductDto>.Failure(Error.NotFound("Products.NotFound", $"Product '{request.Id}' was not found."));
    }
}
