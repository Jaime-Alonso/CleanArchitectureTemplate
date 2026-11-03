using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public sealed class StartupProcessTests
{
    [Fact]
    public async Task OwnedStdoutIsJsonAndDoesNotContainRequestValues()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var info = CreateIsolatedStartInfo();
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        using var host = Process.Start(info)!;
        var stdout = host.StandardOutput.ReadToEndAsync();
        var stderr = host.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ready = false;
            while (!timeout.IsCancellationRequested && !host.HasExited)
            {
                try
                {
                    using var response = await client.GetAsync($"http://127.0.0.1:{port}/health/ready", timeout.Token);
                    if (response.IsSuccessStatusCode) { ready = true; break; }
                }
                catch (HttpRequestException) { }
                await Task.Delay(200, timeout.Token);
            }
            Assert.True(ready, "Host did not become ready");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/CANARY_PATH?x=CANARY_QUERY");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer CANARY_AUTH");
            request.Headers.TryAddWithoutValidation("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
            request.Content = new StringContent("CANARY_BODY");
            using var result = await client.SendAsync(request, timeout.Token);
        }
        finally
        {
            await StopAsync(host);
        }
        var output = await stdout;
        _ = await stderr;
        Assert.DoesNotContain("CANARY_", output);
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        var found = false;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var properties = root.GetProperty("Properties");
            Assert.Equal(version, properties.GetProperty("ServiceVersion").GetString());
            if (root.GetProperty("MessageTemplate").GetString()!.StartsWith("HTTP request {Outcome}", StringComparison.Ordinal)
                && properties.TryGetProperty("TraceId", out var trace) && trace.GetString() == "0123456789abcdef0123456789abcdef")
            {
                Assert.Equal("unmatched", properties.GetProperty("Route").GetString());
                Assert.False(string.IsNullOrEmpty(properties.GetProperty("SpanId").GetString()));
                found = true;
            }
        }
        Assert.True(found, "No correlated completion event");
    }

    [Theory]
    [InlineData("Auth__Schemes__LocalJwt__AccessTokenMinutes", "not-a-number", "Auth:Schemes:LocalJwt:AccessTokenMinutes")]
    [InlineData("ForwardedHeaders__KnownProxies__0", "http://secret.invalid", "ForwardedHeaders:KnownProxies")]
    [InlineData("Cors__AllowedOrigins__0", "https://user:secret@invalid.example", "Cors")]
    [InlineData("Database__AllowEphemeralInProduction", "not-a-bool", "Database:AllowEphemeralInProduction")]
    [InlineData("Auth__RefreshTokenCleanup__IntervalMinutes", "0", "Auth:RefreshTokenCleanup")]
    [InlineData("DatabaseProvider", "", "DatabaseProvider")]
    public async Task InvalidConfiguration_ReportsSafeKey(string key, string value, string expectedKey)
    {
        var info = CreateIsolatedStartInfo();
        info.Environment[key] = value;
        if (key == "Auth__RefreshTokenCleanup__IntervalMinutes") info.Environment["Auth__RefreshTokenCleanup__Enabled"] = "true";
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(expectedKey, output);
            if (value.Length > 0 && value != "0") Assert.DoesNotContain(value, output);
        }
        finally { await StopAsync(process); }
    }

    [Fact]
    public async Task ProductionEphemeral_RequiresOptIn_AndInitializesSchema()
    {
        var rejected = CreateIsolatedStartInfo();
        rejected.Environment.Remove("Database__AllowEphemeralInProduction");
        using (var process = Process.Start(rejected)!)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains("Database:AllowEphemeralInProduction", await output + await error);
            }
            finally { await StopAsync(process); }
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var info = CreateIsolatedStartInfo();
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        using var host = Process.Start(info)!;
        var stdout = host.StandardOutput.ReadToEndAsync();
        var stderr = host.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!timeout.IsCancellationRequested && !host.HasExited)
            {
                try
                {
                    using var response = await client.GetAsync($"http://127.0.0.1:{port}/health/ready", timeout.Token);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using var login = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/auth/login", new { Email = "nobody@example.com", Password = "wrong" }, timeout.Token);
                        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
                        return;
                    }
                }
                catch (HttpRequestException) { }
                await Task.Delay(200, timeout.Token);
            }
            Assert.Fail($"Host did not become ready. Exit: {host.HasExited}; output: {(host.HasExited ? await stdout + await stderr : "running")}");
        }
        finally
        {
            await StopAsync(host);
        }
    }

    private static ProcessStartInfo CreateIsolatedStartInfo()
    {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = AppContext.BaseDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(typeof(Program).Assembly.Location);
        var inherited = new[] { "PATH", "SystemRoot", "DOTNET_ROOT", "HOME", "USERPROFILE", "TEMP", "TMP" };
        var values = inherited.ToDictionary(x => x, Environment.GetEnvironmentVariable);
        info.Environment.Clear();
        foreach (var (key, value) in values)
            if (value is not null) info.Environment[key] = value;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        info.Environment["DOTNET_ENVIRONMENT"] = "Production";
        info.Environment["DatabaseProvider"] = "SqliteInMemory";
        info.Environment["Database__AllowEphemeralInProduction"] = "true";
        info.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        info.Environment["Auth__Schemes__LocalJwt__SigningKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        info.Environment["Auth__RefreshTokenCleanup__Enabled"] = "false";
        info.Environment["OpenTelemetry__Traces__Enabled"] = "false";
        info.Environment["OpenTelemetry__Metrics__Enabled"] = "false";
        return info;
    }

    [Fact]
    public async Task InvalidConfiguration_ExitsWithFailureAndDoesNotLogSecrets()
    {
        const string secret = "startup-secret-do-not-log-78264";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
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
        finally
        {
            await StopAsync(process);
        }

        var output = await stdout + await stderr;
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("DatabaseProvider", output);
        Assert.DoesNotContain(secret, output);
    }

    private static async Task StopAsync(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
}
