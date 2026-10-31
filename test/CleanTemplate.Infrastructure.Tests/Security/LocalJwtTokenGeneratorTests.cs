using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.Infrastructure.Security.Options;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Security;

public sealed class LocalJwtTokenGeneratorTests
{
    [Fact]
    public void Generate_WhenLocalJwtIsDisabled_RejectsIssuanceBeforeResolvingKeys()
    {
        var generator = new LocalJwtTokenGenerator(
            Options.Create(new LocalJwtOptions { Enabled = false }),
            TimeProvider.System);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            generator.Generate(new AuthUser { Id = Guid.NewGuid() }, ["Admin"]));

        Assert.Equal("Local JWT token issuance is disabled.", exception.Message);
    }
}
