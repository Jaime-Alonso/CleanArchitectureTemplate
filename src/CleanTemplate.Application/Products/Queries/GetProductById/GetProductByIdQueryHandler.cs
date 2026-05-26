using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Products.ReadModels;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Mediora;

namespace CleanTemplate.Application.Products.Queries.GetProductById;

public sealed class GetProductByIdQueryHandler(IProductReadRepository productReadRepository) : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly IProductReadRepository _productReadRepository = productReadRepository;

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        ProductReadModel? product = await _productReadRepository
            .GetByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false);

        return product is not null
            ? Result<ProductDto>.Success(new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock
            })
            : Result<ProductDto>.Failure(Error.NotFound("Products.NotFound", $"Product '{request.Id}' was not found."));
    }
}
