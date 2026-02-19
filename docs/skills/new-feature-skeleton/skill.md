# Skill: New Feature Skeleton (Opinionated)

Canonical OpenCode file: `.opencode/skills/new-feature-skeleton/SKILL.md`

## Purpose
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
- Use async handlers with `CancellationToken`.
- EF Core async operators in Application extension methods are allowed by explicit decision.

## Generation Rules

### 1) Application
Create:
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>Command.cs`
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>CommandHandler.cs`
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>CommandValidator.cs`

If missing, create feature extension file:
- `src/CleanTemplate.Application/<FeatureName>/<FeatureSingular>DbContextExtensions.cs`

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
- Group route: `/api/<feature-lowercase>`
- Endpoint mapping based on method + route input
- Authorization policy:
  - `AdminOnly` -> `.RequireAuthorization("AdminOnly")`
  - `UserOrAdmin` -> `.RequireAuthorization("UserOrAdmin")`
  - `None` -> no policy call

## Error Mapping Pattern
- `NotFound` -> `Results.NotFound(...)`
- `Validation` -> `Results.BadRequest(...)`
- fallback -> `Results.BadRequest(...)`

## Opinionated Decision (Explicit)
This template explicitly accepts EF Core async usage in Application feature extension methods (for example, `FirstOrDefaultAsync`).

Reason:
- real async query execution
- cancellation support
- lower handler complexity

This is intentional and not a purist Clean Architecture stance.

## Required Validation
Run:
- `dotnet build CleanArchitectureTemplate.slnx`
- `dotnet test CleanArchitectureTemplate.slnx`

Confirm:
- no layer dependency violations
- endpoint mapped in `Program.cs`
- generated use case compiles and tests pass
