using CleanTemplate.Application.Contracts;
using CleanTemplate.Domain.Entities;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Mediora;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.DeleteProduct;

public sealed class DeleteProductCommandHandler(IProductWriteRepository productWriteRepository, ILogger<DeleteProductCommandHandler> logger) : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IProductWriteRepository _productWriteRepository = productWriteRepository;
    private readonly ILogger<DeleteProductCommandHandler> _logger = logger;

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        Product? product = await _productWriteRepository
            .GetByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            _logger.LogInformation("Product deletion failed. ProductId {ProductId} not found", request.Id);
            return Result.Failure(Error.NotFound("Products.NotFound", $"Product '{request.Id}' was not found."));
        }

        _productWriteRepository.Remove(product);
        await _productWriteRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Product deleted successfully. ProductId {ProductId}", request.Id);

        return Result.Success();
    }
}
