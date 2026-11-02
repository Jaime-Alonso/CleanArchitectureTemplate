using CleanTemplate.SharedKernel.Results;

namespace CleanTemplate.Application.Contracts.Persistence;

/// <summary>Owns persistence for one scoped use case. Discard the scope after a failure.</summary>
public interface IUnitOfWork
{
    /// <summary>Flushes changes; false indicates an optimistic concurrency conflict.</summary>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs explicit flushes atomically; changes not flushed with SaveChangesAsync are not committed.
    /// A failure result rolls back and invalidates the scope, as do exceptions, cancellation and failed flushes.
    /// An ignored failed flush cannot be committed even if the callback returns success.
    /// </summary>
    Task<Result> ExecuteInTransactionAsync(Func<CancellationToken, Task<Result>> operation, CancellationToken cancellationToken = default);
}
