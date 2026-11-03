# Skill: New Feature Skeleton (Opinionated)

For every endpoint, describe actual HTTP success/error responses, security, required fields and shared limits in OpenAPI without transport annotations on Application. Extend curated contract tests and exercise the generated-client smoke when the contract changes; keep document/UI mappings Development-only.

Canonical OpenCode file: `.opencode/skills/new-feature-skeleton/SKILL.md`

## Purpose
Liveness must not depend on external services. Readiness timeouts are cooperative: propagate cancellation and provider timeouts; never use `WaitAsync` to abandon checks holding scoped resources or promise a hard deadline for uncooperative providers.

Create a new feature use case with consistent structure across Application, Api, and tests.

This skill is intentionally opinionated: it prioritizes practical consistency over strict architecture purism.

## Inputs
- `FeatureName` (usually plural), for example: `Clients`
- `UseCaseName`, for example: `CreateClient`
- `HttpMethod`: `POST|PUT|PATCH|DELETE|GET`
- `Route` (inside feature group), for example: `/` or `/{id:guid}`
- `AuthPolicy`: `AdminOnly|UserOrAdmin|None`

## Conventions
- Keep folder/namespace patterns:
  - `CleanTemplate.Application.<FeatureName>.Commands.<UseCaseName>`
  - `CleanTemplate.Api.Endpoints`
  - `CleanTemplate.Api.Endpoints.Contracts`
- One public type per file.
- Use `Result` / `Result<T>` for expected business outcomes.
- Requests with validators must return exactly `Result` or `Result<T>`. After the final validator registration call `ValidateRequestValidationConfiguration()` before building the provider; validators run sequentially and can share scoped dependencies.
- Use async handlers with `CancellationToken`.
- Follow ADR-0001: Application uses specific repository/query contracts; EF Core and Dapper stay in Infrastructure.

## Generation Rules

### 1) Application
Create:
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>Command.cs`
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>CommandHandler.cs`
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>CommandValidator.cs`

Reuse or add a specific write repository contract under `Application/Contracts/Persistence` and its EF implementation in Infrastructure. For reads, use a specific query contract (such as `IProductQueries`) under `Application/Contracts/Persistence` and read models with a Dapper implementation. Register new adapters in Infrastructure DI. Never generate Application DbContext extensions, IQueryable contracts or EF/Dapper package references.

### 2) API Contracts
Create:
- `src/CleanTemplate.Api/Endpoints/Contracts/<UseCaseName>Request.cs`

### 3) API Endpoints
Feature endpoint file name uses singular form:
- `Clients` -> `ClientEndpoints.cs`
- `Products` -> `ProductEndpoints.cs`

If endpoint file does not exist:
- create `src/CleanTemplate.Api/Endpoints/<FeatureSingular>Endpoints.cs`
- add `Map<FeatureSingular>Endpoints(this WebApplication app)`

If endpoint file already exists:
- append route mapping + handler method for the new use case in the same file

### 4) Host Wiring
Ensure `src/CleanTemplate.Host/Program.cs` includes:
- `app.Map<FeatureSingular>Endpoints();`

If missing, add it with other endpoint mappings before health checks.

### 5) Tests
Create or extend:
- `test/CleanTemplate.Application.Tests/<FeatureName>/Commands/<UseCaseName>CommandHandlerTests.cs`

At minimum include:
- success path
- failure path(s) when applicable

## Endpoint Mapping Pattern

- Explicitly assign every business endpoint to its version set and version (currently 1.0); preserve `X-Api-Version` and the default/retirement policy in README. Technical endpoints are neutral. Test HTTP selection, endpoint metadata and the actual OpenAPI operation/header, not just the document version.
- Group route: `/api/<feature-lowercase>`
- Endpoint mapping based on method + route input
- Authorization policy:
  - `AdminOnly` -> `.RequireAuthorization("AdminOnly")`
  - `UserOrAdmin` -> `.RequireAuthorization("UserOrAdmin")`
  - `None` -> no policy call

## Error Mapping Pattern
- Use `ToHttpErrorResult()` for failed `Result` values; do not generate ad-hoc error bodies. Validation uses `ValidationProblemDetails`; unexpected failures become 500, not 400.
- Preserve authentication challenge/forbid semantics and headers. Do not put exception messages, secrets or untrusted validation descriptions into HTTP responses. See the HTTP error contract in README.

## Opinionated Decision (Explicit)
ADR-0001 (`docs/adr/0001-data-access-and-dependencies.md`) keeps EF Core writes and Dapper reads in Infrastructure behind specific Application contracts. Handlers await contract methods with cancellation and ConfigureAwait(false); handler tests use repository doubles, not simulated EF async LINQ. Commit using the scoped IUnitOfWork; use its explicit transaction and Result callback for multiple flushes. Return failure for expected business errors to roll back; discard the scope after failure. Flush explicitly with SaveChangesAsync: transaction success does not flush pending changes, which remain tracked and could be saved later.

## Required Validation
Run:
- `dotnet build CleanArchitectureTemplate.slnx`
- `dotnet test CleanArchitectureTemplate.slnx`

Confirm:
- no layer dependency violations
- endpoint mapped in `Program.cs`
- generated use case compiles and tests pass
# Configuration policy

Preserve startup validation: LocalJwt access 1–1440 minutes, refresh and absolute session 1–90 days; unique signing IDs and a valid active ID. Validate enabled capabilities without logging configuration values. Production in-memory SQLite requires explicit `Database:AllowEphemeralInProduction=true`, loses data on restart, and initializes schema without bootstrap. Never restrict external role names to internal roles.

Fixed-window rate limits allow 1–4294967 seconds; provisioning emails and domains must be well formed. Do not initialize SQLite implicitly in Staging or redefine connectivity readiness as a schema check.
