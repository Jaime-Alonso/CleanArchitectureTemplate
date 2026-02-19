using Asp.Versioning;
using CleanTemplate.Api.Endpoints;
using CleanTemplate.Api.Extensions;
using CleanTemplate.Api.Security.Extensions;
using CleanTemplate.Application.Extensions;
using CleanTemplate.CrossCutting.Networking;
using CleanTemplate.CrossCutting.Observability;
using CleanTemplate.CrossCutting.RateLimiting;
using CleanTemplate.Host.Extensions;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting CleanTemplate application...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSingleton(TimeProvider.System);

    builder.Host.UseSerilog((context, config) =>
        config.ReadFrom.Configuration(context.Configuration));

    var apiVersioningBuilder = builder.Services.AddApiVersioningConfiguration();
    builder.Services.AddScalarDocumentation(apiVersioningBuilder);
    builder.Services.AddGlobalErrorHandling();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiSecurity(builder.Configuration);
    builder.Services.AddApiCors(builder.Configuration);
    builder.Services.AddForwardedHeadersSupport(builder.Configuration);
    builder.Services.AddApiRateLimiting(builder.Configuration);
    builder.Services.AddOpenTelemetryObservability(builder.Configuration, builder.Environment);
    builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();

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
        app.MapOpenApi();
        app.UseScalarDocumentation();
    }

    app.UseConfiguredForwardedHeaders();
    app.UseCors(CleanTemplate.CrossCutting.Networking.Options.ApiCorsOptions.PolicyName);
    app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapAuthEndpoints();

    app.MapProductEndpoints();
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program
{
}
