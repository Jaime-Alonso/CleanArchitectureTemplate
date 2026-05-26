using CleanTemplate.Application.Common.Criteria;

namespace CleanTemplate.Application.Products.ReadModels;

public sealed record ProductSortingCriteria : SortingCriteria
{
    public const string DefaultSortBy = "id";

    public ProductSortingCriteria()
    {
        SortBy = DefaultSortBy;
    }

    public string NormalizedSortBy => NormalizeSortBy(SortBy);

    public static string NormalizeSortBy(string? sortBy)
    {
        return sortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => "name",
            "price" => "price",
            "stock" => "stock",
            _ => DefaultSortBy
        };
    }
}
