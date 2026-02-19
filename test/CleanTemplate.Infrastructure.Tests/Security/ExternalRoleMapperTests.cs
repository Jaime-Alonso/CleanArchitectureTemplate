using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Security;

public sealed class ExternalRoleMapperTests
{
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
            ProviderSubject = "sub-1",
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
