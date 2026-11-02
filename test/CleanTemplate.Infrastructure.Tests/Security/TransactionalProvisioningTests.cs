using System.Collections.Concurrent;
using System.Data.Common;
using CleanTemplate.Application.Security;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.Infrastructure.Tests.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CleanTemplate.Infrastructure.Tests.Security;

public sealed class TransactionalProvisioningTests
{
    [Fact]
    public async Task ConcurrentFirstAccessAcrossInstances_CreatesOneCompleteIdentity()
    {
        var gate = new ConcurrentTransactionGate();
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(gate);
        gate.Armed = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var identity = Identity();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(() =>
            fixture.ProvisionAsync(index % 2 == 0 ? fixture.First : fixture.Second, identity, cancellation.Token))));

        Assert.True(gate.Starts >= 2);
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results.Select(result => result.Value.InternalUserId).Distinct());
        Assert.Equal(1, await fixture.ReadAsync(context => context.Users.CountAsync()));
        Assert.Equal(1, await fixture.ReadAsync(context => context.UserLogins.CountAsync()));
        Assert.Equal(1, await fixture.ReadAsync(context => context.Set<ExternalRoleGrant>().CountAsync()));
    }

    [Fact]
    public async Task ConcurrentDifferentSubjectsWithTheSameNormalizedEmail_AreNeverLinked()
    {
        var gate = new ConcurrentTransactionGate();
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(gate);
        gate.Armed = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var first = Identity();
        var second = first with { ProviderSubject = "different-subject", Email = "RACE@EXAMPLE.TEST" };

        var results = await Task.WhenAll(
            Task.Run(() => fixture.ProvisionAsync(fixture.First, first, cancellation.Token)),
            Task.Run(() => fixture.ProvisionAsync(fixture.Second, second, cancellation.Token)));

        Assert.Single(results, result => result.IsSuccess);
        var rejected = Assert.Single(results, result => result.IsFailure);
        Assert.Equal("Auth.ExternalAccountLinkRequired", rejected.FirstError?.Code);
        Assert.Equal(1, await fixture.ReadAsync(context => context.Users.CountAsync()));
        Assert.Equal(1, await fixture.ReadAsync(context => context.UserLogins.CountAsync()));
        Assert.Equal(1, await fixture.ReadAsync(context => context.Set<ExternalRoleGrant>().CountAsync()));
    }

    [Theory]
    [InlineData("AspNetUserLogins")]
    [InlineData("ExternalRoleGrants")]
    public async Task FailureAfterUserCreation_RollsBackTheWholeIdentity(string table)
    {
        var fault = new CommandFault($"INSERT INTO \"{table}\"", () => new InvalidOperationException("Injected failure."));
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.ProvisionAsync(fixture.First, Identity()));

        await AssertEmptyAsync(fixture);
        fault.Armed = false;
        var retry = await fixture.ProvisionAsync(fixture.Second, Identity());
        Assert.True(retry.IsSuccess);
        Assert.Equal(1, await fixture.ReadAsync(context => context.Users.CountAsync()));
    }

    [Fact]
    public async Task FailureAfterProfileUpdate_RestoresBothProfileAndRoles()
    {
        var fault = new CommandFault("INSERT INTO \"ExternalRoleGrants\"", () => new InvalidOperationException("Injected failure."));
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        var initial = await fixture.ProvisionAsync(fixture.First, Identity());
        Assert.True(initial.IsSuccess);
        fault.Armed = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.ProvisionAsync(fixture.Second,
            Identity() with { Name = "Uncommitted profile", Roles = ["external-admin"] }));

        Assert.Equal("Original profile", await fixture.ReadAsync(context => context.Users.Select(user => user.DisplayName).SingleAsync()));
        Assert.Equal("User", await fixture.ReadAsync(context => context.Set<ExternalRoleGrant>()
            .Join(context.Roles, grant => grant.RoleId, role => role.Id, (_, role) => role.Name).SingleAsync()));
    }

    [Theory]
    [InlineData("sqlite-busy")]
    [InlineData("sqlite-locked")]
    [InlineData("sqlite-unique")]
    [InlineData("postgres-serialization")]
    [InlineData("postgres-deadlock")]
    [InlineData("postgres-unique")]
    [InlineData("ef-concurrency")]
    public async Task KnownContention_RetriesTheCompleteUnitWithFreshContexts(string kind)
    {
        var fault = new CommandFault("INSERT INTO \"AspNetUsers\"", () => Contention(kind)) { FailuresToInject = 1 };
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;

        var result = await fixture.ProvisionAsync(fixture.First, Identity());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fault.Hits);
        Assert.Equal(2, fault.Contexts.Distinct().Count());
        Assert.Equal(1, await fixture.ReadAsync(context => context.Users.CountAsync()));
        Assert.Equal(1, await fixture.ReadAsync(context => context.UserLogins.CountAsync()));
    }

    [Fact]
    public async Task IdentityConcurrencyFailure_RollsBackBeforeRetrying()
    {
        var fault = new CommandFault("UPDATE \"AspNetUsers\"", () => new DbUpdateConcurrencyException("Injected concurrency."))
        {
            FailuresToInject = 1
        };
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;

        var result = await fixture.ProvisionAsync(fixture.First, Identity());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fault.Contexts.Distinct().Count());
        Assert.Equal(1, await fixture.ReadAsync(context => context.Users.CountAsync()));
        Assert.Equal(1, await fixture.ReadAsync(context => context.UserLogins.CountAsync()));
    }

    [Fact]
    public async Task PersistentContention_StopsAfterThreeAttemptsWithoutPartialRows()
    {
        var fault = new CommandFault("UPDATE \"AspNetUsers\"", () => new DbUpdateConcurrencyException("Injected concurrency."));
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;

        var result = await fixture.ProvisionAsync(fixture.First, Identity());

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.ExternalProvisioningConflict", result.FirstError?.Code);
        Assert.Equal(3, fault.Hits);
        Assert.Equal(3, fault.Contexts.Distinct().Count());
        await AssertEmptyAsync(fixture);
    }

    [Theory]
    [InlineData("sqlite-foreign-key")]
    [InlineData("postgres-foreign-key")]
    [InlineData("network")]
    public async Task NonContentionFailures_AreNotBlindlyRetried(string kind)
    {
        var fault = new CommandFault("INSERT INTO \"AspNetUserLogins\"", () => kind switch
        {
            "sqlite-foreign-key" => new SqliteException("Injected foreign key.", 19, 787),
            "postgres-foreign-key" => new PostgresException("Injected foreign key.", "ERROR", "ERROR", "23503"),
            _ => new NpgsqlException("Injected connection failure.")
        });
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.ProvisionAsync(fixture.First, Identity()));

        Assert.Equal(1, fault.Hits);
        await AssertEmptyAsync(fixture);
    }

    [Fact]
    public async Task CancellationDuringContention_DoesNotRetryOrCommitPartialWrites()
    {
        using var cancellation = new CancellationTokenSource();
        var fault = new CommandFault("INSERT INTO \"AspNetUserLogins\"", () =>
        {
            cancellation.Cancel();
            return new SqliteException("Injected busy.", 5);
        });
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ProvisionAsync(fixture.First, Identity(), cancellation.Token));

        Assert.Equal(1, fault.Hits);
        await AssertEmptyAsync(fixture);
    }

    [Fact]
    public async Task AlreadyCancelledRequest_DoesNotStartWriting()
    {
        var fault = new CommandFault("INSERT INTO \"AspNetUsers\"", () => new SqliteException("Injected busy.", 5));
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ProvisionAsync(fixture.First, Identity(), cancellation.Token));

        Assert.Equal(0, fault.Hits);
        await AssertEmptyAsync(fixture);
    }

    [Fact]
    public async Task AmbiguousCommitFailure_IsNotAutomaticallyReplayed()
    {
        var fault = new CommitFault();
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync(fault);
        fault.Armed = true;

        await Assert.ThrowsAsync<NpgsqlException>(() => fixture.ProvisionAsync(fixture.First, Identity()));

        Assert.Equal(1, fault.Commits);
        await AssertEmptyAsync(fixture); // This injected failure happens before the test database commits.
    }

    [Fact]
    public async Task DatabaseConstraint_RejectsDuplicateNormalizedEmailsEvenWithoutUserManagerValidation()
    {
        await using var fixture = await ProvisioningDatabaseFixture.CreateAsync();
        await fixture.ReadAsync(async context =>
        {
            context.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = "first", NormalizedUserName = "FIRST", NormalizedEmail = "SAME@EXAMPLE.TEST" });
            await context.SaveChangesAsync();
            return true;
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.ReadAsync(async context =>
        {
            context.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = "second", NormalizedUserName = "SECOND", NormalizedEmail = "SAME@EXAMPLE.TEST" });
            await context.SaveChangesAsync();
            return true;
        }));

        Assert.Equal(1, await fixture.ReadAsync(context => context.Users.CountAsync()));
    }

    [Fact]
    public async Task Provisioning_DoesNotSaveOrDiscardTheCallersPendingChanges()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var pending = new Product("Caller work", null, 1m, 1);
        fixture.Db.Add(pending);

        var result = await fixture.Provisioner.ProvisionAsync(Identity());

        Assert.True(result.IsSuccess);
        Assert.Equal(EntityState.Added, fixture.Db.Entry(pending).State);
        Assert.Equal(0, await fixture.Db.Set<Product>().CountAsync());
    }

    [Fact]
    public async Task AmbientTransaction_IsRejectedRatherThanSilentlyCommittingIndependentWork()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        using var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provisioner.ProvisionAsync(Identity()));

        Assert.Contains("cannot join an ambient transaction", error.Message);
    }

    [Fact]
    public async Task TokenExpiringWhileWaitingForTheTransaction_CannotCreateAnAccount()
    {
        var advance = new TransactionAction();
        await using var fixture = await IdentitySecurityFixture.CreateAsync(interceptor: advance);
        var identity = Identity() with { TokenExpiresAtUtc = fixture.Clock.GetUtcNow().AddMinutes(1) };
        advance.Action = () => fixture.Clock.Advance(TimeSpan.FromMinutes(2));

        var result = await fixture.Provisioner.ProvisionAsync(identity);

        Assert.True(result.IsFailure);
        Assert.Empty(await fixture.Db.Users.ToListAsync());
        Assert.Empty(await fixture.Db.UserLogins.ToListAsync());
    }

    private static ExternalIdentity Identity() => new()
    {
        Provider = "Oidc",
        Issuer = "https://concurrency.test",
        ProviderSubject = "same-subject",
        Email = "race@example.test",
        EmailVerified = true,
        Name = "Original profile",
        TokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
    };

    private static async Task AssertEmptyAsync(ProvisioningDatabaseFixture fixture)
    {
        Assert.Equal(0, await fixture.ReadAsync(context => context.Users.CountAsync()));
        Assert.Equal(0, await fixture.ReadAsync(context => context.UserLogins.CountAsync()));
        Assert.Equal(0, await fixture.ReadAsync(context => context.Set<ExternalRoleGrant>().CountAsync()));
    }

    private static Exception Contention(string kind) => kind switch
    {
        "sqlite-busy" => new SqliteException("Injected busy.", 5),
        "sqlite-locked" => new SqliteException("Injected locked.", 6),
        "sqlite-unique" => new SqliteException("Injected unique.", 19, 2067),
        "postgres-serialization" => new PostgresException("Injected serialization.", "ERROR", "ERROR", "40001"),
        "postgres-deadlock" => new PostgresException("Injected deadlock.", "ERROR", "ERROR", "40P01"),
        "postgres-unique" => new PostgresException("Injected unique.", "ERROR", "ERROR", "23505"),
        _ => new DbUpdateConcurrencyException("Injected concurrency.")
    };

    private sealed class CommandFault(string commandFragment, Func<Exception> exception) : DbCommandInterceptor
    {
        private int _hits;
        public bool Armed { get; set; }
        public int FailuresToInject { get; init; } = int.MaxValue;
        public int Hits => _hits;
        public ConcurrentBag<Guid> Contexts { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command, eventData);
            return ValueTask.FromResult(result);
        }

        private void Check(DbCommand command, CommandEventData eventData)
        {
            if (!Armed || !command.CommandText.Contains(commandFragment, StringComparison.Ordinal)) return;
            Contexts.Add(eventData.Context!.ContextId.InstanceId);
            if (Interlocked.Increment(ref _hits) <= FailuresToInject) throw exception();
        }
    }

    private sealed class ConcurrentTransactionGate : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _starts;
        public bool Armed { get; set; }
        public int Starts => _starts;

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return result;
            if (Interlocked.Increment(ref _starts) >= 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private sealed class TransactionAction : DbTransactionInterceptor
    {
        public Action? Action { get; set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Action?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitFault : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Commits { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Commits++;
                throw new NpgsqlException("Injected connection failure during commit.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
