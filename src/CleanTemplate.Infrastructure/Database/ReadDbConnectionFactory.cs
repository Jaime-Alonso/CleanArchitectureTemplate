using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CleanTemplate.Infrastructure.Database;

public sealed class ReadDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly DatabaseProvider _provider;

    public ReadDbConnectionFactory(string connectionString, DatabaseProvider provider)
    {
        _connectionString = connectionString;
        _provider = provider;
    }

    public DbConnection CreateConnection()
    {
        return _provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(_connectionString),
            DatabaseProvider.PostgreSql => new NpgsqlConnection(_connectionString),
            DatabaseProvider.SqliteInMemory => new SqliteConnection(_connectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider '{_provider}'.")
        };
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
