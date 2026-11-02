using System.Diagnostics;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public sealed class StartupProcessTests
{
    [Fact]
    public async Task InvalidConfiguration_ExitsWithFailureAndDoesNotLogSecrets()
    {
        const string secret = "startup-secret-do-not-log-78264";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        // The provider parser includes this invalid value in its exception message.
        startInfo.Environment["DatabaseProvider"] = secret;
        startInfo.Environment["Auth__Schemes__LocalJwt__SigningKey"] = secret;
        startInfo.Environment["IdentitySeed__AdminPassword"] = secret;

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        var output = await stdout + await stderr;
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Application terminated unexpectedly", output);
        Assert.Contains("Check the startup configuration and dependencies", output);
        Assert.DoesNotContain(secret, output);
    }
}
