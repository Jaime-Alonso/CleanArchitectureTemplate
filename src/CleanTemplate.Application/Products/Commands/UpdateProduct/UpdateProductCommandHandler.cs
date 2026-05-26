using CleanTemplate.Application.Abstractions;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Mediora;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.UpdateProduct;

public sealed class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductWriteRepository _productWriteRepository;
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(IProductWriteRepository productWriteRepository, ILogger<UpdateProductCommandHandler> logger)
    {
        _productWriteRepository = productWriteRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _productWriteRepository
            .GetByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            _logger.LogInformation("Product update failed. ProductId {ProductId} not found", request.Id);
            return Result.Failure(Error.NotFound("Products.NotFound", $"Product '{request.Id}' was not found."));
        }

        product.Update(request.Name, request.Description, request.Price, request.Stock);

        await _productWriteRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Product updated successfully. ProductId {ProductId}", request.Id);

        return Result.Success();
    }
}
