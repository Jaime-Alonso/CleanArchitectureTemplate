using CleanTemplate.Api.Endpoints.Contracts.Auth;
using CleanTemplate.Api.Extensions;
using CleanTemplate.Application.Security.Commands.Login;
using CleanTemplate.Application.Security.Commands.Logout;
using CleanTemplate.Application.Security.Commands.RefreshToken;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CleanTemplate.Api.Endpoints;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", Login)
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicies.Login)
            .WithName("Login");

        group.MapPost("/refresh", Refresh)
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicies.Refresh)
            .WithName("RefreshToken");

        group.MapPost("/logout", Logout)
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicies.Logout)
            .WithName("Logout");

        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        HttpContext httpContext,
        TimeProvider timeProvider,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new LoginCommand
        {
            EmailOrUserName = request.Email,
            Password = request.Password,
            ClientIp = httpContext.Connection.RemoteIpAddress?.ToString()
        };

        var sessionResult = await sender.Send(command, cancellationToken);

        if (sessionResult.IsFailure)
        {
            return sessionResult.ToHttpErrorResult(_ => Results.Unauthorized());
        }

        var session = sessionResult.Value;

        return Results.Ok(new LoginResponse
        {
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            TokenType = "Bearer",
            ExpiresIn = (int)Math.Max(1, (session.AccessTokenExpiresAtUtc - timeProvider.GetUtcNow().UtcDateTime).TotalSeconds),
            RefreshTokenExpiresAtUtc = session.RefreshTokenExpiresAtUtc
        });
    }

    private static async Task<IResult> Refresh(
        RefreshTokenRequest request,
        HttpContext httpContext,
        TimeProvider timeProvider,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new RefreshTokenCommand
        {
            RefreshToken = request.RefreshToken,
            ClientIp = httpContext.Connection.RemoteIpAddress?.ToString()
        };

        var sessionResult = await sender.Send(command, cancellationToken);

        if (sessionResult.IsFailure)
        {
            return sessionResult.ToHttpErrorResult(_ => Results.Unauthorized());
        }

        var session = sessionResult.Value;

        return Results.Ok(new LoginResponse
        {
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            TokenType = "Bearer",
            ExpiresIn = (int)Math.Max(1, (session.AccessTokenExpiresAtUtc - timeProvider.GetUtcNow().UtcDateTime).TotalSeconds),
            RefreshTokenExpiresAtUtc = session.RefreshTokenExpiresAtUtc
        });
    }

    private static async Task<IResult> Logout(
        RefreshTokenRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new LogoutCommand
        {
            RefreshToken = request.RefreshToken,
            ClientIp = httpContext.Connection.RemoteIpAddress?.ToString()
        };

        await sender.Send(command, cancellationToken);

        return Results.NoContent();
    }
}
