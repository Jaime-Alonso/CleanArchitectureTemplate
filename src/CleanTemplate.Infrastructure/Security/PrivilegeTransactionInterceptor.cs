using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanTemplate.Infrastructure.Security;

internal sealed class PrivilegeTransactionInterceptor(PrivilegeAuditState state) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) { if (eventData.Context is { } context) state.For(context).Commit(eventData.TransactionId); }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { TransactionCommitted(transaction, eventData); return Task.CompletedTask; }
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) { if (eventData.Context is { } context) state.For(context).Rollback(eventData.TransactionId); }
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { TransactionRolledBack(transaction, eventData); return Task.CompletedTask; }
    public override void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) { if (eventData.Context is { } context) state.For(context).RollbackToSavepoint(eventData.TransactionId); }
    public override Task RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default) { RolledBackToSavepoint(transaction, eventData); return Task.CompletedTask; }
}
