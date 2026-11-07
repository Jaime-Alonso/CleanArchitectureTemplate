# Clean Architecture Simple Template (.NET) — Minimal API + Mediora

This repository provides a **solution template** for starting .NET projects using a **simple Clean Architecture**, built with **Minimal API** and **Mediora**, without additional modular complexity.

The template is designed to be:

- Easy to understand and maintain
- Scalable without over-engineering
- Aligned with SOLID principles and clean code practices

---

## Recovery and resilience

The operator owns encrypted backups, separate key and server-level identity recovery, restore drills, retention and RPO/RTO decisions. See [recovery operations](docs/recovery/operations.md) for proposed budgets, cooperative shutdown, cache failures and the isolated drill's evidence and limits. Redis is not a permission fallback; cache factories must be read-only/idempotent because concurrent or repeated attempts can run them again. Do not automatically retry ambiguous writes or refresh rotation.

## HTTP error contract

Error responses use `application/problem+json` regardless of the request's `Accept` header, preserving the original HTTP status (`ProblemDetails`, or `ValidationProblemDetails` with an `errors` object for validation and binding failures). They include the HTTP `status`, a stable `code`, and a request-specific `traceId`. Validation keys identify fields when available; the empty key denotes an unassociated error. Messages are deliberately generic except for explicit, safe endpoint validation messages. JSON binding paths use JSON property names; Application validation uses its property names. Unauthorized Result, login, and refresh failures include a `WWW-Authenticate: Bearer` challenge without exposing token failure details.

| Status | `code` |
| --- | --- |
| 400 | `request.invalid` |
| 401 | `auth.unauthorized` |
| 403 | `auth.forbidden` |
| 404 | `resource.not_found` |
| 409 | `resource.conflict` |
| 429 | `rate_limit.exceeded` |
| 5xx | `server.error` |

Other 4xx responses use `request.error`. Authentication challenges and forbids remain handled by the configured schemes; `WWW-Authenticate` is retained, without token validation details. Rate-limit responses retain `Retry-After` when provided by the limiter. Unexpected failures and exception messages are not returned to clients. Cancellation or a response already started may prevent an error body from being written.

**Breaking change:** clients must replace the previous `{ errors: [ ... ] }` Result envelope and `{ error: "rate_limit_exceeded", detail: ... }` limiter envelope with the contract above; do not rely on internal error descriptions or codes. `ExpectedVersion` validation remains a 400 with an `expectedVersion` field; stale versions remain 409. OpenAPI completeness is tracked separately.

### API operation versions

Product operations and, when local JWT is enabled, `/auth/login`, `/auth/refresh` and `/auth/logout` explicitly belong to API version **1.0**. Existing paths are unchanged. Select it with `X-Api-Version: 1.0`; an absent header defaults to 1.0 for compatibility. An unsupported, malformed or ambiguous version returns 400 as `application/problem+json` with `code` and `traceId`, regardless of `Accept`. Multiple distinct values are ambiguous. Version selection happens at routing: a rejected version can precede authentication/authorization and endpoint-specific rate limiting; clients must not rely on a 401 or 429 for such requests. Health, OpenAPI and Scalar routes are technical and version-neutral. OpenAPI `info.version` names the document, not proof of operation membership; the v1 document lists each business operation with its actual header parameter.

The Development OpenAPI document includes the HTTP Bearer scheme for authorized operations and documents 429 responses, including the optional `Retry-After` header. A 400 can be ordinary ProblemDetails (including version errors) or validation ProblemDetails with `errors`; the document uses `anyOf` because these shapes overlap. Product limits are described in the contract; string length limits are measured by the server in UTF-16 code units, whereas JSON Schema implementations may count Unicode characters differently. The document and Scalar UI are not mapped in Production. A generated-client smoke is described below.

### Generated client smoke

With .NET 10, PowerShell 7, and NuGet access, install the pinned tool locally and run:

```powershell
dotnet tool install NSwag.ConsoleCore --version 14.7.1 --tool-path work/tools
pwsh -NoProfile -File scripts/tests/Test-OpenApiClient.ps1
```

The script builds and starts a separate Development host, generates a C# client from its unmodified `/openapi/v1.json`, builds it and runs login and product create/read/update/delete/404 using Bearer and `X-Api-Version: 1.0`. It checks generated decimal prices and Int64 versions. It uses random environment-only secrets and a uniquely named in-memory SQLite database; the host process is stopped in `finally`. Generated files and the tool are ignored under `work/`. Each run has its own artifact directory. The local port is chosen dynamically. No global tool is installed.

Verified with NSwag 14.7.1 (NJsonSchema 11.6.1), .NET 10 and the emitted OpenAPI **3.1.1**: generation, build and all smoke calls passed. No 3.0 downgrade or document postprocessing was needed for this limited flow. This does not validate every schema constraint, error response, generator, or client use case. On this environment `--allow-roll-forward` produced an invalid NSwag runtime configuration; it is not needed when the .NET 10 runtime is installed.

The smoke runs a child host with primary reads, in-memory caching and OIDC disabled; it clears inherited read-replica and legacy read connections in that child. It exercises high-precision decimal values and a stale-version 409 without printing credentials.

Only 1.0 is currently supported and none is deprecated. When introducing a breaking contract, explicitly map each operation to its supported versions and publish matching documents; do not silently move the default. Announce deprecation and migration guidance before marking a version deprecated, report it with `api-deprecated-versions`, and remove it only after an explicitly communicated retirement decision. No retirement date or support duration is promised here. Removing 1.0 or changing the absent-header default is a breaking change requiring a compatibility decision. `api-supported-versions` reports supported versions on matched business responses.

## Before You Start

> [!IMPORTANT]
> For the default ephemeral Development database, configure a JWT signing key and admin seed password outside the repository.
> Persistent deployments use explicit migration and bootstrap operations; runtime does not require the bootstrap password. See [deployment operations](docs/database/deployment-operations.md).

With PowerShell 7.2 or later and the .NET SDK, initialize unique local values:

```powershell
.\scripts\Initialize-DevelopmentSecrets.ps1
dotnet run --project src/CleanTemplate.Host/CleanTemplate.Host.csproj --environment Development
```

The script uses cryptographic randomness, writes JSON through stdin to `dotnet user-secrets`, never prints secret values, and preserves existing values. The Host includes a `UserSecretsId`, so ASP.NET Core loads them automatically in Development.

Inspect the generated password locally with `dotnet user-secrets --project src/CleanTemplate.Host/CleanTemplate.Host.csproj list` **only in a private terminal**; that command displays sensitive values. Alternatively, set your own password through your local secret-management tooling. User-secrets are not encrypted and are not a production secret store.

Use `-Rotate` only deliberately: replacing the signing key invalidates existing access tokens. Updating the seed password does not change the password of an existing database account. For production, use environment variables or a managed secret store.

The bootstrap can be tested without altering your own secrets:

```powershell
.\scripts\tests\Test-DevelopmentSecrets.ps1
```

### Rename

There is currently no Visual Studio project template package (`.vstemplate`) available for this repository.

To rename the solution, projects, folders, and namespaces safely, use the PowerShell renaming script included in this repo:

```powershell
# Preview changes only
.\scripts\Rename-Template.ps1 -NewPrefix "MyApp" -NewSolutionName "MyAppArchitecture" -DryRun

# Apply changes
.\scripts\Rename-Template.ps1 -NewPrefix "MyApp" -NewSolutionName "MyAppArchitecture"
```

After applying changes, run:

```powershell
dotnet build .\MyAppArchitecture.slnx
dotnet test .\MyAppArchitecture.slnx
```

---

## Solution Projects

The generated solution includes the following projects:

- `MyApp.SharedKernel` _(pure primitives)_
- `MyApp.Crosscutting` _(outer-layer hosting concerns, not part of the core)_
- `MyApp.Domain`
- `MyApp.Application`
- `MyApp.Infrastructure`
- `MyApp.Api`
- `MyApp.Host`
- `MyApp.Migrations.SqlServer` / `MyApp.Migrations.PostgreSql`

And one or more test projects, for example:

- `MyApp.Domain.Tests`
- `MyApp.Application.Tests`
- `MyApp.Infrastructure.Tests`
- `MyApp.ArchitectureTests`
- `MyApp.Api.IntegrationTests`

---

## Visual Studio Solution Folders and Why This Order

In Visual Studio, **Solution Folders** (organizational only, not physical folders) are created in this order:

1. **Shared Kernel**
2. **Crosscutting** (hosting utilities; not an inner layer)
3. **Domain**
4. **Application**
5. **Infrastructure** (including provider migration projects)
6. **Api**
7. **Host**
8. **Tests**

### Why this enumeration?

The order reflects the **dependency rule of Clean Architecture**:

> Dependencies must always point inward, toward the core.

- **SharedKernel** is the innermost shared base with pure primitives such as `Result`, `Error` and `Guard`. **Crosscutting** is a separate technical hosting project, not a domain dependency.
- **Domain** contains business rules and invariants and should only consume `SharedKernel` primitives, never outer layers.

- **Application** implements use cases (Mediora handlers), orchestrates domain logic, and defines contracts (interfaces) to be implemented by infrastructure.  
  It depends on `Domain`.

- **Infrastructure** implements technical details (EF Core, repositories, external integrations).  
  It depends on `Application` (to implement its interfaces) and possibly `Domain`.

- **Api** contains the HTTP contract and endpoint surface (Minimal API endpoint mapping and request/response contracts).  
  It depends on `Application`.

- **Host** is the executable entry point that composes `Api`, `Infrastructure`, `Crosscutting`, and provider migration assemblies.
- **Tests** are placed last to clearly separate production code from verification concerns.

Visually, this ordering reinforces the architectural message:  
**Core first, details later.**

---

## Dependency Overview

- `SharedKernel` → no project, package or framework dependencies
- `Domain` → `SharedKernel`
- `Application` → `Domain` + `SharedKernel`; no EF Core or Dapper
- `Infrastructure` → `Application`
- `Api` → `Application` + `SharedKernel`; no Infrastructure reference
- `Crosscutting` → no project dependencies; technical hosting packages/frameworks
- `Migrations.SqlServer` / `Migrations.PostgreSql` → `Infrastructure`
- `Host` → `Api` + `Infrastructure` + `Crosscutting` + provider migration projects
- `Tests` → depend on the projects they test

The declared dependency matrix and essential build conventions are checked by `CleanTemplate.ArchitectureTests`, including conditional and unused project references. Run it with `dotnet test test/CleanTemplate.ArchitectureTests/CleanTemplate.ArchitectureTests.csproj`. See [ADR-0001](docs/adr/0001-data-access-and-dependencies.md) for the decision and verification limits.

Real SQL Server, PostgreSQL and Redis tests are opt-in and require Docker: `dotnet test test/CleanTemplate.Infrastructure.RealTests/CleanTemplate.Infrastructure.RealTests.csproj -c Release -p:RunInfrastructureReal=true --filter Category=InfrastructureReal`. Docker failures are test failures, not skips; see [the real-test guide](test/CleanTemplate.Infrastructure.RealTests/README.md) for isolation and HTTP acceptance scope. Existing legacy integration factories are not all Testing; their migration is T29. The isolated cleanup shutdown test is labeled `ShutdownCleanupIsolated`.

## Requirements

- .NET SDK installed
- (Optional) Visual Studio 2026 or later

---

### Architectural Ordering (Core First)

The solution folders are intentionally ordered to reflect **Clean Architecture dependency rules**:

1. Shared Kernel
2. Crosscutting (outer hosting utilities)
3. Domain
4. Application
5. Infrastructure
6. Api
7. Host
8. Tests

This order emphasizes that:

- The **core (business logic and primitives)** comes first.
- Technical details (Infrastructure, Api) are outer layers.
- Dependencies must always point inward.

The structure communicates architecture clearly and reinforces proper layering.

---

### Core SharedKernel Usage

`CleanTemplate.SharedKernel` hosts **pure, reusable primitives** shared by inner layers.

Typical contents:

- `Result` / `Result<T>`
- `Error`
- Guard clauses
- Base `Entity`
- Base `ValueObject`
- Strongly Typed IDs

Rules:

- No EF Core
- No HTTP concerns
- No logging frameworks
- No Mediora
- No infrastructure dependencies

It must remain lightweight and framework-agnostic.

If you only need a small helper, keep it minimal and avoid adding unnecessary complexity to this module.

---

### Domain Layer

The `Domain` project contains:

- Business rules
- Invariants
- Entities
- Value Objects
- Domain-specific exceptions

It must not depend on Application, Infrastructure, or Api.

---

### Application Layer

The `Application` project contains:

- Use cases (Mediora handlers)
- DTOs for application flow
- Interfaces (repositories, services)
- Validation logic
- Orchestration of domain behavior

It defines contracts that Infrastructure implements.

Validation approach in this template is intentional: FluentValidation handles request-level input filtering, while Domain entities still enforce invariants. For products, name length is centralized in `Product.NameMaxLength` and reused by validators and EF configuration to keep rules consistent across layers.

Requests with FluentValidation validators must return exactly `Result` or `Result<T>`; other response types are allowed only without validators. `AddApplication` checks registered validators at configuration time. The Host checks its final registrations immediately before building; consumers configuring their own service provider must also call `services.ValidateRequestValidationConfiguration()` **after the final registration and before building the service provider**. The check inspects closed `IValidator<TRequest>` registrations, including factory registrations; registrations hidden from the service collection or added afterwards cannot be checked. Native AOT compatibility has not been established. As a fallback, constructing the validation behavior with an incompatible response and validators throws a configuration exception regardless of request validity; it is not an HTTP validation response and can surface as a server error if configuration verification was omitted. Validators run sequentially, with separate validation contexts, and all failures are collected; exceptions and cancellation still propagate. This does not serialize separate requests sharing a scope.

Product reads expose `version`; updates and deletes require `expectedVersion` and reject stale writes with HTTP 409. See the [product concurrency contract and upgrade requirements](docs/product-concurrency.md) before updating clients or deploying writers.

Product names and descriptions use shared UTF-16 limits; prices reject values outside decimal(18,2) or requiring rounding. See [product integrity and migration preflight](docs/product-integrity.md) for database guarantees and legacy data remediation.

Time-dependent behavior (for example token issuance/expiration) uses `TimeProvider` via DI instead of calling `DateTime.UtcNow` directly, improving testability.

The host exposes `GET /health/live` (process liveness, no dependency checks), `GET /health/ready` (readiness) and `GET /health` (a compatibility alias for readiness). Readiness executes `SELECT 1` against the primary database and, when `ProductReads:Mode=Replica`, the configured read replica; when `Caching:Provider=Redis`, it reads a constant probe key through `IDistributedCache` without writing. A missing key is healthy. This tests connectivity, not schema correctness, replication lag, write permissions or full application functionality. HTTP 200 contains only `Healthy`; failures and degraded results return 503 and `Unhealthy`. Responses disable caching. These technical routes are anonymous, version-neutral, excluded from OpenAPI and exempt from application rate limiting.

**Exposure and HTTPS:** anonymous status still reveals availability. Restrict all three paths to trusted monitoring sources using ingress/network policy; they are not a public diagnostics API. The host's normal HTTPS redirection applies: configure HTTPS probes, or terminate TLS at a trusted ingress and configure forwarded headers correctly. An intentionally isolated plaintext probe network requires an explicit deployment-level TLS/redirect decision; do not assume these routes bypass HTTPS redirection. Protect operational logs separately because provider errors can be logged even though responses contain no details.

`HealthChecks:TimeoutSeconds` is an integer from 1 to 60, default 5, applied per readiness check and as the database command timeout. Cancellation is cooperative, not a hard response deadline: connection opening, database commands and Redis receive the cancellation token, but providers or an already-dispatched Redis operation may finish later. The check awaits the underlying operation before disposing its resources; it does not abandon it with `WaitAsync`. Configure appropriate connection and command timeouts for SQL Server/PostgreSQL and connection/async timeouts in the Redis connection string as additional bounds; verify them in the deployed network. Probe and ingress deadlines must allow for those provider limits.

---

### Infrastructure Layer

The `Infrastructure` project contains:

- EF Core configuration and DbContext
- Repository implementations
- External service integrations
- Technical adapters

It depends on Application but should not introduce business logic.

---

### Crosscutting Hosting Project

The separate `CleanTemplate.Crosscutting` project contains reusable **technical** concerns for outer layers, such as:

- OpenTelemetry registration
- Rate limiting policies and options
- Forwarded headers support and trust options
- CORS policy configuration for HTTP middleware

CORS is configured from `Cors:AllowedOrigins` in `appsettings*.json` and enforced through the `ApiCors` policy.

Rules:

- No business rules
- No domain invariants
- Do not expose these technical concerns as dependencies for domain rules

---

### Decision: Specific repositories and read queries

[ADR-0001](docs/adr/0001-data-access-and-dependencies.md) defines the single data-access model used by the code and feature-generation instructions:

- Application groups contracts by responsibility in `Contracts/Persistence`, `Contracts/Identity` and `Contracts/Caching`. Persistence exposes `IProductWriteRepository` for aggregate changes and `IProductQueries` for projected queries.
- Infrastructure implements writes with EF Core and reads with Dapper. Dapper remains in the current base profile, not an optional generator switch.
- Application does not reference EF Core, Dapper, `DbContext` or `IQueryable`. The unused generic context abstraction has been removed.
- Handlers await contract methods with cancellation; tests use repository doubles. Persistence adapters require relational tests.

**Source compatibility:** external consumers must update imports to `CleanTemplate.Application.Contracts.Persistence`, `CleanTemplate.Application.Contracts.Identity` or `CleanTemplate.Application.Contracts.Caching` and replace `IProductReadRepository` with `IProductQueries`. No compatibility aliases are provided. This refactor does not change the HTTP or database contracts.

For example, an update handler loads its aggregate through the write contract:

```csharp
var product = await _productWriteRepository.GetByIdAsync(request.Id, cancellationToken)
    .ConfigureAwait(false);
```

New features add specific contracts and Infrastructure adapters, not `*DbContextExtensions.cs` in Application. Commit via the scoped `IUnitOfWork`; use `ExecuteInTransactionAsync` with a `Result` callback for multiple flushes. A failure result rolls back and invalidates the scope. Call `SaveChangesAsync` explicitly: transaction success does not flush pending changes, which remain tracked and could be saved later. Discard the scope after failure. This does not coordinate identity/session workflows or other contexts.

Product reads use `ConnectionStrings:DefaultConnection` by default. After a successful committed POST, a subsequent GET against the same primary sees the write, absent intervening changes; this assumes that connection actually targets a primary and is not routed elsewhere by the database infrastructure. To opt into eventual consistency, set `ProductReads:Mode=Replica` and `ConnectionStrings:ProductReadReplica` for the same provider. Replica lag is unbounded here: a GET after POST can return stale data or 404, and no affinity or read-after-write guarantee is provided. SQLite in-memory does not support replica mode.

Migrate legacy `ReadConnection` and `DapperConnection` explicitly: remove them for primary reads, or remove them and configure the replica settings above. A nonempty legacy value differing textually from `DefaultConnection` fails startup, even if semantically equivalent, to avoid silently changing destination; identical values are tolerated. A replica connection without replica mode, missing replica connection, or unknown mode also fails startup. Product pages normalize page below 1 to 1 and page size below 1 to 10 or above 100 to 100; offset uses 64-bit arithmetic. Sorts include an ID tie-breaker, but concurrent writes can change page contents and the count and rows are not a single snapshot. Very deep pages can be expensive. Counts are limited to the existing 32-bit contract.

---

### Api Layer

The `Host` project is the executable entry point, while `Api` contains the HTTP-facing web layer:

- Minimal API endpoints
- Dependency injection configuration
- Middleware
- Authentication & authorization
- HTTP exception handling/mapping
- OpenAPI configuration

It translates HTTP concerns into application requests and maps application responses back to HTTP results.

---

## Authentication and Authorization (Hybrid)

This template includes a hybrid authentication model with two JWT issuers:

- `LocalJwt`: issued by this API using ASP.NET Core Identity credentials.
- `Oidc`: issued by an external OIDC provider (Entra, Cognito, Auth0, Okta, Keycloak, etc.).

A policy scheme named `Bearer` selects the concrete scheme automatically based on the incoming token issuer (`iss`), so clients always send `Authorization: Bearer <token>` and the API resolves the validator.

Only enabled schemes are registered. A token for a disabled scheme or a malformed token receives a Bearer `401` challenge; disabling a scheme never relaxes token validation. If `LocalJwt` is disabled, `/auth/login`, `/auth/refresh`, and `/auth/logout` are not mapped, and the local token generator rejects direct issuance. Disabling both schemes is an invalid configuration and fails startup validation. Changes to scheme enablement require an application restart.

OIDC requires an absolute HTTPS authority. Its issuer, audience, signature and expiration are still validated; reading the untrusted `iss` claim only chooses the validator. Account ownership, role revocation, transactional provisioning and refresh session families are implemented with documented bounds. Rotation/families (T-06/T-07) are validated on SQLite, SQL Server and PostgreSQL; other production-readiness tasks remain pending. The entire identity subsystem is not yet production-ready.

### Decisions taken

1. **Identity is the internal source of truth**
   - ASP.NET Core Identity is persisted in the same EF Core database.
   - Internal roles are standardized as `Admin` and `User`.

2. **Provisioning strategy: Option B (JIT + internal mapping)**
    - OIDC users are provisioned just-in-time using the exact validated `issuer + sub` pair. Bounded SHA-256-derived login keys are stored in Identity; the configured provider name is only a display label.
    - A verified email is required; verification is never inferred from email presence. Matching an existing account's email rejects provisioning rather than linking the accounts.
    - `Auth:Schemes:Oidc:AllowedTenantIds` optionally restricts tenants before provisioning. A linked account cannot silently change tenant.
    - Provisioning decisions are not cached. Legacy `provider + sub` logins are not automatically migrated.
   - Authorization is always based on internal roles, never on external roles directly.

3. **Claims transformation as integration point**
    - `IClaimsTransformation` strips supplied internal markers/roles and creates an application-owned runtime identity after validated local authentication or successful external provisioning.
    - Internal claims emitted include `internal_user_id`, `internal_role`, and `auth_provider`. Policy authorization checks the application's identity type, not a token-supplied marker.
    - OIDC without provisioning may authenticate, but has no internal permissions, regardless of external role claims.

4. **Role mapping precedence**
    - Admin by verified exact email.
    - Admin by verified email domain.
   - Admin by external group.
   - Admin by external role.
   - Fallback to `DefaultRole` (recommended: `User`).

5. **Unified policy-based authorization**
     - Endpoints are protected with internal policies (`AdminOnly`, `UserOrAdmin`) to keep behavior issuer-agnostic.

6. **Role ownership and revocation**
   - Manual assignments remain in `AspNetUserRoles`. External roles are stored separately in `ExternalRoleGrants` per linked identity; synchronization removes only that source's obsolete grants.
   - Current database permissions, not JWT role claims, authorize each request. Login, refresh and OIDC use the same effective union of manual roles and valid linked grants; no permission cache is used.
   - External grants expire after `Auth:Provisioning:RoleGrantLifetimeMinutes` (default 5, allowed 1–60), capped by the external token's original expiration. An old but still-valid provider token may reassert old roles; this is not immediate IdP revocation.
   - Deploy the new table and audit legacy mixed-origin role assignments before upgrading an existing database. See [role ownership and revocation](docs/security/role-ownership-and-revocation.md).

7. **Transactional provisioning**
   - User creation, login association, profile changes and grants share one serializable transaction. Contention retries the complete unit in a fresh context, at most three times; failed attempts leave no partial identity writes.
   - Apply the additive `EnforceUniqueIdentityEmail` migration for the chosen provider after reviewing duplicate normalized emails. Accounts are never merged automatically.
   - SQLite development uses a named shared-memory database with independent request connections. See [transactional provisioning](docs/security/transactional-external-provisioning.md) for retry semantics, deployment prerequisites and validation limits.

See [the external identity trust model](docs/security/external-identity-trust.md) for configuration, account-linking design, migration implications and limitations. In particular, providers that do not emit an explicit trustworthy email-verification claim (including some Entra setups) cannot use the current JIT policy without a reviewed provider-specific integration. There is no public account-linking endpoint yet; do not restore email-based auto-linking as a workaround.

### Configuration overview

Authentication is configured under `Auth` in `CleanTemplate.Host/appsettings.json`:

- `Auth:Schemes:LocalJwt:*`
- `Auth:Schemes:Oidc:*`
- `Auth:Provisioning:*`

For production, keep signing keys and sensitive values in environment variables or secret managers (not committed in plain text).

For local development, prefer user-secrets instead of editing `appsettings.json` with real credentials:

Note: the template intentionally keeps Local JWT signing key values empty in `appsettings*.json`. Startup validation fails until you provide real values through user-secrets or environment variables.

Use `scripts/Initialize-DevelopmentSecrets.ps1` for local generated secrets, or inject configuration with your secret-management tooling. Do not put secret values in command arguments or shell history.

### Local bootstrap account

Only for the in-memory SQLite database in Development/Testing, startup initializes roles (`Admin`, `User`) and a local admin account:

- Email: `admin@local.template`
- Password: configured through `IdentitySeed:AdminPassword` (required)

The template ships with a placeholder password. The ephemeral initializer requires a real password. Persistent databases are never migrated or seeded at API startup; see [deployment operations](docs/database/deployment-operations.md).

### Security hardening implemented

- **Signing key rotation for Local JWT**
  - `Auth:Schemes:LocalJwt:SigningKeys` supports multiple active validation keys.
  - `Auth:Schemes:LocalJwt:ActiveSigningKeyId` controls which key signs newly issued access tokens.
  - Validation accepts all configured signing keys, enabling safe gradual rotation.

- **Refresh token flow (rotation on use)**
  - `POST /auth/login` returns both `accessToken` and `refreshToken`.
  - `POST /auth/refresh` consumes a refresh token and issues a new access/refresh pair.
  - `POST /auth/logout` revokes the presented token's entire session family (including when presenting a consumed ancestor) and always responds `204 No Content` to avoid token-enumeration signals.
  - Refresh tokens are persisted hashed. Rotation conditionally consumes the original and inserts its replacement in one transaction; concurrent losers receive no new token pair and strict reuse detection revokes the whole family, including a concurrent winner's replacement. Clients must serialize refreshes. Rotation/family tests pass on SQL Server and PostgreSQL with real migrations. Run `pwsh -NoProfile -File scripts/tests/Test-RefreshTokenRotation.ps1` with Docker to reproduce. See [atomic rotation](docs/security/refresh-token-rotation.md) and [session policy/rollout](docs/security/refresh-session-families.md).
  - `AbsoluteSessionDays` (default 30, range 1–90) caps session duration independently of per-token `RefreshTokenDays`. Security-stamp changes deny further rotation; missing/locked users trigger revocation of existing families. Administrative withdrawal workflows must call `RevokeAllForUserAsync`. Already-issued access tokens are not immediately session-revoked.
  - **Upgrade:** apply the new provider migration and drain old binaries. Legacy refresh tokens without families require a new login; do not deploy mixed versions or guess family backfills.

- **Automatic refresh token cleanup**
  - A background service removes expired/revoked families and cascades their tokens. Consumed/expired ancestors of active families are retained for replay detection; legacy tokens without families are cleaned separately.
  - Revoked families are retained for a configurable window, bounded by their absolute expiration.
  - Configuration: `Auth:RefreshTokenCleanup` (`Enabled`, `IntervalMinutes`, `RevokedRetentionDays`).

- **Stronger password + lockout policy**
  - Password policy: minimum length 8, uppercase/lowercase, digit, special character, and unique chars.
  - Lockout policy: 5 failed attempts -> 15 minutes lockout.

- **Recommended production posture**
  - Keep Local JWT keys in secret stores.
  - Rotate keys periodically by adding a new key, switching `ActiveSigningKeyId`, and later removing old keys.
  - Use short access tokens and bounded refresh token lifetime.

### Rate limiting

The API includes built-in ASP.NET Core rate limiting with two layers:

- **Global soft limit** applied to requests that reach the limiter; probes are explicitly exempt.
- **Strict endpoint policies** for authentication endpoints:
  - `POST /auth/login`
  - `POST /auth/refresh`
  - `POST /auth/logout`

Configuration is under `RateLimiting` in `CleanTemplate.Host/appsettings*.json`.

When deployed behind a reverse proxy/load balancer, configure its actual source addresses under `ForwardedHeaders` (`KnownProxies` / `KnownNetworks`). ASP.NET Core's default loopback trust remains in place even when these lists are empty: local processes can supply forwarded headers. Other peers are untrusted unless configured. Never use broad CIDRs such as `0.0.0.0/0` or `::/0`, or enable a hosting configuration that trusts all proxies, unless the network boundary independently prevents untrusted access. Restrict direct access to the application and make the trusted ingress remove client-supplied forwarding headers and write its own.

The framework default `ForwardLimit` is **one**, processed from right to left: with multiple hops the app sees the nearest forwarded address, not necessarily the original client. This template does not expose a hop-count setting. Configure the last trusted ingress to present a sanitized single client address and scheme, or explicitly design and test a different proxy chain before changing code. Otherwise clients may share a proxy's quota. Both IPv4 and IPv6 CIDRs are supported; review real source addresses, IPv4-mapped IPv6 and network changes. A missing remote address shares the `unknown` partition.

Default production values (`appsettings.json`):

- `Global`: `120 req / 60s` per client IP
- `Policies:auth.login`: `5 req / 60s` per client IP
- `Policies:auth.refresh`: `20 req / 60s` per client IP
- `Policies:auth.logout`: `30 req / 60s` per client IP

When a limit is exceeded, the API returns:

- `429 Too Many Requests`
- `Retry-After` header when supplied by the limiter

Implementation notes:

- Partitioning is exclusively by resolved remote IP (`HttpContext.Connection.RemoteIpAddress`), never by a supplied user/client claim. NAT users share a quota; clients with multiple addresses can spread traffic. The global and named fixed windows apply together; 429 may precede a 401/403. `Retry-After` is included when the limiter supplies it.
- The pipeline resolves trusted forwarded headers, CORS and HTTPS redirection before rate limiting; authentication, claims transformation/provisioning and authorization follow it. Thus rejected limited requests avoid those authentication costs, but routing, TLS, redirects, CORS and other earlier work still cost resources. Early routing/version errors, CORS preflight responses and HTTP redirects can complete before the limiter. Anonymous health probes bypass the app limiter, not HTTPS redirection. Monitoring requests should omit credentials: anonymous endpoint metadata does not disable authentication middleware when a bearer token is supplied. Restrict the probe paths at the ingress/network boundary and strip unnecessary credentials from monitoring traffic.
- Limits are in memory **per process**, reset on restart, and are not coordinated across replicas. More replicas or uneven load change effective capacity; fixed windows permit boundary bursts. They are defense in depth, not aggregate quotas or DDoS protection.
- The deployment operator owns an independently provisioned ingress/gateway/WAF: enforce aggregate network and request quotas across replicas, connection/body/time limits and abuse controls there; protect direct origin access and monitoring paths. Any user/client quota must use an identity verified by that gateway, not an unvalidated claim or forwarded header. No gateway or distributed limiter is supplied by this template; validate its actual consistency, failure mode and limits under load before deployment.
- Choose TLS termination explicitly: prefer HTTPS to the ingress and either TLS on the internal hop or a deliberately isolated trusted network. Correct trusted `X-Forwarded-Proto` is necessary for HTTPS redirection and HTTPS probes behind termination; untrusted headers cannot set the scheme. Plaintext probe exceptions require a deployment-level decision, not a presumed app exemption.

### OpenTelemetry (OTLP)

The host includes OpenTelemetry for distributed observability while keeping Serilog for application logging.

See the [observability operations guide](docs/observability/operations.md) for security metrics, audit limits, proposed retention and reproducible example alerts. No monitoring backend is deployed by the template.

- **Exporter**: OTLP (`OpenTelemetry:Otlp:*`)
- **Signals**: fine-grained toggles per signal
  - `OpenTelemetry:Traces:Enabled`
  - `OpenTelemetry:Metrics:Enabled`
- **Tracing sampler**: ParentBased + TraceIdRatio (`OpenTelemetry:Traces:SamplingRatio`)

Example configuration:

```json
"OpenTelemetry": {
  "ServiceName": "CleanTemplate.Host",
  "Otlp": {
    "Endpoint": "http://localhost:4317",
    "Protocol": "grpc"
  },
  "Traces": {
    "Enabled": true,
    "SamplingRatio": 0.2
  },
  "Metrics": {
    "Enabled": true
  }
}
```

Behavior notes:

- If both toggles are `false`, OpenTelemetry is not registered.
- The resource `service.version` comes from the Host assembly informational version, not configuration or the entry assembly of a test runner. Remove legacy `OpenTelemetry:ServiceVersion` settings.
- The default ASP.NET Core and HttpClient trace instrumentation disables exception recording. Before the configured OTLP exporter, HTTP server/client spans identified by an HTTP method tag retain only a whitelisted method, server route template, HTTP status and a controlled status-based error type; display names use the method and server template (or `unmatched`). Raw URLs, paths, query strings, headers, user agents and client addresses are not retained as HTTP span attributes. Other spans are not sanitized. W3C trace/span IDs remain available for correlation. Route templates are application-defined: never put personal data or secrets in route literals.
- On .NET 10 the native HttpClient activity can add an exception event independently of `RecordException`; a listener replaces its exception tags with nulls before the event is created. The event itself remains. This is a default instrumentation boundary, not a general telemetry scrubber: enrichment, additional instrumentation, listeners, processors, exporters or collector configuration can introduce or retain sensitive attributes, events and exception details. Existing activity events cannot be removed by this processor. Review every opt-in and the data leaving the process, as well as collector access controls, transformations and retention; arbitrary exporter or collector configuration is not sanitized.
- The default Serilog sink writes JSON to stdout, including during bootstrap. File sinks are opt-in via Serilog configuration; choose access controls and retention appropriate to the deployment. Request completion logs contain a controlled method, route template, status and duration. TraceId and SpanId come from the current activity; ProblemDetails falls back to the HTTP trace identifier when no activity exists.
- The `CleanTemplate.Http` meter records request count and duration, 5xx errors and 401/403 rejections with method, route template and status dimensions. These HTTP counters are not a security audit or an alerting system.
- These protections cover the template's own events and selected framework sources, not arbitrary libraries or custom sinks. Review additional logging providers, libraries, OpenTelemetry instrumentation and exported data before production; URLs or other sensitive data may be present in third-party telemetry.
- Default framework exception and EF Core categories are suppressed because exception messages can include request or database values even without sensitive-data logging. Configuration overrides, additional sinks and libraries can defeat these defaults; arbitrary configuration is not sanitized.
- Recommended defaults: `SamplingRatio=1.0` in development, lower in production (for example `0.1` to `0.2`).
- Request/response bodies are not captured in telemetry by default.

---

### Result vs Exceptions

Expected business outcomes should use the `Result` pattern.

`Error` includes an `ErrorType` category (for example `Validation`, `NotFound`, `Failure`) so API layers can map HTTP status codes explicitly without relying on string conventions in `Error.Code`.

Exceptions should be reserved for:

- Unexpected failures
- Invariant violations inside Domain
- Infrastructure errors

This prevents exceptions from being used as control flow and keeps business logic explicit.
# Persistencia: migraciones versionadas

SQL Server y PostgreSQL disponen de migraciones independientes. Consulta
[la guía de migraciones](docs/database/versioned-migrations.md) antes de actualizar
una base, especialmente si fue creada con `EnsureCreated`. SQLite queda reservado
a bases en memoria de Development/Testing en el perfil predeterminado; la excepción explícita de Production se describe abajo. La separación del arranque, migraciones
y bootstrap administrativo se ejecutan como operaciones explícitas; consulte [operaciones de despliegue](docs/database/deployment-operations.md).

## Startup configuration and ephemeral databases

LocalJwt, when enabled, requires issuer, audience, an active signing key ID and a signing key of at least 32 characters. Rotated key IDs must be unique and include the active ID. Access tokens are limited to 1–1440 minutes; refresh and absolute sessions to 1–90 days. Access cannot exceed the absolute session. Disabled LocalJwt does not require its signing secret. OIDC authority must be HTTPS without user info, query or fragment; restricted tenants require a tenant claim. Provisioning supports internal default roles `Admin` and `User`; external role names remain unrestricted, but rule entries cannot be empty.

Production rejects `DatabaseProvider=SqliteInMemory` before opening the database unless `Database:AllowEphemeralInProduction=true` is explicitly set (strict boolean). This opt-in deliberately loses all data on restart and is not a substitute for persistent migrations or bootstrap. It creates the ephemeral schema without seeding an administrator or requiring bootstrap secrets; Staging is not automatically initialized. Readiness checks connectivity, not schema completeness. `DatabaseProvider` defaults to PostgreSQL only when absent; an empty or unknown value is invalid. Startup diagnostics identify a configuration key or section without printing values or exception messages. Fixed-window rate limits accept `WindowSeconds` from 1 through 4294967 for both Global and named policies. Provisioning email and domain rules must be well formed; external role names remain unrestricted. Review existing deployments for these stricter limits before upgrading.

Rate limits require positive permit counts, nonnegative queues and supported fixed windows; invalid values are rejected instead of clamped. Cache configuration requires a defined provider, positive default TTL and a nonempty key prefix; Redis requires its connection only when selected. Enabled cleanup requires a 1–1440 minute interval and 0–365 retention days. Forwarded proxies must be IP addresses and networks valid IPv4/IPv6 CIDR prefixes without host bits. CORS entries must be HTTP(S) origins without credentials, paths, queries, fragments or wildcards. Enabled telemetry requires a nonempty service name, an absolute HTTP(S) OTLP endpoint without credentials, and protocol `grpc`, `http` or `httpprotobuf`; active trace sampling must be finite in [0,1]. Service version comes from the Host artifact. Inactive capabilities do not require their secrets; configured scalar booleans and integers must still parse. Diagnostics may identify a whole section instead of a single field to avoid exposing exception contents.
