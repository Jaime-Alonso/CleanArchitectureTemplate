using CleanTemplate.Infrastructure.Database;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class DatabaseProviderParserTests
{
    [Theory]
    [InlineData(null, DatabaseProvider.PostgreSql)]
    [InlineData("", DatabaseProvider.PostgreSql)]
    [InlineData("postgres", DatabaseProvider.PostgreSql)]
    [InlineData("postgresql", DatabaseProvider.PostgreSql)]
    [InlineData("sqlserver", DatabaseProvider.SqlServer)]
    [InlineData("mssql", DatabaseProvider.SqlServer)]
    [InlineData("sqlite", DatabaseProvider.SqliteInMemory)]
    [InlineData("sqlite-inmemory", DatabaseProvider.SqliteInMemory)]
    [InlineData("inmemory", DatabaseProvider.SqliteInMemory)]
    public void Parse_WithKnownAliases_ReturnsExpectedProvider(string? input, DatabaseProvider expected)
    {
        var result = DatabaseProviderParser.Parse(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Parse_WithUnsupportedProvider_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseProviderParser.Parse("oracle"));
    }
}
