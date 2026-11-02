using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.Infrastructure.Tests.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Security;

public sealed class RefreshTokenRotationTests
{
    private static RefreshTokenService CreateService(ApplicationDbContext db) =>
        new(db, TimeProvider.System, Options.Create(new RefreshTokenOptions()));

    [Fact]
    public async Task ConcurrentRotationAcrossInstances_ProducesExactlyOneReplacement()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        var userId = Guid.NewGuid();
        await using var initialScope = fixture.First.CreateAsyncScope();
        var initial = CreateService(initialScope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var issued = await initial.IssueAsync(userId, "original", null);

        // Both requests pass the advisory lookup before competing for the conditional write.
        await using var firstScope = fixture.First.CreateAsyncScope();
        await using var secondScope = fixture.Second.CreateAsyncScope();
        var first = CreateService(firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var second = CreateService(secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        Assert.NotNull(await first.FindValidAsync(issued.RefreshToken, null));
        Assert.NotNull(await second.FindValidAsync(issued.RefreshToken, null));
        var results = await Task.WhenAll(
            Task.Run(() => first.RotateAsync(issued.RefreshToken, userId, "first", null)),
            Task.Run(() => second.RotateAsync(issued.RefreshToken, userId, "second", null)));

        var winner = Assert.Single(results, x => x is not null);
        Assert.Null(await initial.FindValidAsync(issued.RefreshToken, null));
        Assert.NotNull(await initial.FindValidAsync(winner!.RefreshToken, null));
        var tokens = await fixture.ReadAsync(db => db.Set<RefreshToken>().AsNoTracking().ToListAsync());
        Assert.Equal(2, tokens.Count);
        var consumed = Assert.Single(tokens, x => x.RevokedAtUtc != null);
        Assert.Equal(Assert.Single(tokens, x => x.RevokedAtUtc == null).TokenHash, consumed.ReplacedByTokenHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReplacement_RollsBackConsumption(bool cancel)
    {
        var interceptor = new FailReplacementInterceptor(cancel);
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync(interceptor);
        await using var scope = fixture.First.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = CreateService(db);
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        interceptor.Enabled = true;

        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));

        Assert.DoesNotContain(db.ChangeTracker.Entries<RefreshToken>(), x => x.State == EntityState.Added);
        var tokens = await fixture.ReadAsync(context => context.Set<RefreshToken>().AsNoTracking().ToListAsync());
        var original = Assert.Single(tokens);
        Assert.Null(original.RevokedAtUtc);
        Assert.Null(original.ReplacedByTokenHash);
        Assert.NotNull(await service.FindValidAsync(issued.RefreshToken, null));
        interceptor.Enabled = false;
        Assert.NotNull(await service.RotateAsync(issued.RefreshToken, userId, "retry", null));
    }

    [Fact]
    public async Task InvalidWrongUserAndRevokedTokens_DoNotCreateReplacement()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var scope = fixture.First.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        Assert.Null(await service.RotateAsync("unknown", userId, "replacement", null));
        Assert.Null(await service.RotateAsync(issued.RefreshToken, Guid.NewGuid(), "replacement", null));
        Assert.NotNull(await service.FindValidAsync(issued.RefreshToken, null));
        Assert.True(await service.RevokeAsync(issued.RefreshToken, null));
        Assert.Null(await service.RotateAsync(issued.RefreshToken, userId, "replacement", null));
        Assert.Equal(1, await fixture.ReadAsync(db => db.Set<RefreshToken>().CountAsync()));
    }

    [Fact]
    public async Task ExpiredToken_CannotRotate()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var scope = fixture.First.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = CreateService(db);
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        await db.Set<RefreshToken>().ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Null(await service.FindValidAsync(issued.RefreshToken, null));
        Assert.Null(await service.RotateAsync(issued.RefreshToken, userId, "replacement", null));
        Assert.Equal(1, await fixture.ReadAsync(context => context.Set<RefreshToken>().CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCommit_RollsBackBothPersistedReplacementAndConsumption(bool cancel)
    {
        var interceptor = new FailCommitInterceptor(cancel);
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync(interceptor);
        await using var scope = fixture.First.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = CreateService(db);
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        interceptor.Enabled = true;

        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));

        var original = Assert.Single(await fixture.ReadAsync(context => context.Set<RefreshToken>().AsNoTracking().ToListAsync()));
        Assert.Null(original.RevokedAtUtc);
        Assert.Null(original.ReplacedByTokenHash);
        Assert.DoesNotContain(db.ChangeTracker.Entries<RefreshToken>(), entry => entry.Entity.JwtId == "replacement");
        interceptor.Enabled = false;
        Assert.NotNull(await service.RotateAsync(issued.RefreshToken, userId, "retry", null));
    }

    [Fact]
    public async Task SuccessfulRotation_PersistsOnlyHashesAndRejectsReplay()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var scope = fixture.First.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", "192.0.2.1");

        var replacement = await service.RotateAsync(issued.RefreshToken, userId, "new-jwt", "192.0.2.2");

        Assert.NotNull(replacement);
        Assert.Null(await service.RotateAsync(issued.RefreshToken, userId, "replay", null));
        var tokens = await fixture.ReadAsync(db => db.Set<RefreshToken>().AsNoTracking().ToListAsync());
        Assert.Equal(2, tokens.Count);
        var consumed = Assert.Single(tokens, token => token.JwtId == "original");
        var active = Assert.Single(tokens, token => token.JwtId == "new-jwt");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replacement.RefreshToken))), active.TokenHash);
        Assert.Equal(active.TokenHash, consumed.ReplacedByTokenHash);
        Assert.Equal("192.0.2.2", consumed.RevokedByIp);
        Assert.Equal("192.0.2.2", active.CreatedByIp);
        // PostgreSQL timestamps have microsecond precision; the issued value is a .NET timestamp.
        Assert.InRange(Math.Abs((replacement.ExpiresAtUtc - active.ExpiresAtUtc).Ticks), 0, 9);
        Assert.NotNull(consumed.RevokedAtUtc);
        Assert.Null(active.RevokedAtUtc);
    }

    [Fact]
    public async Task RotationInsideExistingTransaction_IsRejectedWithoutConsumingToken()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var scope = fixture.First.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = CreateService(db);
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));
            await transaction.RollbackAsync();
        }

        Assert.NotNull(await service.FindValidAsync(issued.RefreshToken, null));
        Assert.Equal(1, await fixture.ReadAsync(context => context.Set<RefreshToken>().CountAsync()));
    }

    [Fact]
    public async Task RotationInsideAmbientTransaction_IsRejectedWithoutConsumingToken()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var scope = fixture.First.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        using (var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));
            Assert.Contains("requires its own transaction", exception.Message);
        }

        Assert.NotNull(await service.FindValidAsync(issued.RefreshToken, null));
    }

    [Fact]
    public async Task CanceledRotation_LeavesOriginalUsable()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var scope = fixture.First.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null, cancellation.Token));

        Assert.NotNull(await service.FindValidAsync(issued.RefreshToken, null));
        Assert.Equal(1, await fixture.ReadAsync(context => context.Set<RefreshToken>().CountAsync()));
    }

    [Fact]
    public async Task ReplacementViolatingUniqueConstraint_RollsBackConsumption()
    {
        var interceptor = new DuplicateHashInterceptor();
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync(interceptor);
        await using var scope = fixture.First.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        interceptor.DuplicateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issued.RefreshToken)));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));

        var original = Assert.Single(await fixture.ReadAsync(db => db.Set<RefreshToken>().AsNoTracking().ToListAsync()));
        Assert.Null(original.RevokedAtUtc);
        Assert.Null(original.ReplacedByTokenHash);
        interceptor.DuplicateHash = null;
        Assert.NotNull(await service.RotateAsync(issued.RefreshToken, userId, "retry", null));
    }

    [Fact]
    public async Task LogoutRacingRotation_DoesNotLeavePartialReplacement()
    {
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var initialScope = fixture.First.CreateAsyncScope();
        var initial = CreateService(initialScope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await initial.IssueAsync(userId, "original", null);
        await using var rotationScope = fixture.First.CreateAsyncScope();
        await using var logoutScope = fixture.Second.CreateAsyncScope();
        var rotation = CreateService(rotationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var logout = CreateService(logoutScope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        Assert.NotNull(await rotation.FindValidAsync(issued.RefreshToken, null));

        var rotateTask = Task.Run(() => rotation.RotateAsync(issued.RefreshToken, userId, "replacement", null));
        var logoutTask = Task.Run(() => logout.RevokeAsync(issued.RefreshToken, null));
        await Task.WhenAll(rotateTask, logoutTask);

        Assert.True(await logoutTask);
        var replacement = await rotateTask;
        var tokens = await fixture.ReadAsync(db => db.Set<RefreshToken>().AsNoTracking().ToListAsync());
        var original = Assert.Single(tokens, token => token.JwtId == "original");
        Assert.NotNull(original.RevokedAtUtc);
        if (replacement is null)
        {
            Assert.Single(tokens);
            Assert.Null(original.ReplacedByTokenHash);
        }
        else
        {
            Assert.Equal(2, tokens.Count);
            Assert.Equal(Assert.Single(tokens, token => token.JwtId == "replacement").TokenHash, original.ReplacedByTokenHash);
            // Logout of the original does not revoke descendants until T-07.
            Assert.NotNull(await initial.FindValidAsync(replacement.RefreshToken, null));
        }
    }

    [Fact]
    public async Task LostCommitAcknowledgment_IsNotRetriedAndOriginalRemainsConsumed()
    {
        var interceptor = new LostCommitAcknowledgmentInterceptor();
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync(interceptor);
        await using var scope = fixture.First.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        interceptor.Enabled = true;

        await Assert.ThrowsAsync<IOException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));

        interceptor.Enabled = false;
        var tokens = await fixture.ReadAsync(db => db.Set<RefreshToken>().AsNoTracking().ToListAsync());
        Assert.Equal(2, tokens.Count);
        var original = Assert.Single(tokens, token => token.JwtId == "original");
        Assert.NotNull(original.RevokedAtUtc);
        Assert.Equal(Assert.Single(tokens, token => token.JwtId == "replacement").TokenHash, original.ReplacedByTokenHash);
        Assert.Null(await service.RotateAsync(issued.RefreshToken, userId, "retry", null));
    }

    [Fact]
    public async Task ConnectionClosedBeforeCommit_RollsBackBothChanges()
    {
        var interceptor = new CloseConnectionInterceptor();
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync(interceptor);
        await using var scope = fixture.First.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var userId = Guid.NewGuid();
        var issued = await service.IssueAsync(userId, "original", null);
        interceptor.Enabled = true;

        await Assert.ThrowsAsync<IOException>(() => service.RotateAsync(issued.RefreshToken, userId, "replacement", null));

        interceptor.Enabled = false;
        var original = Assert.Single(await fixture.ReadAsync(db => db.Set<RefreshToken>().AsNoTracking().ToListAsync()));
        Assert.Null(original.RevokedAtUtc);
        Assert.Null(original.ReplacedByTokenHash);
        Assert.NotNull(await service.RotateAsync(issued.RefreshToken, userId, "retry", null));
    }

    private sealed class CloseConnectionInterceptor : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                await transaction.Connection!.CloseAsync();
                throw new IOException("Connection closed after insertion, before commit.");
            }
            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class DuplicateHashInterceptor : SaveChangesInterceptor
    {
        public string? DuplicateHash { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (DuplicateHash is not null)
            {
                var replacement = eventData.Context!.ChangeTracker.Entries<RefreshToken>().Single(entry => entry.State == EntityState.Added);
                replacement.Property(token => token.TokenHash).CurrentValue = DuplicateHash;
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class LostCommitAcknowledgmentInterceptor : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Enabled)
                throw new IOException("Simulated acknowledgment loss after the real database commit.");

            return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        }
    }

    private sealed class FailCommitInterceptor(bool cancel) : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                if (cancel)
                    throw new OperationCanceledException("Injected cancellation after persistence, before commit.");
                throw new InvalidOperationException("Injected failure after persistence, before commit.");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class FailReplacementInterceptor(bool cancel) : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                if (cancel)
                    throw new OperationCanceledException("Injected cancellation before replacement persistence.");
                throw new InvalidOperationException("Injected failure before replacement persistence.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
