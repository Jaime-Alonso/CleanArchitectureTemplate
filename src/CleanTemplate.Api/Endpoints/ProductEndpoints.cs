using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using CleanTemplate.Api.Endpoints.Contracts.Products;
using CleanTemplate.Api.Extensions;

namespace CleanTemplate.Api.Endpoints;

public static class ProductEndpoints
{
    public static WebApplication MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/products");
        group.RequireAuthorization("UserOrAdmin");

        group.MapPost("/", CreateProduct)
            .RequireAuthorization("AdminOnly")
            .WithName("CreateProduct");

        group.MapPut("/{id:guid}", UpdateProduct)
            .RequireAuthorization("AdminOnly")
            .WithName("UpdateProduct");

        group.MapDelete("/{id:guid}", DeleteProduct)
            .RequireAuthorization("AdminOnly")
            .WithName("DeleteProduct");

        group.MapGet("/{id:guid}", GetProductById)
            .WithName("GetProductById");

        group.MapGet("/", GetProducts)
            .WithName("GetProducts");

        return app;
    }

    private static async Task<IResult> CreateProduct(
        CreateProductRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new Application.Products.Commands.CreateProduct.CreateProductCommand
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Stock = request.Stock
        };

        var productId = await sender.Send(command, cancellationToken);

        if (productId.IsFailure)
        {
            return productId.ToHttpErrorResult();
        }

        return Results.Created($"/api/products/{productId.Value}", new { Id = productId.Value });
    }

    private static async Task<IResult> UpdateProduct(
        Guid id,
        UpdateProductRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new Application.Products.Commands.UpdateProduct.UpdateProductCommand
        {
            Id = id,
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Stock = request.Stock
        };

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess ? Results.NoContent() : result.ToHttpErrorResult();
    }

    private static async Task<IResult> DeleteProduct(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new Application.Products.Commands.DeleteProduct.DeleteProductCommand
        {
            Id = id
        };

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess ? Results.NoContent() : result.ToHttpErrorResult();
    }

    private static async Task<IResult> GetProductById(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var query = new Application.Products.Queries.GetProductById.GetProductByIdQuery
        {
            Id = id
        };

        var product = await sender.Send(query, cancellationToken);

        return product.IsSuccess ? Results.Ok(product.Value) : product.ToHttpErrorResult();
    }

    private static async Task<IResult> GetProducts(
        int? page,
        int? pageSize,
        string? sortBy,
        string? sortDirection,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var query = new Application.Products.Queries.GetProducts.GetProductsQuery
        {
            Page = page ?? Application.Products.Queries.GetProducts.GetProductsQuery.DefaultPage,
            PageSize = pageSize ?? Application.Products.Queries.GetProducts.GetProductsQuery.DefaultPageSize,
            SortBy = sortBy ?? Application.Products.Queries.GetProducts.GetProductsQuery.DefaultSortBy,
            SortDirection = sortDirection ?? Application.Products.Queries.GetProducts.GetProductsQuery.DefaultSortDirection
        };

        var products = await sender.Send(query, cancellationToken);

        return Results.Ok(products);
    }
}
