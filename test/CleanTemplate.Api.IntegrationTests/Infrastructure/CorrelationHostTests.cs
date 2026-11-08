using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

[CollectionDefinition("Host console", DisableParallelization = true)]
public sealed class HostConsoleCollection;

[Collection("Host console")]
public sealed class CorrelationHostTests
{
    [Fact]
    public async Task InvalidVersionIsObservedByRealHostWithoutRawRequestData()
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        using var listener = new MeterListener();
        var measurements = new List<(string? TraceId, KeyValuePair<string, object?>[] Tags)>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "CleanTemplate.Http" && instrument.Name == "cleantemplate.http.requests")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (value == 1)
                measurements.Add((Activity.Current?.TraceId.ToString(), tags.ToArray()));
        });
        listener.Start();
        try
        {
            await using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login?secret=CANARY_QUERY");
            request.Headers.TryAddWithoutValidation("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
            request.Headers.TryAddWithoutValidation("X-Api-Version", "invalid");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
            var events = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var completed = events.Select(e => e.RootElement).Single(e => e.GetProperty("MessageTemplate").GetString()!.StartsWith("HTTP request {Outcome}", StringComparison.Ordinal));
                var properties = completed.GetProperty("Properties");
                Assert.Equal("0123456789abcdef0123456789abcdef", properties.GetProperty("TraceId").GetString());
                Assert.Equal(400, properties.GetProperty("StatusCode").GetInt32());
                var measurement = Assert.Single(measurements);
                Assert.Equal(properties.GetProperty("TraceId").GetString(), measurement.TraceId);
                Assert.Contains(measurement.Tags, tag => tag.Key == "status" && Equals(tag.Value, 400));
                Assert.DoesNotContain("CANARY_QUERY", output.ToString());
                Assert.DoesNotContain(measurement.Tags, tag => tag.Value?.ToString()?.Contains("CANARY_QUERY", StringComparison.Ordinal) == true);
            }
            finally { foreach (var item in events) item.Dispose(); }
        }
        finally { Console.SetOut(previous); }
    }

    [Fact]
    public async Task MatchedEndpointFailureRetainsSafeRouteAndTrace()
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            await using var factory = new MatchedFailureFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            using var request = new HttpRequestMessage(HttpMethod.Get, "/failure/CANARY_VALUE?x=CANARY_QUERY");
            request.Headers.TryAddWithoutValidation("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            using var problem = (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
            Assert.Equal("0123456789abcdef0123456789abcdef", problem.RootElement.GetProperty("traceId").GetString());
            var events = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var completed = events.Select(e => e.RootElement).Single(e => e.GetProperty("MessageTemplate").GetString()!.StartsWith("HTTP request {Outcome}", StringComparison.Ordinal));
                var properties = completed.GetProperty("Properties");
                Assert.Equal("0123456789abcdef0123456789abcdef", properties.GetProperty("TraceId").GetString());
                Assert.Equal("/failure/{id}", properties.GetProperty("Route").GetString());
                Assert.DoesNotContain("CANARY", output.ToString());
            }
            finally { foreach (var item in events) item.Dispose(); }
        }
        finally { Console.SetOut(previous); }
    }

    [Fact]
    public async Task ErrorAndCompletionShareServerActivityAndHostVersion()
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            await using var factory = new CorrelationFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            using var request = new HttpRequestMessage(HttpMethod.Post, "/CANARY_PATH?x=CANARY_QUERY");
            request.Headers.TryAddWithoutValidation("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
            request.Headers.TryAddWithoutValidation("X-Canary", "CANARY_HEADER");
            request.Content = new StringContent("CANARY_BODY");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            using var problem = (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
            var trace = problem.RootElement.GetProperty("traceId").GetString();
            var events = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = events.Select(e => e.RootElement).Single(e => e.GetProperty("MessageTemplate").GetString()!.StartsWith("Request {TraceId} failed", StringComparison.Ordinal));
                var completed = events.Select(e => e.RootElement).Single(e => e.GetProperty("MessageTemplate").GetString()!.StartsWith("HTTP request {Outcome}", StringComparison.Ordinal));
                var errorProperties = error.GetProperty("Properties");
                var completionProperties = completed.GetProperty("Properties");
                Assert.Equal("0123456789abcdef0123456789abcdef", trace);
                Assert.Equal(trace, errorProperties.GetProperty("TraceId").GetString());
                Assert.Equal(trace, completionProperties.GetProperty("TraceId").GetString());
                Assert.Equal(errorProperties.GetProperty("SpanId").GetString(), completionProperties.GetProperty("SpanId").GetString());
                Assert.Equal(500, completionProperties.GetProperty("StatusCode").GetInt32());
                var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
                Assert.Equal(version, completionProperties.GetProperty("ServiceVersion").GetString());
                Assert.DoesNotContain("CANARY", output.ToString());
            }
            finally { foreach (var item in events) item.Dispose(); }
        }
        finally { Console.SetOut(previous); }
    }

    private sealed class CorrelationFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingFilter>());
        }
    }

    private sealed class ThrowingFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Run(_ => throw new InvalidOperationException("CANARY_EXCEPTION"));
        };
    }

    private sealed class MatchedFailureFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, MatchedFailureFilter>());
        }
    }

    private sealed class MatchedFailureFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(endpoints => endpoints.MapGet("/failure/{id}", (HttpContext _) => throw new InvalidOperationException("CANARY_EXCEPTION")));
        };
    }
}
