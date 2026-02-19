using System.Text;
using CleanTemplate.Infrastructure.Security.Options;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Infrastructure.Security;

public static class LocalJwtSigningKeyResolver
{
    public static SecurityKey GetActiveSigningKey(LocalJwtOptions options)
    {
        if (options.SigningKeys.Count == 0)
        {
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey))
            {
                KeyId = options.ActiveSigningKeyId
            };
        }

        var activeKey = options.SigningKeys.First(x => string.Equals(x.KeyId, options.ActiveSigningKeyId, StringComparison.Ordinal));
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(activeKey.Value))
        {
            KeyId = activeKey.KeyId
        };
    }
}
