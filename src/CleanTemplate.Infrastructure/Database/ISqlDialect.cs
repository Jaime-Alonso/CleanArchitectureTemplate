namespace CleanTemplate.Infrastructure.Database;

public interface ISqlDialect
{
    string Paginate(string sql, SqlSortField sortField, bool descending);
}
