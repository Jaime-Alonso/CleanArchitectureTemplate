using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Infrastructure.Security;

internal readonly record struct PrivilegeKey(Guid UserId, Guid RoleId, string Source, string? LoginProvider = null, string? ProviderKey = null);
internal readonly record struct PrivilegeChange(PrivilegeKey Key, bool BeforeExists, DateTime? BeforeExpiry, bool AfterExists, DateTime? AfterExpiry);

internal sealed class PrivilegeAuditState(ILogger<PrivilegeAuditState> logger)
{
    private readonly ConditionalWeakTable<DbContext, PrivilegeContextAuditState> _contexts = new();
    internal PrivilegeContextAuditState For(DbContext context) => _contexts.GetValue(context, _ => new(logger));
}

internal sealed class PrivilegeContextAuditState(ILogger<PrivilegeAuditState> logger)
{
    private readonly Dictionary<PrivilegeKey, PrivilegeChange> _buffer = new();
    private Guid? _transaction;
    internal List<PrivilegeChange> Candidate { get; set; } = [];
    internal bool Saving { get; set; }

    internal void Saved(Guid? transaction)
    {
        if (_transaction != transaction) { _buffer.Clear(); _transaction = transaction; }
        foreach (var change in Candidate)
        {
            if (_buffer.TryGetValue(change.Key, out var previous))
                _buffer[change.Key] = change with { BeforeExists = previous.BeforeExists, BeforeExpiry = previous.BeforeExpiry };
            else
                _buffer[change.Key] = change;
        }
        Candidate.Clear();
        Saving = false;
        if (transaction is null) Emit();
    }

    internal void Commit(Guid transaction)
    {
        if (_transaction == transaction) Emit();
    }

    internal void Rollback(Guid transaction)
    {
        if (_transaction == transaction) { _buffer.Clear(); _transaction = null; }
    }

    internal void RollbackToSavepoint(Guid transaction)
    {
        // EF's automatic savepoint rollback happens during a failed save, before its candidate is discarded.
        // A caller-controlled savepoint cannot be identified by name here: conservatively drop the buffer.
        if (!Saving && _transaction == transaction) _buffer.Clear();
    }

    internal void Failed() { Candidate.Clear(); Saving = false; }

    private void Emit()
    {
        foreach (var (key, value) in _buffer)
        {
            var change = !value.BeforeExists && value.AfterExists ? "grant" : value.BeforeExists && !value.AfterExists ? "revoke" : value.BeforeExists && value.AfterExists && value.BeforeExpiry != value.AfterExpiry ? "renew" : null;
            if (change is null) continue;
            try
            {
                logger.LogInformation(new EventId(2601, "PrivilegeAssignmentCommitted"),
                    "Privilege assignment {Source} {Change} {Result} target {TargetId} role {RoleId} actor {Actor} trace {TraceId} span {SpanId}",
                    key.Source, change, "committed", key.UserId, key.RoleId, "unknown", Activity.Current?.TraceId.ToString(), Activity.Current?.SpanId.ToString());
            }
            catch { }
            SecurityMetrics.RecordPrivilege(key.Source, change);
        }
        _buffer.Clear();
        _transaction = null;
    }
}
