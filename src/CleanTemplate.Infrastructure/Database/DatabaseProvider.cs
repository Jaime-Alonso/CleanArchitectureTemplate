using System;

namespace CleanTemplate.Infrastructure.Database;

public enum DatabaseProvider
{
    PostgreSql,
    SqlServer,
    SqliteInMemory
}

public static class DatabaseProviderParser
{
    public static DatabaseProvider Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DatabaseProvider.PostgreSql;

        return value.Trim().ToLowerInvariant() switch
        {
            "postgres" => DatabaseProvider.PostgreSql,
            "postgresql" => DatabaseProvider.PostgreSql,
            "sqlserver" => DatabaseProvider.SqlServer,
            "mssql" => DatabaseProvider.SqlServer,
            "sqlite" => DatabaseProvider.SqliteInMemory,
            "sqliteinmemory" => DatabaseProvider.SqliteInMemory,
            "sqlite-inmemory" => DatabaseProvider.SqliteInMemory,
            "inmemory" => DatabaseProvider.SqliteInMemory,
            _ => throw new InvalidOperationException($"Unsupported database provider '{value}'.")
        };
    }
}
