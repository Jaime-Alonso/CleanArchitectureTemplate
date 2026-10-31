using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Security;

public sealed class ExternalRoleMapperTests
{
    [Theory]
    [InlineData(false, "User")]
    [InlineData(true, "Admin")]
    public void MapRoles_EmailRulesRequireExplicitVerification(bool verified, string expectedRole)
    {
        var mapper = BuildMapper(new ExternalRoleMappingOptions
        {
            Admin = new ExternalRoleMappingRuleOptions { Emails = ["admin@example.test"], Domains = ["example.test"] }
        });

        var roles = mapper.MapRoles(new ExternalIdentity
        {
            Provider = "Oidc",
            Issuer = "https://identity.test",
            ProviderSubject = "subject",
            Email = "admin@example.test",
            EmailVerified = verified
        });

        Assert.Equal([expectedRole], roles);
    }

    [Fact]
    public void MapRoles_WhenAdminEmailMatches_ReturnsAdmin()
    {
        var mapper = BuildMapper(new ExternalRoleMappingOptions
        {
            DefaultRole = "User",
            Admin = new ExternalRoleMappingRuleOptions
            {
                Emails = ["admin@contoso.com"]
            }
        });

        var identity = new ExternalIdentity
        {
            Provider = "Oidc",
            Issuer = "https://identity.test",
            ProviderSubject = "sub-1",
            EmailVerified = true,
            Email = "admin@contoso.com"
        };

        var roles = mapper.MapRoles(identity);

        Assert.Equal(["Admin"], roles);
    }

    [Fact]
    public void MapRoles_WhenNoRuleMatches_ReturnsDefaultRole()
    {
        var mapper = BuildMapper(new ExternalRoleMappingOptions
        {
            DefaultRole = "User"
        });

        var identity = new ExternalIdentity
        {
            Provider = "Oidc",
            Issuer = "https://identity.test",
            ProviderSubject = "sub-2",
            Email = "john@unknown.com"
        };

        var roles = mapper.MapRoles(identity);

        Assert.Equal(["User"], roles);
    }

    private static ExternalRoleMapper BuildMapper(ExternalRoleMappingOptions options)
    {
        return new ExternalRoleMapper(Options.Create(options));
    }
}
