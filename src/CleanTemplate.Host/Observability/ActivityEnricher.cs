using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace CleanTemplate.Host.Observability;

public sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.RemovePropertyIfPresent("RequestPath");
        logEvent.RemovePropertyIfPresent("QueryString");
        if (Activity.Current is not { } activity) return;
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
