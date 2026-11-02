using CleanTemplate.Application.Contracts.Persistence;
using CleanTemplate.Application.Products.ReadModels;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Mediora;

namespace CleanTemplate.Application.Products.Queries.GetProductById;

public sealed class GetProductByIdQueryHandler(IProductQueries productQueries) : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly IProductQueries _productQueries = productQueries;

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        ProductReadModel? product = await _productQueries
            .GetByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false);

        return product is not null
            ? Result<ProductDto>.Success(new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                Version = product.Version
            })
            : Result<ProductDto>.Failure(Error.NotFound("Products.NotFound", $"Product '{request.Id}' was not found."));
    }
}
