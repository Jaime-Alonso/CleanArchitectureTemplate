using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using CleanTemplate.Api.Extensions;
using CleanTemplate.Crosscutting.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public sealed class TracePrivacyTests
{
    [Theory]
    [InlineData(ActivityKind.Client)]
    [InlineData(ActivityKind.Server)]
    public void NonHttpSpansAreNotAltered(ActivityKind kind)
    {
        var exporter = new CaptureExporter();
        using var tracer = Sdk.CreateTracerProviderBuilder().AddSource("privacy-test")
            .AddPrivateHttpTracing().AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        using var source = new ActivitySource("privacy-test");
        using var span = source.StartActivity("database operation", kind)!;
        span.SetTag("db.query.text", "select 1");
        span.SetStatus(ActivityStatusCode.Error, "database detail");
        span.Stop();
        Assert.Contains(span, exporter.Activities);
        Assert.Equal("select 1", span.GetTagItem("db.query.text"));
        Assert.Equal("database operation", span.DisplayName);
        Assert.Equal("database detail", span.StatusDescription);
    }

    [Fact]
    public async Task RealHttpInstrumentationExportsMinimizedSpans()
    {
        var exporter = new CaptureExporter();
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("test", serviceVersion: "test-version"))
            .SetSampler(new AlwaysOnSampler())
            .AddPrivateHttpTracing()
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services => { services.AddRouting(); services.AddGlobalErrorHandling(); })
            .Configure(app =>
            {
                app.UseExceptionHandler();
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapPost("/items/{id}", async context =>
                {
                    using var reader = new StreamReader(context.Request.Body);
                    _ = await reader.ReadToEndAsync();
                    throw new InvalidOperationException("CANARY_EXCEPTION");
                }));
            })).StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/items/CANARY_PATH?secret=CANARY_QUERY");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer CANARY_AUTH");
        request.Headers.TryAddWithoutValidation("User-Agent", "CANARY_AGENT");
        request.Content = new StringContent("CANARY_BODY");
        using var response = await host.GetTestClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var handler = new SocketsHttpHandler { ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(new ResponseStream()) };
        using var client = new HttpClient(handler);
        using var outgoing = new HttpRequestMessage(HttpMethod.Post, "http://example.test/CANARY_PATH?secret=CANARY_QUERY");
        outgoing.Headers.TryAddWithoutValidation("Authorization", "Bearer CANARY_AUTH");
        outgoing.Headers.TryAddWithoutValidation("User-Agent", "CANARY_AGENT");
        outgoing.Content = new StringContent("CANARY_BODY");
        using var outgoingResponse = await client.SendAsync(outgoing);
        Assert.Equal(HttpStatusCode.InternalServerError, outgoingResponse.StatusCode);

        using var unmatched = new HttpRequestMessage(new HttpMethod("CANARY_METHOD"), "/CANARY_PATH?x=CANARY_QUERY");
        using var unmatchedResponse = await host.GetTestClient().SendAsync(unmatched);
        Assert.Equal(HttpStatusCode.NotFound, unmatchedResponse.StatusCode);
        using var failedHandler = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => ValueTask.FromException<Stream>(new HttpRequestException("CANARY_EXCEPTION"))
        };
        using var failedClient = new HttpClient(failedHandler);
        await Assert.ThrowsAsync<HttpRequestException>(() => failedClient.GetAsync("http://example.test/CANARY_PATH?x=CANARY_QUERY"));

        Assert.Equal("test-version", tracer.GetResource().Attributes.Single(a => a.Key == "service.version").Value);
        Assert.Contains(exporter.Activities, a => a.Kind == ActivityKind.Server);
        Assert.Contains(exporter.Activities, a => a.Kind == ActivityKind.Client);
        foreach (var activity in exporter.Activities)
        {
            Assert.NotEqual(default, activity.TraceId);
            Assert.NotEqual(default, activity.SpanId);
            var text = string.Join(" ", activity.TagObjects.Select(t => $"{t.Key}={t.Value}")
                .Concat(activity.Events.SelectMany(e => e.Tags.Select(t => $"{e.Name} {t.Key}={t.Value}")))
                .Append(activity.DisplayName).Append(activity.StatusDescription));
            Assert.DoesNotContain("CANARY", text, StringComparison.OrdinalIgnoreCase);
            Assert.All(activity.Events, e => Assert.All(e.Tags, tag => Assert.Null(tag.Value)));
        }
        var server = Assert.Single(exporter.Activities, a => a.Kind == ActivityKind.Server && (string?)a.GetTagItem("http.route") == "/items/{id}");
        Assert.Equal("/items/{id}", server.GetTagItem("http.route"));
        Assert.Equal(500, server.GetTagItem("http.response.status_code"));
        Assert.Contains(exporter.Activities, a => a.Kind == ActivityKind.Server && a.DisplayName == "_OTHER unmatched");
        Assert.Contains(exporter.Activities, a => a.Kind == ActivityKind.Client && a.Status == ActivityStatusCode.Error);
        Assert.Contains(exporter.Activities, a => a.Kind == ActivityKind.Client && a.Events.Any(e => e.Name == "exception"));
    }

    private sealed class CaptureExporter : BaseExporter<Activity>
    {
        public ConcurrentQueue<Activity> Activities { get; } = new();
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch) Activities.Enqueue(activity);
            return ExportResult.Success;
        }
    }

    private sealed class ResponseStream : Stream
    {
        private readonly MemoryStream _response = new(Encoding.ASCII.GetBytes("HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _response.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _response.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) { }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _response.Dispose(); base.Dispose(disposing); }
    }
}
