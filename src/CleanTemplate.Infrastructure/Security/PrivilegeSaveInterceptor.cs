using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanTemplate.Infrastructure.Security;

internal sealed class PrivilegeSaveInterceptor(PrivilegeAuditState state) : SaveChangesInterceptor
{
    private void Capture(DbContext? context)
    {
        if (context is null) return;
        var audit = state.For(context);
        audit.Candidate = [];
        audit.Saving = true;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            var delta = entry.State == EntityState.Added ? 1 : entry.State == EntityState.Deleted ? -1 : 0;
            if (entry.Entity is IdentityUserRole<Guid> manual && delta != 0)
                audit.Candidate.Add(new(new(manual.UserId, manual.RoleId, "manual"), delta < 0, null, delta > 0, null));
            else if (entry.Entity is ExternalRoleGrant external && (delta != 0 || entry.State == EntityState.Modified && entry.Property(nameof(ExternalRoleGrant.ValidUntilUtc)).IsModified))
            {
                var expiry = entry.Property(nameof(ExternalRoleGrant.ValidUntilUtc));
                audit.Candidate.Add(new(new(external.UserId, external.RoleId, "external", external.LoginProvider, external.ProviderKey), delta != 1, delta == 1 ? null : (DateTime)expiry.OriginalValue!, delta != -1, delta == -1 ? null : (DateTime)expiry.CurrentValue!));
            }
        }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { Capture(eventData.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Capture(eventData.Context); return ValueTask.FromResult(result); }
    private void Saved(DbContext? context)
    {
        var transaction = context?.Database.CurrentTransaction?.TransactionId;
        // Ambient transactions are not observed by the EF transaction interceptor; never report them as autocommitted.
        if (context is null) return;
        var audit = state.For(context);
        if (transaction is null && System.Transactions.Transaction.Current is not null) audit.Failed();
        else audit.Saved(transaction);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) { Saved(eventData.Context); return result; }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) { Saved(eventData.Context); return ValueTask.FromResult(result); }
    public override void SaveChangesFailed(DbContextErrorEventData eventData) { if (eventData.Context is { } context) state.For(context).Failed(); }
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) { if (eventData.Context is { } context) state.For(context).Failed(); return Task.CompletedTask; }
    public override void SaveChangesCanceled(DbContextEventData eventData) { if (eventData.Context is { } context) state.For(context).Failed(); }
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default) { if (eventData.Context is { } context) state.For(context).Failed(); return Task.CompletedTask; }
}
