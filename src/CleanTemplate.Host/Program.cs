using CleanTemplate.Api.Endpoints;
using CleanTemplate.Api.Extensions;
using CleanTemplate.Api.Security.Extensions;
using CleanTemplate.Application.Extensions;
using CleanTemplate.Crosscutting.Networking;
using CleanTemplate.Crosscutting.Observability;
using CleanTemplate.Crosscutting.RateLimiting;
using CleanTemplate.Host.Extensions;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting CleanTemplate application...");

    var operation = args.Length > 0 && args[0] is "migrate" or "bootstrap-admin" ? args[0] : null;
    if (operation is not null && args.Length != 1)
        throw new InvalidOperationException("Operations do not accept additional arguments.");
    if (args.Any(a => a.StartsWith("--operation", StringComparison.Ordinal)))
        throw new InvalidOperationException("Unsupported operation.");
    var builder = WebApplication.CreateBuilder(operation is "migrate" or "bootstrap-admin" ? [] : args);

    var ephemeralOptIn = builder.Configuration["Database:AllowEphemeralInProduction"];
    if (ephemeralOptIn is not null && !bool.TryParse(ephemeralOptIn, out _))
        throw new StartupConfigurationException("Database:AllowEphemeralInProduction");
    CleanTemplate.Infrastructure.Database.DatabaseProvider provider = default;
    ConfigureSafely("DatabaseProvider", () => provider = CleanTemplate.Infrastructure.Database.DatabaseProviderParser.Parse(builder.Configuration["DatabaseProvider"]));
    if (builder.Environment.IsProduction()
        && provider == CleanTemplate.Infrastructure.Database.DatabaseProvider.SqliteInMemory
        && !string.Equals(ephemeralOptIn, "true", StringComparison.OrdinalIgnoreCase))
        throw new StartupConfigurationException("Database:AllowEphemeralInProduction");

    if (operation is "migrate" or "bootstrap-admin")
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddInfrastructure(builder.Configuration);
        await using var operationHost = builder.Build();
        using var scope = operationHost.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsSqlite())
            throw new InvalidOperationException("Operations require a persistent database provider.");
        if (operation == "migrate")
            await db.InitializeDatabaseAsync(false);
        else
            await scope.ServiceProvider.GetRequiredService<CleanTemplate.Infrastructure.Identity.IdentityDataSeeder>().SeedAsync();
        return 0;
    }

    StartupPreflight.Check(builder.Configuration);

    builder.Services.AddSingleton(TimeProvider.System);

    builder.Host.UseSerilog((context, config) =>
        config.ReadFrom.Configuration(context.Configuration));

    var apiVersioningBuilder = builder.Services.AddApiVersioningConfiguration();
    builder.Services.AddApiContract(apiVersioningBuilder);
    builder.Services.AddGlobalErrorHandling();

    builder.Services.AddApplication();
    ConfigureSafely("Database/Auth/Caching", () => builder.Services.AddInfrastructure(builder.Configuration));
    ConfigureSafely("Auth", () => builder.Services.AddApiSecurity(builder.Configuration));
    ConfigureSafely("Cors", () => builder.Services.AddApiCors(builder.Configuration));
    ConfigureSafely("ForwardedHeaders:KnownProxies/Networks", () => builder.Services.AddForwardedHeadersSupport(builder.Configuration));
    ConfigureSafely("RateLimiting", () => builder.Services.AddApiRateLimiting(builder.Configuration));
    ConfigureSafely("OpenTelemetry", () => builder.Services.AddOpenTelemetryObservability(builder.Configuration, builder.Environment));
    ConfigureSafely("HealthChecks", () => builder.Services.AddOperationalHealthChecks(builder.Configuration));

    builder.Services.ValidateRequestValidationConfiguration();
    var app = builder.Build();

    ConfigureSafely("Auth", () => _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanTemplate.Api.Security.Options.AuthOptions>>().Value);
    ConfigureSafely("Auth:Schemes:LocalJwt", () => _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanTemplate.Infrastructure.Security.RefreshTokenOptions>>().Value);
    ConfigureSafely("Auth:Schemes:LocalJwt", () => _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanTemplate.Infrastructure.Security.Options.LocalJwtOptions>>().Value);
    ConfigureSafely("Auth:Provisioning", () => _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanTemplate.Application.Security.ExternalRoleMappingOptions>>().Value);
    ConfigureSafely("Auth:RefreshTokenCleanup", () => _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanTemplate.Infrastructure.Security.RefreshTokenCleanupOptions>>().Value);
    ConfigureSafely("Caching", () => _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanTemplate.Infrastructure.Caching.Options.CacheOptions>>().Value);
    ConfigureSafely("RateLimiting", () => _ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleanTemplate.Crosscutting.RateLimiting.Options.ApiRateLimitingOptions>>().Value);

    await app.Services.InitializeIdentityAsync(app.Lifetime.ApplicationStopping);

    app.UseGlobalErrorHandling();
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().WithDocumentPerVersion();
        app.UseScalarDocumentation();
    }

    app.UseConfiguredForwardedHeaders();
    app.UseCors(CleanTemplate.Crosscutting.Networking.Options.ApiCorsOptions.PolicyName);
    app.UseHttpsRedirection();

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapAuthEndpoints();

    app.MapProductEndpoints();
    app.MapOperationalHealthChecks();

    app.Run();
}
catch (HostAbortedException)
{
    // WebApplicationFactory uses this exception to stop the entry point after building the host.
    throw;
}
catch (StartupConfigurationException ex)
{
    Log.Fatal("Invalid startup configuration: {ConfigurationKey}", ex.Key);
    return 1;
}
catch (Exception ex)
{
    // Exception messages can contain configuration values, including secrets.
    Log.Fatal("Application terminated unexpectedly ({ExceptionType}). Check the startup configuration and dependencies", ex.GetType().Name);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

static void ConfigureSafely(string key, Action configure)
{
    try { configure(); }
    catch (Exception) { throw new StartupConfigurationException(key); }
}

internal sealed class StartupConfigurationException(string key) : Exception
{
    public string Key { get; } = key;
}

public partial class Program
{
}
