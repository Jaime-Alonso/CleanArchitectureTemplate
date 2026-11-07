using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CleanTemplate.Domain.Entities;

namespace CleanTemplate.Api.Endpoints.Contracts.Products;

public sealed record UpdateProductRequest
{
    [Required, Range(1L, long.MaxValue)]
    public long ExpectedVersion { get; init; }

    [StringLength(Product.NameMaxLength, MinimumLength = 1)]
    [Description("Must contain a non-whitespace character; length is measured in UTF-16 code units.")]
    public required string Name { get; init; }

    [MaxLength(Product.DescriptionMaxLength)]
    public string? Description { get; init; }

    public decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public int Stock { get; init; }
}
