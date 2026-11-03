using System.Diagnostics.Metrics;

namespace CleanTemplate.Infrastructure.Security;

internal static class SecurityMetrics
{
    private static readonly Meter Meter = new("CleanTemplate.Security");
    private static readonly Counter<long> Replay = Meter.CreateCounter<long>("security.refresh.replay");
    private static readonly Counter<long> Rejection = Meter.CreateCounter<long>("security.provisioning.rejected");
    private static readonly Counter<long> Contention = Meter.CreateCounter<long>("security.provisioning.contention");
    private static readonly Counter<long> CleanupFailure = Meter.CreateCounter<long>("security.cleanup.failure");
    private static readonly Counter<long> PrivilegeCommitted = Meter.CreateCounter<long>("security.privilege.committed");

    internal static void RecordReplay() => Record(Replay);
    internal static void RecordRejection() => Record(Rejection);
    internal static void RecordContention() => Record(Contention);
    internal static void RecordCleanupFailure() => Record(CleanupFailure);
    internal static void RecordPrivilege(string source, string change)
    {
        try { PrivilegeCommitted.Add(1, new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("change", change)); }
        catch { }
    }

    private static void Record(Counter<long> counter)
    {
        // A third-party listener must not change security or transaction behavior.
        try { counter.Add(1); }
        catch { }
    }
}
