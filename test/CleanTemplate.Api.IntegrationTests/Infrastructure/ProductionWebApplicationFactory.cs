namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public sealed class ProductionWebApplicationFactory() : TestWebApplicationFactory("Production", new Dictionary<string, string?>
{
    ["Database:AllowEphemeralInProduction"] = "true",
    ["IdentitySeed:AdminPassword"] = null
});
