using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.Infrastructure.Security.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static void AddIdentityOptions(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ExternalRoleMappingOptions>()
            .Bind(configuration.GetSection("Auth:Provisioning"))
            .Validate(options => options.RoleGrantLifetimeMinutes is >= 1 and <= 60,
                "Auth:Provisioning:RoleGrantLifetimeMinutes must be between 1 and 60.")
            .ValidateOnStart();

        services
            .AddOptions<RefreshTokenOptions>()
            .Bind(configuration.GetSection("Auth:Schemes:LocalJwt"))
            .Validate(options => !configuration.GetValue("Auth:Schemes:LocalJwt:Enabled", true) || options.RefreshTokenDays is >= 1 and <= 90 && options.AbsoluteSessionDays is >= 1 and <= 90,
                "RefreshTokenDays and AbsoluteSessionDays must be between 1 and 90.")
            .ValidateOnStart();

        services
            .AddOptions<LocalJwtOptions>()
            .Bind(configuration.GetSection("Auth:Schemes:LocalJwt"))
            .Validate(options => !options.Enabled || options.AccessTokenMinutes is >= 1 and <= 1440,
                "Auth:Schemes:LocalJwt:AccessTokenMinutes must be between 1 and 1440.")
            .ValidateOnStart();

        services
            .AddOptions<RefreshTokenCleanupOptions>()
            .Bind(configuration.GetSection(RefreshTokenCleanupOptions.SectionName))
            .Validate(options => !options.Enabled || options.IntervalMinutes is >= 1 and <= 1440,
                "Auth:RefreshTokenCleanup:IntervalMinutes must be between 1 and 1440 when cleanup is enabled.")
            .Validate(options => !options.Enabled || options.RevokedRetentionDays is >= 0 and <= 365,
                "Auth:RefreshTokenCleanup:RevokedRetentionDays must be between 0 and 365 when cleanup is enabled.")
            .ValidateOnStart();

        services
            .AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection("IdentitySeed"))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AdminEmail),
                "IdentitySeed:AdminEmail must be configured.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AdminPassword),
                "IdentitySeed:AdminPassword must be configured via user-secrets or environment variables.")
            .Validate(
                options => !options.AdminPassword.StartsWith("__SET_VIA_", StringComparison.Ordinal),
                "IdentitySeed:AdminPassword is using a placeholder value. Configure a real password via user-secrets or environment variables.");

    }

    private static void AddIdentity(IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

    }

    private static void AddIdentityServices(IServiceCollection services)
    {
        services.AddScoped<IExternalRoleMapper, ExternalRoleMapper>();
        services.AddExternalIdentityProvisioning();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthUserService, AuthUserService>();
        services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
        services.AddScoped<ILocalJwtTokenGenerator, LocalJwtTokenGenerator>();
        services.AddHostedService<RefreshTokenCleanupBackgroundService>();

    }
}
