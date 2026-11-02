# Clean Architecture Simple Template (.NET) — Minimal API + Mediora

This repository provides a **solution template** for starting .NET projects using a **simple Clean Architecture**, built with **Minimal API** and **Mediora**, without additional modular complexity.

The template is designed to be:

- Easy to understand and maintain
- Scalable without over-engineering
- Aligned with SOLID principles and clean code practices

---

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

Product reads expose `version`; updates and deletes require `expectedVersion` and reject stale writes with HTTP 409. See the [product concurrency contract and upgrade requirements](docs/product-concurrency.md) before updating clients or deploying writers.

Time-dependent behavior (for example token issuance/expiration) uses `TimeProvider` via DI instead of calling `DateTime.UtcNow` directly, improving testability.

The host exposes `GET /health` and includes an EF Core `DbContext` health check for basic runtime monitoring.

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

- Application defines specific contracts: `IProductWriteRepository` for aggregate changes and `IProductReadRepository` for projected queries.
- Infrastructure implements writes with EF Core and reads with Dapper. Dapper remains in the current base profile, not an optional generator switch.
- Application does not reference EF Core, Dapper, `DbContext` or `IQueryable`. The unused generic context abstraction has been removed.
- Handlers await contract methods with cancellation; tests use repository doubles. Persistence adapters require relational tests.

For example, an update handler loads its aggregate through the write contract:

```csharp
var product = await _productWriteRepository.GetByIdAsync(request.Id, cancellationToken)
    .ConfigureAwait(false);
```

New features add specific contracts and Infrastructure adapters, not `*DbContextExtensions.cs` in Application. The current write contract still exposes `SaveChangesAsync`; moving commit ownership to an explicit unit of work is pending T-13. Read-replica consistency remains pending T-14.

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

- **Global soft limit** applied to all requests.
- **Strict endpoint policies** for authentication endpoints:
  - `POST /auth/login`
  - `POST /auth/refresh`
  - `POST /auth/logout`

Configuration is under `RateLimiting` in `CleanTemplate.Host/appsettings*.json`.

When deployed behind a reverse proxy/load balancer, configure trusted proxy sources under `ForwardedHeaders` (`KnownProxies` / `KnownNetworks`). The template does not trust raw `X-Forwarded-For` unless the source proxy/network is explicitly configured.

Default production values (`appsettings.json`):

- `Global`: `120 req / 60s` per client IP
- `Policies:auth.login`: `5 req / 60s` per client IP
- `Policies:auth.refresh`: `20 req / 60s` per client IP
- `Policies:auth.logout`: `30 req / 60s` per client IP

When a limit is exceeded, the API returns:

- `429 Too Many Requests`
- `Retry-After` header

Implementation notes:

- Partitioning key uses the resolved remote client IP (`HttpContext.Connection.RemoteIpAddress`).
- Rate limiting is in-memory per application instance. In multi-instance deployments, limits are enforced per replica.

### OpenTelemetry (OTLP)

The host includes OpenTelemetry for distributed observability while keeping Serilog for application logging.

- **Exporter**: OTLP (`OpenTelemetry:Otlp:*`)
- **Signals**: fine-grained toggles per signal
  - `OpenTelemetry:Traces:Enabled`
  - `OpenTelemetry:Metrics:Enabled`
- **Tracing sampler**: ParentBased + TraceIdRatio (`OpenTelemetry:Traces:SamplingRatio`)

Example configuration:

```json
"OpenTelemetry": {
  "ServiceName": "CleanTemplate.Host",
  "ServiceVersion": "1.0.0",
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
a bases en memoria de Development/Testing. La separación del arranque, migraciones
y bootstrap administrativo se ejecutan como operaciones explícitas; consulte [operaciones de despliegue](docs/database/deployment-operations.md).
