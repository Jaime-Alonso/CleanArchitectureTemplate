# Infrastructure real tests

These tests are excluded from ordinary `dotnet test` runs. Run them explicitly with a working Docker daemon and permission to pull Linux images:

```sh
dotnet test test/CleanTemplate.Infrastructure.RealTests/CleanTemplate.Infrastructure.RealTests.csproj -c Release -p:RunInfrastructureReal=true --filter Category=InfrastructureReal
```

Explicit execution fails if Docker is unavailable; it does not skip or fall back to an in-memory database. Containers use random host ports, no reuse, and are owned and disposed by each test instance. Database configuration is built exclusively in memory; no appsettings, user secrets or environment configuration providers are loaded. Testcontainers itself uses its normal Docker discovery configuration. Images must be available to Docker; the first run may download them.

The ten cases cover SQL Server 2022 and PostgreSQL 17 migrations, EF commits and rollbacks, Dapper reads, optimistic concurrency, concurrent refresh rotation and replay revocation, parallel databases with identical product IDs, and authenticated HTTP reads. Redis 7 cases verify cache hits, removal, isolation of identical keys and prefixes across concurrent containers, and a real server shutdown without invoking or retrying the cache factory. Passwords and database names are generated for each instance. No installed SQL Server, PostgreSQL or Redis service is used. Concurrent rendezvous have bounded waits and propagate sibling failures before disposing owned resources.

To include this suite in the full Release regression run:

```sh
dotnet test CleanArchitectureTemplate.slnx -c Release -p:RunInfrastructureReal=true
```

Without `RunInfrastructureReal=true` the project builds but does not run tests or start containers. The legacy engine acceptance and backup/restore scripts retain their separate opt-ins; this suite does not execute every historical migration acceptance scenario.

The HTTP acceptance runs two simultaneous Testing hosts per database provider, with independent containers and settings supplied in memory before the application entrypoint as well as in final application configuration. It checks the actual DI DbContext provider and database, readiness and independent authenticated reads. Host startup alone is serialized because Serilog has process-wide state; the instances coexist and their reads run in parallel. This is not a production multi-replica or external ingress test. The ordinary HTTP host fixtures were isolated in T29: Testing by default, explicit Development for documentation and Production for production behavior, with per-factory in-memory configuration, database names and JWT keys. See [negative and Production acceptance](../../docs/testing/acceptance.md). The separate `ShutdownCleanupIsolated` trait marks the isolated cooperative shutdown test, which does not require Docker.
