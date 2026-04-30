using System;
using System.Globalization;
using System.Threading.RateLimiting;
using CleanTemplate.Core.CrossCutting.RateLimiting.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Core.CrossCutting.RateLimiting;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ApiRateLimitingOptions>()
            .Bind(configuration.GetSection(ApiRateLimitingOptions.SectionName))
            .ValidateOnStart();

        var options = configuration.GetSection(ApiRateLimitingOptions.SectionName).Get<ApiRateLimitingOptions>()
            ?? new ApiRateLimitingOptions();

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => BuildFixedWindowOptions(options.Global)));

            foreach (var (policyName, policyOptions) in options.Policies)
            {
                rateLimiterOptions.AddPolicy(policyName, httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetPartitionKey(httpContext),
                        _ => BuildFixedWindowOptions(policyOptions)));
            }

            rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");

                logger.LogWarning(
                    "Rate limit exceeded for {Method} {Path}. Client {ClientIp}. Retry-After {RetryAfterSeconds}s",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path,
                    GetPartitionKey(context.HttpContext),
                    context.HttpContext.Response.Headers.RetryAfter.ToString());

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "rate_limit_exceeded",
                        detail = "Too many requests. Please retry later."
                    },
                    cancellationToken)
                    .ConfigureAwait(false);
            };
        });

        return services;
    }

    private static string GetPartitionKey(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static FixedWindowRateLimiterOptions BuildFixedWindowOptions(FixedWindowPolicyOptions policy)
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, policy.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, policy.WindowSeconds)),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = Math.Max(0, policy.QueueLimit)
        };
    }
}
