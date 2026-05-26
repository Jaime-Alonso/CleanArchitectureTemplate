using CleanTemplate.Application.Contracts;
using CleanTemplate.Domain.Entities;
using CleanTemplate.SharedKernel.Results;
using Mediora;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.CreateProduct;

public sealed class CreateProductCommandHandler(IProductWriteRepository productWriteRepository, ILogger<CreateProductCommandHandler> logger) : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductWriteRepository _productWriteRepository = productWriteRepository;
    private readonly ILogger<CreateProductCommandHandler> _logger = logger;

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        Product product = new(
            request.Name,
            request.Description,
            request.Price,
            request.Stock);

        _productWriteRepository.Add(product);
        await _productWriteRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Product created successfully. ProductId {ProductId}", product.Id);

        return Result<Guid>.Success(product.Id);
    }
}
