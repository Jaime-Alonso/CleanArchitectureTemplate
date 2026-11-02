using CleanTemplate.Application.Contracts;
using CleanTemplate.Domain.Entities;
using CleanTemplate.SharedKernel.Results;
using Mediora;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.CreateProduct;

public sealed class CreateProductCommandHandler(IProductWriteRepository productWriteRepository, IUnitOfWork unitOfWork, ILogger<CreateProductCommandHandler> logger) : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductWriteRepository _productWriteRepository = productWriteRepository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ILogger<CreateProductCommandHandler> _logger = logger;

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        Product product = new(
            request.Name,
            request.Description,
            request.Price,
            request.Stock);

        _productWriteRepository.Add(product);
        if (!await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Unexpected concurrency conflict while creating a product.");

        _logger.LogInformation("Product created successfully. ProductId {ProductId}", product.Id);

        return Result<Guid>.Success(product.Id);
    }
}
