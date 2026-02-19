namespace CleanTemplate.Infrastructure.Database;

public sealed class SqliteDialect : ISqlDialect
{
    public string Paginate(string sql, SqlSortField sortField, bool descending)
    {
        var orderByClause = BuildOrderByClause(sortField, descending);
        return $"{sql} ORDER BY {orderByClause} LIMIT @PageSize OFFSET @Offset";
    }

    private static string BuildOrderByClause(SqlSortField sortField, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        var primary = sortField switch
        {
            SqlSortField.Name => $"\"Name\" {direction}",
            SqlSortField.Price => $"\"Price\" {direction}",
            SqlSortField.Stock => $"\"Stock\" {direction}",
            _ => $"\"Id\" {direction}"
        };

        return sortField == SqlSortField.Id
            ? primary
            : $"{primary}, \"Id\" ASC";
    }
}
