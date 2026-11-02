using System.Security.Claims;
using CleanTemplate.Api.Security.Options;
using CleanTemplate.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Api.Security.Extensions;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services
            .AddOptions<AuthOptions>()
            .Bind(configuration.GetSection("Auth"))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();

        // Scheme registration and routing are a startup snapshot. Changing Enabled requires a restart.
        var configuredOptions = configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

        var authentication = services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddPolicyScheme("Bearer", "Bearer", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    return SelectScheme(context, configuredOptions);
                };
            })
            .AddScheme<AuthenticationSchemeOptions, RejectedBearerHandler>(RejectedBearerHandler.SchemeName, _ => { });

        if (configuredOptions.Schemes.LocalJwt.Enabled)
        {
            authentication.AddJwtBearer("LocalJwt", _ => { });
            services
                .AddOptions<JwtBearerOptions>("LocalJwt")
                .Configure<IOptions<AuthOptions>>((jwtOptions, authOptions) =>
                    ConfigureLocalJwt(jwtOptions, authOptions.Value.Schemes.LocalJwt));
        }

        if (configuredOptions.Schemes.Oidc.Enabled)
        {
            authentication.AddJwtBearer("Oidc", _ => { });
            services
                .AddOptions<JwtBearerOptions>("Oidc")
                .Configure<IOptions<AuthOptions>>((jwtOptions, authOptions) =>
                    ConfigureOidcJwt(jwtOptions, authOptions.Value.Schemes.Oidc));
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasInternalRole(context.User, "Admin")));
            options.AddPolicy("UserOrAdmin", policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasInternalRole(context.User, "User", "Admin")));
        });

        services.AddTransient<IClaimsTransformation, ProvisioningClaimsTransformation>();

        return services;
    }

    private static string SelectScheme(HttpContext context, AuthOptions authOptions)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return RejectedBearerHandler.SchemeName;
        }

        var token = authorization["Bearer ".Length..].Trim();

        try
        {
            var jwtToken = new JsonWebTokenHandler().ReadJsonWebToken(token);
            // Reading iss only selects a validator; it never establishes trust in the token.
            if (string.Equals(jwtToken.Issuer, authOptions.Schemes.LocalJwt.Issuer, StringComparison.Ordinal))
            {
                return authOptions.Schemes.LocalJwt.Enabled ? "LocalJwt" : RejectedBearerHandler.SchemeName;
            }

            return authOptions.Schemes.Oidc.Enabled ? "Oidc" : RejectedBearerHandler.SchemeName;
        }
        catch
        {
            return RejectedBearerHandler.SchemeName;
        }
    }

    private static bool HasInternalRole(ClaimsPrincipal principal, params string[] roles)
    {
        return principal.Identities.OfType<InternalClaimsIdentity>()
            .Any(identity => identity.IsAuthenticated && roles.Any(role => identity.HasClaim(InternalClaimTypes.Role, role)));
    }

    private static void ConfigureLocalJwt(JwtBearerOptions options, LocalJwtOptions localJwt)
    {
        options.RequireHttpsMetadata = true;
        options.IncludeErrorDetails = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            AuthenticationType = "LocalJwt",
            ValidateIssuer = true,
            ValidIssuer = localJwt.Issuer,
            ValidateAudience = true,
            ValidAudience = localJwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, _, _) => LocalJwtSigningKeyResolver.GetValidationSigningKeys(localJwt),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "sub"
        };
    }

    private static void ConfigureOidcJwt(JwtBearerOptions options, OidcOptions oidc)
    {
        options.Authority = oidc.Authority;
        options.Audience = oidc.Audience;
        options.RequireHttpsMetadata = true;
        options.IncludeErrorDetails = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            AuthenticationType = "Oidc",
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "sub"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (oidc.AllowedTenantIds.Count > 0)
                {
                    var tenants = context.Principal?.Claims.Where(claim => claim.Type == oidc.Claims.TenantId).Take(2).ToArray() ?? [];
                    if (tenants.Length != 1 || !oidc.AllowedTenantIds.Contains(tenants[0].Value, StringComparer.Ordinal))
                    {
                        context.Fail("The token tenant is not allowed.");
                    }
                }

                return Task.CompletedTask;
            }
        };
    }
}
