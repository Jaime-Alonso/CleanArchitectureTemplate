---
name: new-feature-skeleton
description: Generate a new feature command use case across Application, Api, and tests using this repository's opinionated conventions.
compatibility: opencode
metadata:
  audience: maintainers
  stack: dotnet-clean-architecture
---

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
- Follow ADR-0001: Application uses specific repository/query contracts; EF Core and Dapper stay in Infrastructure.

## Generation Rules

### 1) Application
Create:
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>Command.cs`
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>CommandHandler.cs`
- `src/CleanTemplate.Application/<FeatureName>/Commands/<UseCaseName>/<UseCaseName>CommandValidator.cs`

Reuse or add a specific write repository contract under `Application/Contracts` and its EF implementation in Infrastructure. For reads, use a specific query contract and read models with a Dapper implementation. Register new adapters in Infrastructure DI. Never generate Application DbContext extensions, IQueryable contracts or EF/Dapper package references.

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
ADR-0001 (`docs/adr/0001-data-access-and-dependencies.md`) keeps EF Core writes and Dapper reads in Infrastructure behind specific Application contracts. Handlers await contract methods with cancellation and ConfigureAwait(false); handler tests use repository doubles, not simulated EF async LINQ. Follow the current write repository commit convention until T-13 introduces an explicit unit of work; do not invent a parallel commit model.

## Required Validation
Run:
- `dotnet build CleanArchitectureTemplate.slnx`
- `dotnet test CleanArchitectureTemplate.slnx`

Confirm:
- no layer dependency violations
- endpoint mapped in `Program.cs`
- generated use case compiles and tests pass
