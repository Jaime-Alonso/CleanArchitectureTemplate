using CleanTemplate.Application.Contracts.Persistence;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.RealTests;

[Trait("Category", "InfrastructureReal")]
public sealed class DatabaseRealTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public async Task TransactionsConcurrencyAndDapperUseRealDatabase(string provider)
    {
        await using var database = await RealDatabase.StartAsync(provider);
        var product = new Product("committed", null, 12.34m, 3);
        var rolledBack = new Product("rolled back", null, 1m, 1);
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var result = await uow.ExecuteInTransactionAsync(async ct =>
            {
                db.Set<Product>().Add(product);
                Assert.True(await uow.SaveChangesAsync(ct));
                return Result.Success();
            });
            Assert.True(result.IsSuccess);
        }
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var result = await uow.ExecuteInTransactionAsync(async ct =>
            {
                db.Set<Product>().Add(rolledBack);
                Assert.True(await uow.SaveChangesAsync(ct));
                return Result.Failure(Error.Failure("test", "rollback"));
            });
            Assert.True(result.IsFailure);
        }
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<IProductQueries>();
            var read = await queries.GetByIdAsync(product.Id);
            Assert.NotNull(read);
            Assert.Equal("committed", read.Name);
            Assert.Equal(12.34m, read.Price);
            Assert.Null(await queries.GetByIdAsync(rolledBack.Id));
        }
        await using var first = database.Services.CreateAsyncScope();
        await using var second = database.Services.CreateAsyncScope();
        var a = await first.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<Product>().SingleAsync(x => x.Id == product.Id);
        var b = await second.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<Product>().SingleAsync(x => x.Id == product.Id);
        a.Update("winner", null, 2m, 2);
        b.Update("stale", null, 3m, 3);
        Assert.True(await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
        Assert.False(await second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
        await using var verify = database.Services.CreateAsyncScope();
        var winner = await verify.ServiceProvider.GetRequiredService<IProductQueries>().GetByIdAsync(product.Id);
        Assert.Equal("winner", winner!.Name);
        Assert.Equal(2, winner.Version);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public async Task ConcurrentInstancesAreIsolatedEvenWithSameId(string provider)
    {
        // Each task owns its resource even if the other task fails to start.
        var id = Guid.NewGuid();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        await Task.WhenAll(Exercise("first"), Exercise("second"));

        async Task Exercise(string name)
        {
            try
            {
                await ExerciseCore(name);
            }
            catch
            {
                ready.TrySetCanceled();
                throw;
            }
        }

        async Task ExerciseCore(string name)
        {
            await using var database = await RealDatabase.StartAsync(provider);
            if (Interlocked.Increment(ref started) == 2)
                ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(2));
            await using var scope = database.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = new Product(name, null, 1m, 1);
            db.Entry(product).Property(x => x.Id).CurrentValue = id;
            db.Add(product);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
            var result = await scope.ServiceProvider.GetRequiredService<IProductQueries>().GetByIdAsync(id);
            Assert.Equal(name, result!.Name);
        }
    }
}
