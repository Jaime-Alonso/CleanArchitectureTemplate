using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.RealTests;

[Trait("Category", "InfrastructureReal")]
public sealed class RefreshRotationRealTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public async Task ConcurrentRotationHasOneWinnerAndReplayRevokesFamily(string provider)
    {
        await using var database = await RealDatabase.StartAsync(provider);
        var userId = Guid.NewGuid();
        string original;
        await using (var seed = database.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(new ApplicationUser { Id = userId, UserName = "rotation", SecurityStamp = Guid.NewGuid().ToString() });
            await db.SaveChangesAsync();
            original = (await seed.ServiceProvider.GetRequiredService<IRefreshTokenService>().IssueAsync(userId, "initial", null)).RefreshToken;
        }
        async Task<string?> Rotate()
        {
            await using var scope = database.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>().RotateAsync(original, userId, Guid.NewGuid().ToString(), null))?.RefreshToken;
        }
        var results = await Task.WhenAll(Rotate(), Rotate());
        Assert.True(results.Count(x => x is not null) == 1);
        await using var verify = database.Services.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await context.Set<RefreshToken>().CountAsync() == 2);
        Assert.True(await context.Set<RefreshTokenFamily>().AnyAsync(x => x.UserId == userId && x.RevokedAtUtc != null && x.RevocationReason == "ReuseDetected"));
        Assert.True(await verify.ServiceProvider.GetRequiredService<IRefreshTokenService>().FindValidAsync(results.Single(x => x is not null)!, null) is null);
    }
}
