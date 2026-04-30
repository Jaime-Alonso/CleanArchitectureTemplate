# Clean Architecture Simple Template (.NET) — Minimal API + MediatR

This repository provides a **solution template** for starting .NET projects using a **simple Clean Architecture**, built with **Minimal API** and **MediatR**, without additional modular complexity.

The template is designed to be:

- Easy to understand and maintain
- Scalable without over-engineering
- Aligned with SOLID principles and clean code practices

---

## Before You Start

> [!IMPORTANT]
> Before running the host for the first time, configure at least the admin seed credentials in user-secrets.
> Without these values, the app cannot provision the initial admin user correctly in development.

```json
{
  "IdentitySeed": {
    "AdminEmail": "admin@local.template",
    "AdminPassword": "Test005!"
  }
}
```

You can set these values with `dotnet user-secrets` for the Host project, or by using your preferred secret manager in your environment.

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

- `MyApp.Core` _(contains `Core/SharedKernel` primitives and `Core/CrossCutting` technical concerns)_
- `MyApp.Domain`
- `MyApp.Application`
- `MyApp.Infrastructure`
- `MyApp.Api`
- `MyApp.Host`

And one or more test projects, for example:

- `MyApp.Domain.Tests`
- `MyApp.Application.Tests`
- `MyApp.Api.IntegrationTests`

---

## Visual Studio Solution Folders and Why This Order

In Visual Studio, **Solution Folders** (organizational only, not physical folders) are created in this order:

1. **Core**
2. **Domain**
3. **Application**
4. **Infrastructure**
5. **Api**
6. **Host**
7. **Tests**

### Why this enumeration?

The order reflects the **dependency rule of Clean Architecture**:

> Dependencies must always point inward, toward the core.

- **Core** is the innermost shared base and includes two internal areas: `Core/SharedKernel` (pure primitives like `Result`, `Error`, `Guard`, base `Entity`, `ValueObject`) and `Core/CrossCutting` (technical runtime concerns like observability, networking, and rate limiting).
- **Domain** contains business rules and invariants and should only consume `Core.SharedKernel` primitives, never outer layers.

- **Application** implements use cases (MediatR handlers), orchestrates domain logic, and defines contracts (interfaces) to be implemented by infrastructure.  
  It depends on `Domain`.

- **Infrastructure** implements technical details (EF Core, repositories, external integrations).  
  It depends on `Application` (to implement its interfaces) and possibly `Domain`.

- **Api** contains the HTTP contract and endpoint surface (Minimal API endpoint mapping and request/response contracts).  
  It depends on `Application`.

- **Host** is the executable entry point that composes `Api`, `Infrastructure`, and `Core` runtime concerns.
- **Tests** are placed last to clearly separate production code from verification concerns.

Visually, this ordering reinforces the architectural message:  
**Core first, details later.**

---

## Dependency Overview

- `Core` → shared base project with `SharedKernel` + `CrossCutting` modules
- `Domain` → depends on `Core` (consuming `Core.SharedKernel` primitives)
- `Application` → depends on `Domain`
- `Infrastructure` → depends on `Application` (and possibly `Domain`)
- `Api` → depends on `Application`
- `Host` → depends on `Api` + `Infrastructure` + `Core`
- `Tests` → depend on the projects they test

## Requirements

- .NET SDK installed
- (Optional) Visual Studio 2026 or later

---

### Architectural Ordering (Core First)

The solution folders are intentionally ordered to reflect **Clean Architecture dependency rules**:

1. Core
2. Domain
3. Application
4. Infrastructure
5. Api
6. Host
7. Tests

This order emphasizes that:

- The **core (business logic and primitives)** comes first.
- Technical details (Infrastructure, Api) are outer layers.
- Dependencies must always point inward.

The structure communicates architecture clearly and reinforces proper layering.

---

### Core SharedKernel Usage

`Core/SharedKernel` hosts **pure, reusable primitives** shared by inner layers.

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
- No MediatR
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

- Use cases (MediatR handlers)
- DTOs for application flow
- Interfaces (repositories, services)
- Validation logic
- Orchestration of domain behavior

It defines contracts that Infrastructure implements.

Validation approach in this template is intentional: FluentValidation handles request-level input filtering, while Domain entities still enforce invariants. For products, name length is centralized in `Product.NameMaxLength` and reused by validators and EF configuration to keep rules consistent across layers.

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

### Core CrossCutting Module

The `Core/CrossCutting` module contains reusable **technical** concerns for outer layers, such as:

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

### Decision: No repository layer over EF Core

DbContext is already a Unit of Work. DbSet<T> is already a Repository. Wrapping EF Core behind another repository layer usually adds abstraction over abstraction without real gain.

Arguments that do not hold well:

- "To be able to change ORM" - In practice this almost never happens. And if it does, a generic repository does not save you because EF Core behavior leaks everywhere: lazy loading, change tracker, Include(), AsNoTracking(), transactions. The repository pretends to hide EF Core while it actually assumes it all over the place.
- "To test without a database" - EF Core already provides UseInMemoryDatabase and SQLite in test mode. You do not need the pattern for this.
- "To comply with Clean Architecture" - Clean Architecture asks Infrastructure not to contaminate Application, not that EF Core must disappear behind five layers of interfaces.

### How IApplicationDbContext is designed

`IApplicationDbContext` is intentionally **generic** so it does not grow as the domain grows:

```csharp
public interface IApplicationDbContext
{
    IQueryable<TEntity> Set<TEntity>() where TEntity : class;
    void Add<TEntity>(TEntity entity) where TEntity : class;
    void Remove<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

Entity-specific access needed by handlers is implemented through **feature-local extension methods**, not by adding entity members to the interface.
This keeps the contract stable and avoids coupling Application to Infrastructure details.

This template makes an explicit **opinionated** trade-off here: Application extension methods are allowed to use EF Core async operators.
We accept this technical coupling to gain real async query execution, cancellation support, and lower friction in handlers.
The goal is practical maintainability over architectural purism.

```csharp
// Application/Products/ProductDbContextExtensions.cs
using Microsoft.EntityFrameworkCore;

internal static class ProductDbContextExtensions
{
    internal static Task<Product?> FindByIdAsync(
        this IApplicationDbContext context,
        Guid id,
        CancellationToken cancellationToken = default)
        => context.Set<Product>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
}
```

Handlers call the extension method naturally:

```csharp
var product = await _context.FindByIdAsync(request.Id, cancellationToken);
```

When a new aggregate is added, create a new `*DbContextExtensions.cs` file next to that feature.
The interface remains unchanged.

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

### Decisions taken

1. **Identity is the internal source of truth**
   - ASP.NET Core Identity is persisted in the same EF Core database.
   - Internal roles are standardized as `Admin` and `User`.

2. **Provisioning strategy: Option B (JIT + internal mapping)**
   - OIDC users are provisioned just-in-time and linked by `provider + sub`.
   - Authorization is always based on internal roles, never on external roles directly.

3. **Claims transformation as integration point**
   - `IClaimsTransformation` converts external identity into stable internal claims before authorization.
   - Internal claims emitted include `internal_user_id`, `role`, and `auth_provider`.

4. **Role mapping precedence**
   - Admin by exact email.
   - Admin by email domain.
   - Admin by external group.
   - Admin by external role.
   - Fallback to `DefaultRole` (recommended: `User`).

5. **Unified policy-based authorization**
   - Endpoints are protected with internal policies (`AdminOnly`, `UserOrAdmin`) to keep behavior issuer-agnostic.

### Configuration overview

Authentication is configured under `Auth` in `CleanTemplate.Host/appsettings.json`:

- `Auth:Schemes:LocalJwt:*`
- `Auth:Schemes:Oidc:*`
- `Auth:Provisioning:*`

For production, keep signing keys and sensitive values in environment variables or secret managers (not committed in plain text).

For local development, prefer user-secrets instead of editing `appsettings.json` with real credentials:

Note: the template intentionally keeps Local JWT signing key values empty in `appsettings*.json`. Startup validation fails until you provide real values through user-secrets or environment variables.

```bash
dotnet user-secrets --project src/CleanTemplate.Host/CleanTemplate.Host.csproj set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=cleantemplate;Username=postgres;Password=<your-password>"
dotnet user-secrets --project src/CleanTemplate.Host/CleanTemplate.Host.csproj set "ConnectionStrings:DapperConnection" "Host=localhost;Database=cleantemplate;Username=postgres;Password=<your-password>"
dotnet user-secrets --project src/CleanTemplate.Host/CleanTemplate.Host.csproj set "Auth:Schemes:LocalJwt:SigningKey" "<dev-signing-key>"
dotnet user-secrets --project src/CleanTemplate.Host/CleanTemplate.Host.csproj set "IdentitySeed:AdminPassword" "<dev-admin-password>"
```

### Local bootstrap account

At startup, the app seeds roles (`Admin`, `User`) and a local admin account:

- Email: `admin@local.template`
- Password: configured through `IdentitySeed:AdminPassword` (required)

The template intentionally ships with a placeholder password value in `appsettings*.json`. If you do not set `IdentitySeed:AdminPassword` via user-secrets or environment variables, startup validation fails.

### Security hardening implemented

- **Signing key rotation for Local JWT**
  - `Auth:Schemes:LocalJwt:SigningKeys` supports multiple active validation keys.
  - `Auth:Schemes:LocalJwt:ActiveSigningKeyId` controls which key signs newly issued access tokens.
  - Validation accepts all configured signing keys, enabling safe gradual rotation.

- **Refresh token flow (rotation on use)**
  - `POST /auth/login` returns both `accessToken` and `refreshToken`.
  - `POST /auth/refresh` consumes a refresh token and issues a new access/refresh pair.
  - `POST /auth/logout` explicitly revokes the current refresh token and always responds `204 No Content` to avoid token-enumeration signals.
  - Refresh tokens are persisted hashed, with one-time consumption semantics.

- **Automatic refresh token cleanup**
  - A background service periodically removes expired refresh tokens.
  - Revoked tokens are retained for a configurable window before cleanup.
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
