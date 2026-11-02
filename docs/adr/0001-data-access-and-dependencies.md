# ADR-0001: Specific repositories and queries, with infrastructure-owned persistence

- Status: Accepted
- Date: 2026-10-31
- Plan: T-15, T-16, T-13, T-14

## Context

Product commands already use `IProductWriteRepository`; queries use `IProductQueries` and read models. The generic `IApplicationDbContext` has no consumers. The README and feature generator previously described a different model, with EF operators in Application.

## Decision

Keep specific write repositories and read-query contracts in Application. Implement EF Core writes and Dapper reads in Infrastructure. Do not expose `DbContext`, `DbSet`, `IQueryable`, EF operators or Dapper to Application. Do not add a generic repository or a second context abstraction.

Dapper remains in the current base profile: it implements the existing paged projections and SQL dialects without changing the application contracts. It is not currently a selectable generation option. Supporting an EF-only profile would require its own implementation and tests; this ADR does not announce such a profile or certify every database combination for production.

Remove the unused context interface, DI binding, EF package reference in Application and unconsumed EF async-query test utilities. New features follow the existing repository/queries model and test handlers through contract doubles. Database adapter behavior requires relational integration tests, not simulated async LINQ.

## Allowed direct project dependencies

| Project | Allowed direct dependencies |
| --- | --- |
| SharedKernel | None; no packages or framework references |
| Domain | SharedKernel; no packages or framework references |
| Application | Domain, SharedKernel |
| Infrastructure | Application (Domain and SharedKernel are available transitively) |
| Api | Application, SharedKernel |
| Crosscutting | None; technical hosting packages/frameworks only |
| Migrations.SqlServer / Migrations.PostgreSql | Infrastructure |
| Host | Api, Infrastructure, Crosscutting, both migration projects |

Crosscutting is an outer hosting adapter, not part of the pure core. Api must not reference Infrastructure; Host is the composition root. Migration projects are separate deployment details already present in the working tree; this ADR does not change their behavior.

Architecture tests inspect declared project references, packages, framework references and core SDKs, including unused and conditional references. New production projects require an explicit matrix decision. Application's direct packages are limited to Mediora, FluentValidation and logging abstractions. The latter are used by existing handlers and previously arrived transitively through EF Core; they now have an explicit reference. Central versions are not package usage. Tests also protect nullable reference types, implicit usings and warnings-as-errors in shared configuration. These source-level checks complement the existing assembly checks; they are not a full analysis of transitive NuGet dependencies or arbitrary MSBuild evaluation.

## Consequences and remaining work

- Handlers stay independent of persistence technology; Infrastructure owns SQL, dialects, connections and EF mappings.
- Maintaining EF writes plus Dapper reads costs two persistence paths. Keep them behind specific contracts and test both.
- The scoped `IUnitOfWork` owns flushes, sharing the context with write repositories. A single flush is atomic; multiple flushes must be enclosed in `ExecuteInTransactionAsync`, whose callback returns `Result`. A failure result rolls back and invalidates the unit, as do exceptions, cancellation and concurrency failures; discard the scope. Ignoring a failed flush and returning success cannot commit. Flushes are explicit via `SaveChangesAsync`: transaction success does not flush pending changes, which remain tracked and could be saved later. Nested and ambient transactions are rejected. This does not coordinate separate contexts, identity/session workflows, external services or read replicas. Commit acknowledgement failures can leave the database outcome unknown.
- Product reads use DefaultConnection by default. Explicit ProductReads:Mode=Replica and ProductReadReplica select eventual consistency, without read-after-write or bounded lag. Differing legacy read connection strings fail startup rather than silently changing destination; see README for migration and limits. Pagination uses a 64-bit offset and stable ID tie-breaker, not a snapshot across count and rows.
- HTTP error normalization, versioning, OpenAPI and validation pipeline work remain T-17 through T-20.

## Verification

```powershell
dotnet test test/CleanTemplate.ArchitectureTests/CleanTemplate.ArchitectureTests.csproj
dotnet test CleanArchitectureTemplate.slnx
```

The architecture suite includes negative fixtures: a forbidden reference, including a conditional one, and an Application EF dependency must produce violations. No working-tree mutation is necessary to exercise these regressions.

T-13 verification, 2026-11-02: `pwsh -NoProfile -File scripts/tests/Test-VersionedMigrations.ps1` passed sequentially on SQL Server 2022 and PostgreSQL 17, with 11/11 persistence/migration and 3/3 deployment tests per engine, no skips. This includes business failure after a real flush, rollback and scope invalidation, explicit-flush behavior, exceptions, cancellation and ignored concurrency conflict. `dotnet test CleanArchitectureTemplate.slnx -c Release --nologo` passed 249 tests with 12 acceptance tests skipped (executed separately above). Whitespace format verified for the four C# files changed in the final Result-contract change. Codex review completed.

T-14 verification, 2026-11-02: the isolated acceptance script passed 12 persistence/migration and three deployment tests on each engine, including committed EF writes read through Dapper and extreme offsets. HTTP tests cover POST followed by GET and extreme pages on SQLite; configuration tests cover primary selection, explicit replica selection and incompatible legacy settings. Release: 257 passed, 13 engine acceptance tests skipped and executed separately. These checks do not rehearse live replication or measure replica lag. Codex review completed.
