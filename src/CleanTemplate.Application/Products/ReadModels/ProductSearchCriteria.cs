using CleanTemplate.Application.Common.Criteria;

namespace CleanTemplate.Application.Products.ReadModels;

public sealed record ProductSearchCriteria : PaginationCriteria
{
    public ProductSortingCriteria Sorting { get; init; } = new();
}
