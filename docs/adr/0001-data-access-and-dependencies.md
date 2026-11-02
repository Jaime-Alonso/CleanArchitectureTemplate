# ADR-0001: Specific repositories and queries, with infrastructure-owned persistence

- Status: Accepted
- Date: 2026-10-31
- Plan: T-15, T-16; follow-up T-13 and T-14

## Context

Product commands already use `IProductWriteRepository`; queries use `IProductReadRepository` and read models. The generic `IApplicationDbContext` has no consumers. The README and feature generator previously described a different model, with EF operators in Application.

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
- `SaveChangesAsync` remains on the write repository for compatibility in this increment. **T-13 is still pending**: move commit ownership to an explicit application unit of work and prove rollback for composed changes. This ADR does not claim atomicity across multiple repository commits.
- ReadConnection fallback and replica/read-after-write guarantees remain **T-14**, not resolved by keeping Dapper.
- HTTP error normalization, versioning, OpenAPI and validation pipeline work remain T-17 through T-20.

## Verification

```powershell
dotnet test test/CleanTemplate.ArchitectureTests/CleanTemplate.ArchitectureTests.csproj
dotnet test CleanArchitectureTemplate.slnx
```

The architecture suite includes negative fixtures: a forbidden reference, including a conditional one, and an Application EF dependency must produce violations. No working-tree mutation is necessary to exercise these regressions.
