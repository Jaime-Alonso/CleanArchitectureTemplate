using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Products.Queries.GetProductById;
using CleanTemplate.Application.Products.Queries.GetProducts;
using CleanTemplate.Infrastructure.Database;
using Dapper;

namespace CleanTemplate.Infrastructure.Repositories.Reads;

public sealed class ProductReadRepository : IProductReadRepository
{
    static ProductReadRepository()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
    }

    private readonly ReadDbConnectionFactory _connectionFactory;
    private readonly ISqlDialect _sqlDialect;

    public ProductReadRepository(ReadDbConnectionFactory connectionFactory, ISqlDialect sqlDialect)
    {
        _connectionFactory = connectionFactory;
        _sqlDialect = sqlDialect;
    }

    public async Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT "Id", "Name", "Description", "Price", "Stock"
            FROM "Products"
            WHERE "Id" = @Id
            """;

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var command = new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken);

        var row = await connection
            .QueryFirstOrDefaultAsync<ProductRow>(command)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new ProductDto
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            Price = row.Price,
            Stock = row.Stock
        };
    }

    public async Task<IReadOnlyList<ProductListItemDto>> GetPagedAsync(
        int page,
        int pageSize,
        string sortBy,
        string sortDirection,
        CancellationToken cancellationToken = default)
    {
        var sortField = MapSortField(sortBy);
        var isDescending = sortDirection == "desc";
        var offset = (page - 1) * pageSize;

        var sql = _sqlDialect.Paginate(
            """
            SELECT "Id", "Name", "Price", "Stock"
            FROM "Products"
            """,
            sortField,
            isDescending);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var command = new CommandDefinition(
            sql,
            new { Offset = offset, PageSize = pageSize },
            cancellationToken: cancellationToken);
        var result = await connection
            .QueryAsync<ProductListItemRow>(command)
            .ConfigureAwait(false);

        return result
            .Select(row => new ProductListItemDto
            {
                Id = row.Id,
                Name = row.Name,
                Price = row.Price,
                Stock = row.Stock
            })
            .ToList();
    }

    private sealed record ProductRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public decimal Price { get; init; }
        public int Stock { get; init; }
    }

    private sealed record ProductListItemRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public int Stock { get; init; }
    }

    private sealed class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value;
        }

        public override Guid Parse(object value)
        {
            return value switch
            {
                Guid guid => guid,
                string text => Guid.Parse(text),
                byte[] bytes when bytes.Length == 16 => new Guid(bytes),
                _ => throw new DataException($"Cannot convert '{value}' ({value.GetType().Name}) to Guid.")
            };
        }
    }

    private static SqlSortField MapSortField(string sortBy)
    {
        return sortBy switch
        {
            "name" => SqlSortField.Name,
            "price" => SqlSortField.Price,
            "stock" => SqlSortField.Stock,
            _ => SqlSortField.Id
        };
    }
}
