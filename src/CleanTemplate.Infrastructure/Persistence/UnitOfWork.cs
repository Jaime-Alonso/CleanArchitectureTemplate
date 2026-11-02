using System.Transactions;
using CleanTemplate.Application.Contracts.Persistence;
using CleanTemplate.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Infrastructure.Persistence;

public sealed class UnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    private bool _failed;
    private bool _inTransaction;

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            Invalidate();
            return false;
        }
        catch
        {
            Invalidate();
            throw;
        }
    }

    public async Task<Result> ExecuteInTransactionAsync(Func<CancellationToken, Task<Result>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureUsable();
        if (_inTransaction || dbContext.Database.CurrentTransaction is not null || Transaction.Current is not null)
            throw new InvalidOperationException("Nested or ambient transactions are not supported.");

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _inTransaction = true;
            try
            {
                var result = await operation(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsFailure)
                {
                    Invalidate();
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return result;
                }
                EnsureUsable();
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch
            {
                Invalidate();
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _inTransaction = false;
            }
        }
        catch
        {
            Invalidate();
            throw;
        }
    }

    private void EnsureUsable()
    {
        if (_failed)
            throw new InvalidOperationException("The unit of work has failed; discard its scope.");
    }

    private void Invalidate()
    {
        _failed = true;
        dbContext.ChangeTracker.Clear();
    }
}
