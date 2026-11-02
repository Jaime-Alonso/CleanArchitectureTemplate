using CleanTemplate.Infrastructure.Database;
using CleanTemplate.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class ProductReadConfigurationTests
{
    private const string Primary = "Host=primary;Database=test;Username=test;Password=test";
    private const string Replica = "Host=replica;Database=test;Username=test;Password=test";

    [Fact]
    public void DefaultAndMatchingLegacyUsePrimary()
    {
        using var services = Build(new() { ["ConnectionStrings:ReadConnection"] = Primary });
        using var connection = services.GetRequiredService<ReadDbConnectionFactory>().CreateConnection();
        Assert.Equal("primary", new Npgsql.NpgsqlConnectionStringBuilder(connection.ConnectionString).Host);
    }

    [Fact]
    public void ExplicitReplicaUsesReplica()
    {
        using var services = Build(new() { ["ProductReads:Mode"] = "Replica", ["ConnectionStrings:ProductReadReplica"] = Replica });
        using var connection = services.GetRequiredService<ReadDbConnectionFactory>().CreateConnection();
        Assert.Equal("replica", new Npgsql.NpgsqlConnectionStringBuilder(connection.ConnectionString).Host);
    }

    [Theory]
    [InlineData("ConnectionStrings:ReadConnection", Replica)]
    [InlineData("ConnectionStrings:DapperConnection", Replica)]
    [InlineData("ConnectionStrings:ProductReadReplica", Replica)]
    [InlineData("ProductReads:Mode", "Replica")]
    [InlineData("ProductReads:Mode", "invalid")]
    public void IncompatibleConfigurationFails(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => Build(new() { [key] = value }));
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        values["DatabaseProvider"] = "PostgreSql";
        values["ConnectionStrings:DefaultConnection"] = Primary;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }
}
