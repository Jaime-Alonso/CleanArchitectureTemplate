using System.Diagnostics;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public sealed class DeploymentAcceptanceTests
{
    [Theory]
    [InlineData("migrate")]
    [InlineData("bootstrap-admin")]
    public async Task OperationWithExtraArgumentsExitsWithoutStartingServer(string operation)
    {
        Assert.NotEqual(0, await RunOperation(operation, "", false, "--urls=http://127.0.0.1:0"));
    }

    [DeploymentFact]
    public async Task RuntimeHasNoDdlOrBootstrapAndConcurrentBootstrapIsSafe()
    {
        var sql = Environment.GetEnvironmentVariable("VERSIONED_MIGRATIONS_TEST_PROVIDER") == "SqlServer";
        var baseConnection = Environment.GetEnvironmentVariable("VERSIONED_MIGRATIONS_TEST_CONNECTION")!;
        var name = "deploy_" + Guid.NewGuid().ToString("N");
        var login = "runtime_" + Guid.NewGuid().ToString("N");
        var password = "aA1!" + Guid.NewGuid().ToString("N");
        var admin = sql ? new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = name, Pooling = false }.ConnectionString
            : new NpgsqlConnectionStringBuilder(baseConnection) { Database = name, Pooling = false }.ConnectionString;
        var runtime = sql ? new SqlConnectionStringBuilder(admin) { UserID = login, Password = password }.ConnectionString
            : new NpgsqlConnectionStringBuilder(admin) { Username = login, Password = password }.ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (sql) options.UseSqlServer(admin, x => x.MigrationsAssembly("CleanTemplate.Migrations.SqlServer"));
        else options.UseNpgsql(admin, x => x.MigrationsAssembly("CleanTemplate.Migrations.PostgreSql"));
        await using var db = new ApplicationDbContext(options.Options);
        await db.GetService<IRelationalDatabaseCreator>().CreateAsync();
        try
        {
            Assert.Equal(0, await RunOperation("migrate", admin, sql));
            // Randomly generated identifiers and password consist only of known safe characters.
#pragma warning disable EF1002 // DDL identifiers and credentials are generated above, never user input.
            if (sql)
            {
                await db.Database.ExecuteSqlRawAsync($"CREATE LOGIN [{login}] WITH PASSWORD = '{password}'; CREATE USER [{login}] FOR LOGIN [{login}]; ALTER ROLE db_datareader ADD MEMBER [{login}]; ALTER ROLE db_datawriter ADD MEMBER [{login}];");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync($"CREATE ROLE \"{login}\" LOGIN PASSWORD '{password}'; GRANT CONNECT ON DATABASE \"{name}\" TO \"{login}\"; GRANT USAGE ON SCHEMA public TO \"{login}\"; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \"{login}\";");
            }
#pragma warning restore EF1002

            // Prove the principal cannot execute DDL, rather than inferring from grants.
            await using (var limited = sql ? (System.Data.Common.DbConnection)new SqlConnection(runtime) : new NpgsqlConnection(runtime))
            {
                await limited.OpenAsync();
                await using var command = limited.CreateCommand();
                command.CommandText = "CREATE TABLE forbidden_ddl (id int)";
                await Assert.ThrowsAnyAsync<Exception>(() => command.ExecuteNonQueryAsync());
            }

            await using (var first = StartRuntime(runtime, sql))
            await using (var second = StartRuntime(runtime, sql))
            {
                await WaitForReady(first);
                await WaitForReady(second);
                Assert.Equal(0, await db.Users.CountAsync());
            }

            var bootstraps = await Task.WhenAll(RunOperation("bootstrap-admin", runtime, sql), RunOperation("bootstrap-admin", runtime, sql));
            Assert.Single(bootstraps, code => code == 0);
            Assert.Single(bootstraps, code => code != 0);
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(1, await db.Roles.CountAsync(role => role.Name == "Admin"));
            Assert.Equal(1, await db.UserRoles.CountAsync());
            Assert.Equal(1, await (from userRole in db.UserRoles
                                   join role in db.Roles on userRole.RoleId equals role.Id
                                   where role.Name == "Admin"
                                   select userRole).CountAsync());

            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AspNetUserRoles\"");
            await using (var restarted = StartRuntime(runtime, sql))
            {
                await WaitForReady(restarted);
                Assert.Equal(0, await db.UserRoles.CountAsync());
            }
            Assert.NotEqual(0, await RunOperation("bootstrap-admin", runtime, sql));
            Assert.Equal(0, await db.UserRoles.CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if (sql)
            {
                await using var server = new SqlConnection(baseConnection);
                await server.OpenAsync();
                await using var command = server.CreateCommand();
                command.CommandText = $"DROP LOGIN [{login}]";
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                await using var server = new NpgsqlConnection(baseConnection);
                await server.OpenAsync();
                await using var command = server.CreateCommand();
                command.CommandText = $"DROP ROLE IF EXISTS \"{login}\"";
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static ProcessStartInfo Info(string connection, bool sql)
    {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(typeof(Program).Assembly.Location);
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        info.Environment["DOTNET_ENVIRONMENT"] = "Production";
        info.Environment["DatabaseProvider"] = sql ? "SqlServer" : "PostgreSql";
        info.Environment["ConnectionStrings__DefaultConnection"] = connection;
        info.Environment["ConnectionStrings__DapperConnection"] = connection;
        info.Environment["ConnectionStrings__ReadConnection"] = connection;
        info.Environment["Auth__Schemes__LocalJwt__SigningKey"] = "test-only-signing-key-12345678901234567890";
        info.Environment["Auth__RefreshTokenCleanup__Enabled"] = "false";
        info.Environment["Serilog__MinimumLevel__Override__Microsoft.Hosting.Lifetime"] = "Information";
        info.Environment.Remove("IdentitySeed__AdminPassword");
        return info;
    }

    private static async Task<int> RunOperation(string operation, string connection, bool sql, params string[] extraArguments)
    {
        var info = Info(connection, sql);
        info.ArgumentList.Add(operation);
        foreach (var argument in extraArguments) info.ArgumentList.Add(argument);
        if (operation == "bootstrap-admin") info.Environment["IdentitySeed__AdminPassword"] = "test-only-Password_123!";
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        var output = await Task.WhenAll(stdout, stderr);
        if (extraArguments.Length > 0)
            Assert.DoesNotContain("Now listening on", string.Join('\n', output), StringComparison.Ordinal);
        return process.ExitCode;
    }

    private static RunningProcess StartRuntime(string connection, bool sql)
    {
        var info = Info(connection, sql);
        info.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        return new RunningProcess(Process.Start(info)!);
    }

    private static async Task WaitForReady(RunningProcess running)
    {
        for (var i = 0; i < 100; i++)
        {
            if (running.Process.HasExited) throw new InvalidOperationException("Runtime exited before startup.");
            lock (running.Output)
            {
                if (running.Output.ToString().Contains("Now listening on", StringComparison.Ordinal)) return;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException("Runtime did not start.");
    }

    private sealed class RunningProcess : IAsyncDisposable
    {
        public Process Process { get; }
        public System.Text.StringBuilder Output { get; } = new();
        public RunningProcess(Process process)
        {
            Process = process;
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (Output) Output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited) { Process.Kill(true); await Process.WaitForExitAsync(); }
            Process.Dispose();
        }
    }

    private sealed class DeploymentFactAttribute : FactAttribute
    {
        public DeploymentFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VERSIONED_MIGRATIONS_TEST_PROVIDER")))
                Skip = "Run scripts/tests/Test-VersionedMigrations.ps1 for isolated engine acceptance.";
        }
    }
}
