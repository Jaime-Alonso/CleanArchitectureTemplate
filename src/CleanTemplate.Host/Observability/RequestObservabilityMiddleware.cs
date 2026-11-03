using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Diagnostics;

namespace CleanTemplate.Host.Observability;

public sealed class RequestObservabilityMiddleware(RequestDelegate next, ILogger<RequestObservabilityMiddleware> logger)
{
    private static readonly Meter Meter = new("CleanTemplate.Http");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("cleantemplate.http.requests");
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("cleantemplate.http.errors");
    private static readonly Counter<long> AuthRejects = Meter.CreateCounter<long>("cleantemplate.http.auth_rejections");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("cleantemplate.http.duration", "ms");

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch
        {
            outcome = "faulted";
            throw;
        }
        finally
        {
            var method = context.Request.Method switch
            {
                "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" => context.Request.Method,
                _ => "OTHER"
            };
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                ?? (context.Features.Get<IExceptionHandlerFeature>()?.Endpoint as RouteEndpoint)?.RoutePattern.RawText
                ?? "unmatched";
            var status = outcome == "faulted" && !context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested
                ? StatusCodes.Status500InternalServerError
                : context.Response.StatusCode;
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var tags = new TagList { { "method", method }, { "route", route }, { "status", status }, { "outcome", outcome } };
            // Observability callbacks and sinks must not alter the request or mask its exception.
            try { Requests.Add(1, tags); } catch { }
            try { Duration.Record(elapsed, tags); } catch { }
            if (outcome != "cancelled" && (status >= 500 || outcome == "faulted"))
            {
                try { Errors.Add(1, tags); } catch { }
            }
            if (status is 401 or 403)
            {
                try { AuthRejects.Add(1, tags); } catch { }
            }
            try { logger.LogInformation("HTTP request {Outcome} {Method} {Route} {StatusCode} {DurationMs}", outcome, method, route, status, elapsed); } catch { }
        }
    }
}
