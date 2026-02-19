using System.Security.Claims;
using CleanTemplate.Api.Security.Options;
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

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddPolicyScheme("Bearer", "Bearer", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authOptions = context.RequestServices.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue;
                    return SelectScheme(context, authOptions);
                };
            })
            .AddJwtBearer("LocalJwt", _ => { })
            .AddJwtBearer("Oidc", _ => { });

        services
            .AddOptions<JwtBearerOptions>("LocalJwt")
            .Configure<IOptions<AuthOptions>>((jwtOptions, authOptions) =>
                ConfigureLocalJwt(jwtOptions, authOptions.Value.Schemes.LocalJwt));

        services
            .AddOptions<JwtBearerOptions>("Oidc")
            .Configure<IOptions<AuthOptions>>((jwtOptions, authOptions) =>
                ConfigureOidcJwt(jwtOptions, authOptions.Value.Schemes.Oidc));

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("User", "Admin"));
        });

        services.AddTransient<IClaimsTransformation, ProvisioningClaimsTransformation>();

        return services;
    }

    private static string SelectScheme(HttpContext context, AuthOptions authOptions)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authOptions.Schemes.LocalJwt.Enabled ? "LocalJwt" : "Oidc";
        }

        var token = authorization["Bearer ".Length..].Trim();

        try
        {
            var jwtToken = new JsonWebTokenHandler().ReadJsonWebToken(token);
            if (string.Equals(jwtToken.Issuer, authOptions.Schemes.LocalJwt.Issuer, StringComparison.OrdinalIgnoreCase))
            {
                return "LocalJwt";
            }

            return "Oidc";
        }
        catch
        {
            return authOptions.Schemes.LocalJwt.Enabled ? "LocalJwt" : "Oidc";
        }
    }

    private static void ConfigureLocalJwt(JwtBearerOptions options, LocalJwtOptions localJwt)
    {
        if (!localJwt.Enabled)
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = false,
                ValidateLifetime = false
            };
            return;
        }

        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
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
        if (!oidc.Enabled)
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = false,
                ValidateLifetime = false
            };
            return;
        }

        options.Authority = oidc.Authority;
        options.Audience = oidc.Audience;
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "sub"
        };
    }
}
