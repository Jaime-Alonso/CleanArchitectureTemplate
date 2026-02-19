using System.Text;
using CleanTemplate.Api.Security.Options;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Api.Security;

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

    public static IReadOnlyCollection<SecurityKey> GetValidationSigningKeys(LocalJwtOptions options)
    {
        if (options.SigningKeys.Count == 0)
        {
            return [GetActiveSigningKey(options)];
        }

        var keys = options.SigningKeys
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => (SecurityKey)new SymmetricSecurityKey(Encoding.UTF8.GetBytes(x.Value))
            {
                KeyId = x.KeyId
            })
            .ToArray();

        return keys.Length > 0 ? keys : [GetActiveSigningKey(options)];
    }
}
