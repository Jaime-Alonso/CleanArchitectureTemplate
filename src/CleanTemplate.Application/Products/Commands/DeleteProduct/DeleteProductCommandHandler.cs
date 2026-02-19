using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Products;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.DeleteProduct;

public sealed class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<DeleteProductCommandHandler> _logger;

    public DeleteProductCommandHandler(IApplicationDbContext context, ILogger<DeleteProductCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context
            .FindByIdAsync(request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            _logger.LogInformation("Product deletion failed. ProductId {ProductId} not found", request.Id);
            return Result.Failure(Error.NotFound("Products.NotFound", $"Product '{request.Id}' was not found."));
        }

        _context.Remove(product);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Product deleted successfully. ProductId {ProductId}", request.Id);

        return Result.Success();
    }
}
