using System.Diagnostics;
using System.Diagnostics.Metrics;
using CleanTemplate.Api.ExceptionHandling;
using CleanTemplate.Api.Extensions;
using CleanTemplate.Host.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

[Collection("Host console")]
public sealed class RequestObservabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingTelemetry_DoesNotChangeResponseOrMaskPipelineException(bool faulted)
    {
        using var listener = new MeterListener();
        var calls = 0;
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "CleanTemplate.Http") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => { calls++; throw new InvalidOperationException("listener"); });
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => { calls++; throw new InvalidOperationException("listener"); });
        listener.Start();
        var original = new InvalidOperationException("original");
        var logger = new ThrowingLogger();
        var middleware = new RequestObservabilityMiddleware(context =>
        {
            context.Response.StatusCode = 201;
            return faulted ? Task.FromException(original) : Task.CompletedTask;
        }, logger);
        var http = new DefaultHttpContext();
        if (faulted) Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(http)));
        else await middleware.InvokeAsync(http);
        Assert.Equal(201, http.Response.StatusCode);
        Assert.True(calls >= 2);
        Assert.True(logger.Called);
    }

    [Fact]
    public async Task ExceptionHandlerUsesHttpFallbackInLogAndProblemDetails()
    {
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            using var services = new ServiceCollection().AddLogging().AddGlobalErrorHandling().BuildServiceProvider();
            var context = new DefaultHttpContext { TraceIdentifier = "http-fallback", RequestServices = services };
            context.Response.Body = new MemoryStream();
            var logger = new HandlerLogger();
            Assert.True(await new GlobalExceptionHandler(logger).TryHandleAsync(context, new InvalidOperationException("CANARY"), default));
            context.Response.Body.Position = 0;
            using var details = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("http-fallback", details.RootElement.GetProperty("traceId").GetString());
            Assert.Contains("http-fallback", logger.Message);
            Assert.DoesNotContain("CANARY", logger.Message);
        }
        finally { Activity.Current = previous; }
    }

    [Fact]
    public void ProblemDetailsFallsBackToHttpTraceIdentifierWithoutActivity()
    {
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            var services = new ServiceCollection().AddGlobalErrorHandling().BuildServiceProvider();
            using (services)
            {
                var context = new DefaultHttpContext { TraceIdentifier = "http-fallback" };
                var details = new ProblemDetails();
                services.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value.CustomizeProblemDetails!(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details });
                Assert.Equal("http-fallback", details.Extensions["traceId"]);
            }
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task CompletionAndMetricsUseOnlyControlledDimensions(int status)
    {
        var records = new List<(string Name, long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "CleanTemplate.Http") meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => records.Add((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => records.Add((instrument.Name, 1, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();
        var logger = new CapturingLogger();
        var middleware = new RequestObservabilityMiddleware(context =>
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }, logger);
        var http = new DefaultHttpContext();
        http.Request.Method = "CANARY_METHOD";
        http.Request.Path = "/CANARY_PATH";
        http.Request.QueryString = new QueryString("?secret=CANARY_QUERY");
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/products/{id}"), 0, EndpointMetadataCollection.Empty, "test"));
        await middleware.InvokeAsync(http);
        Assert.Contains(records, r => r.Name == "cleantemplate.http.requests" && r.Value == 1 && Equals(r.Tags["route"], "/products/{id}") && Equals(r.Tags["method"], "OTHER") && Equals(r.Tags["status"], status));
        Assert.All(records, r => Assert.Equal(new[] { "method", "outcome", "route", "status" }, r.Tags.Keys.Order().ToArray()));
        Assert.All(records, r =>
        {
            Assert.Equal("OTHER", r.Tags["method"]);
            Assert.Equal("/products/{id}", r.Tags["route"]);
            Assert.Equal(status, r.Tags["status"]);
            Assert.Equal("completed", r.Tags["outcome"]);
        });
        Assert.Contains(records, r => r.Name == "cleantemplate.http.duration");
        Assert.Contains(records, r => r.Name == (status == 500 ? "cleantemplate.http.errors" : "cleantemplate.http.auth_rejections"));
        Assert.DoesNotContain("CANARY", logger.Message);
    }

    [Fact]
    public async Task EscapedExceptionIsNotRecordedAsSuccess()
    {
        var logger = new CapturingLogger();
        var middleware = new RequestObservabilityMiddleware(_ => throw new InvalidOperationException("CANARY"), logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext()));
        Assert.Contains("500", logger.Message);
        Assert.DoesNotContain("CANARY", logger.Message);
    }

    [Fact]
    public async Task EscapedFailuresAndCancellation_AreReportedSeparately()
    {
        var records = new List<(string Name, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "CleanTemplate.Http") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => records.Add((instrument.Name, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();
        var logger = new CapturingLogger();
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var http = new DefaultHttpContext { RequestAborted = aborted.Token };
        var middleware = new RequestObservabilityMiddleware(_ => throw new InvalidOperationException("CANARY"), logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(http));
        Assert.Contains("faulted", logger.Message);
        Assert.Contains(records, r => r.Name == "cleantemplate.http.errors" && Equals(r.Tags["outcome"], "faulted") && Equals(r.Tags["status"], 200));
        records.Clear();
        middleware = new RequestObservabilityMiddleware(_ => throw new OperationCanceledException(aborted.Token), logger);
        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(http));
        Assert.Contains("cancelled", logger.Message);
        Assert.Contains(records, r => r.Name == "cleantemplate.http.requests" && Equals(r.Tags["outcome"], "cancelled"));
        Assert.DoesNotContain(records, r => r.Name == "cleantemplate.http.errors");
    }

    [Fact]
    public async Task EscapedFailureAfterResponseStarted_PreservesWireStatusAndCountsError()
    {
        var records = new List<(string Name, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "CleanTemplate.Http") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => records.Add((instrument.Name, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();
        var logger = new CapturingLogger();
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var middleware = new RequestObservabilityMiddleware(_ => throw new InvalidOperationException(), logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(http));
        Assert.Equal(200, http.Response.StatusCode);
        Assert.Contains("faulted", logger.Message);
        Assert.Contains(records, r => r.Name == "cleantemplate.http.errors" && Equals(r.Tags["status"], 200) && Equals(r.Tags["outcome"], "faulted"));
    }

    [Fact]
    public void EnricherUsesCurrentServerActivityIdentifiers()
    {
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration().Enrich.With<ActivityEnricher>().WriteTo.Sink(sink).CreateLogger();
        logger.Information("completed");
        Assert.Equal(activity.TraceId.ToString(), ((ScalarValue)sink.Event!.Properties["TraceId"]).Value);
        Assert.Equal(activity.SpanId.ToString(), ((ScalarValue)sink.Event.Properties["SpanId"]).Value);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public LogEvent? Event { get; private set; }
        public void Emit(LogEvent logEvent) => Event = logEvent;
    }

    private sealed class ThrowingLogger : ILogger<RequestObservabilityMiddleware>
    {
        public bool Called { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Called = true;
            throw new InvalidOperationException("sink");
        }
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    private sealed class CapturingLogger : ILogger<RequestObservabilityMiddleware>
    {
        public string Message { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Message = formatter(state, exception);
    }

    private sealed class HandlerLogger : ILogger<GlobalExceptionHandler>
    {
        public string Message { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Message = formatter(state, exception);
    }
}
