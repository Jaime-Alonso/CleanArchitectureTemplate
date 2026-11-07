using System.Security.Cryptography;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Persistence;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace CleanTemplate.Infrastructure.RealTests;

internal sealed class RealDatabase : IAsyncDisposable
{
    private readonly IContainer _container;
    private string _connectionString = null!;
    private string _provider = null!;
    public ServiceProvider Services { get; private set; } = null!;

    internal Dictionary<string, string?> CreateConfiguration() => new()
    {
        ["DatabaseProvider"] = _provider,
        ["ConnectionStrings:DefaultConnection"] = _connectionString
    };

    private RealDatabase(IContainer container) => _container = container;

    public static async Task<RealDatabase> StartAsync(string provider)
    {
        if (provider is not ("SqlServer" or "PostgreSql"))
            throw new ArgumentOutOfRangeException(nameof(provider));
        var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var database = "test_" + Guid.NewGuid().ToString("N");
        IContainer container = provider == "SqlServer"
            ? new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").WithPassword(password).Build()
            : new PostgreSqlBuilder("postgres:17-alpine").WithDatabase(database).WithPassword(password).Build();
        var instance = new RealDatabase(container);
        try
        {
            await container.StartAsync();
            string connectionString;
            if (container is MsSqlContainer sql)
            {
                await using var connection = new SqlConnection(sql.GetConnectionString());
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{database}]";
                await command.ExecuteNonQueryAsync();
                connectionString = new SqlConnectionStringBuilder(sql.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
            }
            else
            {
                connectionString = ((PostgreSqlContainer)container).GetConnectionString();
            }

            instance._provider = provider;
            instance._connectionString = connectionString;
            var config = new ConfigurationBuilder().AddInMemoryCollection(instance.CreateConfiguration()).Build();
            instance.Services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System).AddInfrastructure(config).BuildServiceProvider();
            await using var scope = instance.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            return instance;
        }
        catch
        {
            await instance.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Services is not null)
                await Services.DisposeAsync();
        }
        finally
        {
            await _container.DisposeAsync();
        }
    }
}
