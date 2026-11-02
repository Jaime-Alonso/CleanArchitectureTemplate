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

    builder.Services.AddSingleton(TimeProvider.System);

    builder.Host.UseSerilog((context, config) =>
        config.ReadFrom.Configuration(context.Configuration));

    var apiVersioningBuilder = builder.Services.AddApiVersioningConfiguration();
    builder.Services.AddApiContract(apiVersioningBuilder);
    builder.Services.AddGlobalErrorHandling();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiSecurity(builder.Configuration);
    builder.Services.AddApiCors(builder.Configuration);
    builder.Services.AddForwardedHeadersSupport(builder.Configuration);
    builder.Services.AddApiRateLimiting(builder.Configuration);
    builder.Services.AddOpenTelemetryObservability(builder.Configuration, builder.Environment);
    builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();

    builder.Services.ValidateRequestValidationConfiguration();
    var app = builder.Build();

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

    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapAuthEndpoints();

    app.MapProductEndpoints();
    app.MapHealthChecks("/health");

    app.Run();
}
catch (HostAbortedException)
{
    // WebApplicationFactory uses this exception to stop the entry point after building the host.
    throw;
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

public partial class Program
{
}
