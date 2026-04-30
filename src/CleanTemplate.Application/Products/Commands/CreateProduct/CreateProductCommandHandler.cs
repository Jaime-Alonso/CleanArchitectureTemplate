using CleanTemplate.Application.Abstractions;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Core.SharedKernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Products.Commands.CreateProduct;

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(IApplicationDbContext context, ILogger<CreateProductCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = new Product(
            request.Name,
            request.Description,
            request.Price,
            request.Stock);

        _context.Add(product);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Product created successfully. ProductId {ProductId}", product.Id);

        return Result<Guid>.Success(product.Id);
    }
}
