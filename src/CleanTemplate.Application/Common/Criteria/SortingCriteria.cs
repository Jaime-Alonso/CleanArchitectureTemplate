namespace CleanTemplate.Application.Common.Criteria;

public abstract record SortingCriteria
{
    public const string DefaultSortDirection = "asc";

    public string SortBy { get; init; } = string.Empty;
    public string SortDirection { get; init; } = DefaultSortDirection;

    public string NormalizedSortDirection => NormalizeSortDirection(SortDirection);

    public static string NormalizeSortDirection(string? sortDirection)
    {
        return sortDirection?.Trim().ToLowerInvariant() == "desc"
            ? "desc"
            : DefaultSortDirection;
    }
}
