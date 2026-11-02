using CleanTemplate.Application.Contracts.Persistence;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class SqliteConnectionIsolationTests
{
    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=must-not-be-created.db")]
    public void Infrastructure_RejectsPrivateOrPersistentSqliteConnections(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "SqliteInMemory",
            ["ConnectionStrings:DefaultConnection"] = connectionString
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("named database", error.Message);
    }

    [Fact]
    public async Task SeparateContexts_HaveIndependentConnectionsToTheSameNamedMemoryDatabase()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "SqliteInMemory",
            ["ConnectionStrings:DefaultConnection"] = $"Data Source=isolation-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.NotSame(first.Database.GetDbConnection(), second.Database.GetDbConnection());
        await first.Database.EnsureCreatedAsync();
        Assert.Equal(0, await second.Users.CountAsync());
    }

    [Fact]
    public async Task ScopedUnitOfWorkSharesContextWithRepository()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "SqliteInMemory",
            ["ConnectionStrings:DefaultConnection"] = $"Data Source=uow-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Assert.Same(unit, scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        scope.ServiceProvider.GetRequiredService<IProductWriteRepository>().Add(new Product("scoped", null, 1m, 1));
        Assert.True(await unit.SaveChangesAsync());
        await using var verifyScope = provider.CreateAsyncScope();
        Assert.NotSame(unit, verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.Equal(1, await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<Product>().CountAsync());
    }
}
