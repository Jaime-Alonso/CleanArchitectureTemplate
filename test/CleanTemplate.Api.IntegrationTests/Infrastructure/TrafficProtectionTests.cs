using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CleanTemplate.Crosscutting.Networking;
using CleanTemplate.Crosscutting.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

[Collection(HostIntegrationCollection.Name)]
public sealed class TrafficProtectionTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("192.0.2.10", "198.51.100.1", "https", "198.51.100.1|https")]
    [InlineData("192.0.2.11", "198.51.100.2", "https", "192.0.2.11|http")]
    [InlineData("10.1.2.3", "198.51.100.3", "https", "198.51.100.3|https")]
    [InlineData("2001:db8:1::2", "2001:db8:2::1", "https", "2001:db8:2::1|https")]
    [InlineData("127.0.0.1", "198.51.100.4", "https", "198.51.100.4|https")]
    [InlineData("::1", "198.51.100.5", "https", "198.51.100.5|https")]
    [InlineData("192.0.2.10", "198.51.100.7, 10.1.2.4", "http, https", "10.1.2.4|https")]
    public async Task ForwardedValuesFollowActualTrustAndSingleHop(string peer, string forwarded, string proto, string expected)
    {
        using var server = CreateServer();
        var response = await Send(server, peer, forwarded, proto);
        Assert.Equal(200, response.Response.StatusCode);
        using var reader = new StreamReader(response.Response.Body);
        Assert.Equal(expected, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task SpoofedUntrustedHeadersCannotEvadeQuota_TrustedClientsAreIndependent()
    {
        using var server = CreateServer();
        Assert.Equal(200, (await Send(server, "192.0.2.20", "198.51.100.1", "https")).Response.StatusCode);
        Assert.Equal(429, (await Send(server, "192.0.2.20", "198.51.100.2", "https")).Response.StatusCode);
        Assert.Equal(200, (await Send(server, "192.0.2.10", "198.51.100.1", "https")).Response.StatusCode);
        Assert.Equal(200, (await Send(server, "192.0.2.10", "198.51.100.2", "https")).Response.StatusCode);
        Assert.Equal(429, (await Send(server, "192.0.2.10", "198.51.100.1", "https")).Response.StatusCode);
    }

    [Fact]
    public async Task HostRejectsBeforeAuthenticationAndClaimsTransformation()
    {
        var counter = new CostCounter();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
                options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("test", _ => new()
                    { PermitLimit = 1, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 })));
            services.AddSingleton(counter);
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, CountingHandler>("counting", _ => { });
            services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = "counting");
            services.AddTransient<IClaimsTransformation, CountingTransformation>();
        }));
        using var client = host.CreateClient();
        using var first = await client.GetAsync("/api/products/");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(1, counter.Authentication);
        Assert.Equal(1, counter.Transformation);
        using var rejected = await client.GetAsync("/api/products/");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(1, counter.Authentication);
        Assert.Equal(1, counter.Transformation);
    }

    private static IHost CreateServer()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.1.2.0/24",
            ["ForwardedHeaders:KnownNetworks:1"] = "2001:db8:1::/64",
            ["RateLimiting:Global:PermitLimit"] = "1",
            ["RateLimiting:Global:WindowSeconds"] = "300"
        }).Build();
        return new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer().ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddForwardedHeadersSupport(config);
            services.AddApiRateLimiting(config);
        }).Configure(app =>
        {
            app.UseForwardedHeaders();
            app.UseRouting();
            app.UseRateLimiter();
            app.UseEndpoints(endpoints => endpoints.MapGet("/", context =>
                context.Response.WriteAsync($"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}")));
        })).Start();
    }

    private static Task<HttpContext> Send(IHost server, string peer, string forwarded, string proto) => server.GetTestServer().SendAsync(context =>
    {
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = forwarded;
        context.Request.Headers["X-Forwarded-Proto"] = proto;
    });

    public sealed class CostCounter
    {
        public int Authentication;
        public int Transformation;
    }

    public sealed class CountingHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, CostCounter counter)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            counter.Authentication++;
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("counting")), Scheme.Name)));
        }
    }

    public sealed class CountingTransformation(CostCounter counter) : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            counter.Transformation++;
            return Task.FromResult(principal);
        }
    }
}
