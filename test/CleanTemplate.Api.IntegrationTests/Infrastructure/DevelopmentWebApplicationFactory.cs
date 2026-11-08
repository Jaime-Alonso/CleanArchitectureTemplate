namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

// Development is intentional only for contracts served by the development-only documentation routes.
public sealed class DevelopmentWebApplicationFactory() : TestWebApplicationFactory("Development");
