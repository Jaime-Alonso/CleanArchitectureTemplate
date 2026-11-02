using CleanTemplate.Application.Contracts.Persistence;
using CleanTemplate.Domain.Entities;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Mediora;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.UpdateProduct;

public sealed class UpdateProductCommandHandler(IProductWriteRepository productWriteRepository, IUnitOfWork unitOfWork, ILogger<UpdateProductCommandHandler> logger) : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductWriteRepository _productWriteRepository = productWriteRepository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ILogger<UpdateProductCommandHandler> _logger = logger;

    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        Product? product = await _productWriteRepository
            .GetByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            _logger.LogInformation("Product update failed. ProductId {ProductId} not found", request.Id);
            return Result.Failure(Error.NotFound("Products.NotFound", $"Product '{request.Id}' was not found."));
        }

        if (product.Version != request.ExpectedVersion)
            return Result.Failure(new Error("Products.VersionConflict", "The product version is stale.", ErrorType.Conflict));

        product.Update(request.Name, request.Description, request.Price, request.Stock);

        if (!await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
            return Result.Failure(new Error("Products.VersionConflict", "The product version is stale.", ErrorType.Conflict));

        _logger.LogInformation("Product updated successfully. ProductId {ProductId}", request.Id);

        return Result.Success();
    }
}
