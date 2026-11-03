using System.Globalization;
using System.Threading.RateLimiting;
using CleanTemplate.Crosscutting.RateLimiting.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Crosscutting.RateLimiting;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ApiRateLimitingOptions>()
            .Bind(configuration.GetSection(ApiRateLimitingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiRateLimitingOptions>, RateLimitingValidator>();

        ApiRateLimitingOptions options = configuration.GetSection(ApiRateLimitingOptions.SectionName).Get<ApiRateLimitingOptions>()
            ?? new ApiRateLimitingOptions();
        var error = ValidateOptions(options);
        if (error is not null)
            throw new InvalidOperationException(error);

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => BuildFixedWindowOptions(options.Global)));

            foreach ((string? policyName, FixedWindowPolicyOptions? policyOptions) in options.Policies)
            {
                rateLimiterOptions.AddPolicy(policyName, httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetPartitionKey(httpContext),
                        _ => BuildFixedWindowOptions(policyOptions)));
            }

            rateLimiterOptions.OnRejected = (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                ILogger logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");

                logger.LogWarning(
                    "Rate limit exceeded for {Method} {Path}. Client {ClientIp}. Retry-After {RetryAfterSeconds}s",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path,
                    GetPartitionKey(context.HttpContext),
                    context.HttpContext.Response.Headers.RetryAfter.ToString());

                return ValueTask.CompletedTask;
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
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = policy.QueueLimit
        };
    }

    private static string? ValidateOptions(ApiRateLimitingOptions options)
    {
        static string? ValidatePolicy(FixedWindowPolicyOptions policy, string key)
        {
            if (policy.PermitLimit < 1) return $"{key}:PermitLimit must be greater than zero.";
            if (policy.WindowSeconds is < 1 or > 4294967) return $"{key}:WindowSeconds must be between 1 and 4294967.";
            if (policy.QueueLimit < 0) return $"{key}:QueueLimit must not be negative.";
            return null;
        }

        var error = ValidatePolicy(options.Global, "RateLimiting:Global");
        if (error is not null) return error;
        foreach (var (name, policy) in options.Policies)
        {
            error = ValidatePolicy(policy, $"RateLimiting:Policies:{name}");
            if (error is not null) return error;
        }

        return null;
    }

    private sealed class RateLimitingValidator : IValidateOptions<ApiRateLimitingOptions>
    {
        public RateLimitingValidator() { }

        public ValidateOptionsResult Validate(string? name, ApiRateLimitingOptions options)
        {
            var error = ValidateOptions(options);
            return error is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
        }
    }
}
