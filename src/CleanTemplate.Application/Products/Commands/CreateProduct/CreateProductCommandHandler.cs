using CleanTemplate.Application.Abstractions;
using CleanTemplate.Domain.Entities;
using CleanTemplate.SharedKernel.Results;
using Mediora;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.CreateProduct;

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductWriteRepository _productWriteRepository;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(IProductWriteRepository productWriteRepository, ILogger<CreateProductCommandHandler> logger)
    {
        _productWriteRepository = productWriteRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = new Product(
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
