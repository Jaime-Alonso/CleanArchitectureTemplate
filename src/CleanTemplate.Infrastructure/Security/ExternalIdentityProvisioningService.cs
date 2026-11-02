using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CleanTemplate.Infrastructure.Security;

/// <summary>Retries a complete serializable unit in a fresh DI scope, never partial Identity writes.</summary>
public sealed class ExternalIdentityProvisioningService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExternalIdentityProvisioningService> logger) : IExternalIdentityProvisioningService
{
    private const int MaxAttempts = 3;

    public async Task<Result<ProvisionResult>> ProvisionAsync(
        ExternalIdentity externalIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalIdentity);
        if (System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("External provisioning owns its transaction and cannot join an ambient transaction.");
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Dispose the attempt (and its transaction) before retrying. Never clear the caller's tracker.
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ExternalIdentityProvisioningAttempt>()
                    .ExecuteAsync(externalIdentity, cancellationToken).ConfigureAwait(false);
                if (result.FirstError?.Code != ExternalIdentityProvisioningAttempt.RetryCode)
                {
                    return result;
                }
            }
            catch (Exception exception) when (IsRetryable(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Known rollback/uniqueness conflicts only; do not retry ambiguous connection/commit failures.
                logger.LogWarning("External provisioning database contention. Attempt {Attempt} of {MaxAttempts}", attempt, MaxAttempts);
            }

            if (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(25 * attempt + Random.Shared.Next(25));
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        logger.LogWarning("External provisioning contention exceeded the bounded retry budget");
        return Result<ProvisionResult>.Failure(new Error(
            "Auth.ExternalProvisioningConflict", "External identity cannot be provisioned. Retry later.", ErrorType.Conflict));
    }

    private static bool IsRetryable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException
                || current is PostgresException { SqlState: "40001" or "40P01" or "23505" }
                || current is SqlException { Number: 1205 or 1222 or 2601 or 2627 or 3960 }
                || current is SqliteException { SqliteErrorCode: 5 or 6 }
                || current is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 1555 or 2067 })
            {
                return true;
            }
        }

        return false;
    }
}
