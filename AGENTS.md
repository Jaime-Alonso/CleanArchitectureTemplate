# AGENTS.md

Guidelines for AI coding agents working in this repository.

---

## Build/Lint/Test Commands

### Build

```bash
# Build entire solution
dotnet build CleanArchitectureTemplate.slnx

# Build specific project
dotnet build src/CleanTemplate.Domain/CleanTemplate.Domain.csproj

# Build in release mode
dotnet build CleanArchitectureTemplate.slnx -c Release

# Clean build artifacts
dotnet clean CleanArchitectureTemplate.slnx
```

### Run

```bash
# Run the host application
dotnet run --project CleanTemplate.Host/CleanTemplate.Host.csproj

# Run with specific environment
dotnet run --project CleanTemplate.Host/CleanTemplate.Host.csproj --environment Development
```

### Test

```bash
# Run all tests (when test projects exist)
dotnet test CleanArchitectureTemplate.slnx

# Run specific test project
dotnet test test/CleanTemplate.Tests/CleanTemplate.Tests.csproj

# Run single test class
dotnet test CleanArchitectureTemplate.slnx --filter "FullyQualifiedName~MyTestClass"

# Run single test method
dotnet test CleanArchitectureTemplate.slnx --filter "FullyQualifiedName~MyTestClass.MyTestMethod"

# Run tests with coverage
dotnet test CleanArchitectureTemplate.slnx --collect:"XPlat Code Coverage"
```

### Format/Lint

```bash
# Format all code (whitespace, style, analyzers)
dotnet format CleanArchitectureTemplate.slnx

# Format specific project
dotnet format src/CleanTemplate.Domain/CleanTemplate.Domain.csproj

# Verify formatting without making changes
dotnet format CleanArchitectureTemplate.slnx --verify-no-changes

# Run only style analysis
dotnet format CleanArchitectureTemplate.slnx --severity info
```

---

## Architecture Overview

This is a **Clean Architecture** solution using **Minimal APIs** with the following layer ordering (core first, details last):

```
1. SharedKernel → No dependencies (pure primitives)
2. Domain       → Depends on SharedKernel only
3. Application  → Depends on Domain + SharedKernel
4. Infrastructure → Depends on Application
5. Api          → Depends on Application + SharedKernel (not Infrastructure)
6. Host         → Composition root: Api + Infrastructure + Crosscutting + provider migrations
```

### Dependency Rule

**Dependencies must always point inward toward the core.** Never reference outer layers from inner layers.

Crosscutting contains outer-layer hosting utilities, not pure core primitives. Provider migration projects depend on Infrastructure. The direct dependency matrix is documented in `docs/adr/0001-data-access-and-dependencies.md` and checked by `test/CleanTemplate.ArchitectureTests`.

### Data Access Decision (ADR-0001)

- Application uses specific write repositories and read-query contracts, never DbContext, IQueryable, EF Core or Dapper.
- Infrastructure implements EF Core writes and Dapper reads; Dapper remains in the current base profile.
- Do not recreate the removed generic context contract or Application DbContext extension methods.
- Application owns commits through the scoped `IUnitOfWork`, shared with write repositories. Multiple flushes require `ExecuteInTransactionAsync` and a `Result` callback: failure rolls back and invalidates the scope. Flush explicitly with `SaveChangesAsync`; transaction success does not flush pending changes, which remain tracked and could be saved later. Discard the scope after failure. Identity/session transactions remain separate. Read consistency (T-14) remains pending.

---

## Code Style Guidelines

### Project Configuration

All projects use:
- **Target Framework**: net10.0
- **Nullable**: `enable` (always use nullable reference types)
- **ImplicitUsings**: `enable`

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `UserAccount`, `OrderProcessor` |
| Interfaces | PascalCase with `I` prefix | `IUserRepository`, `IOrderService` |
| Methods | PascalCase | `GetUserById`, `CalculateTotal` |
| Properties | PascalCase | `FirstName`, `CreatedAt` |
| Constants | PascalCase | `MaxRetryCount`, `DefaultTimeout` |
| Private fields | `_camelCase` | `_userRepository`, `_logger` |
| Parameters | camelCase | `userId`, `cancellationToken` |
| Local variables | camelCase | `result`, `orderItems` |

### File Organization

- One public type per file
- File name must match the type name
- Organize files in folders matching namespace

### Imports

```csharp
// Order: System namespaces first, then third-party, then project namespaces
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mediora;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Application.Interfaces;
```

### Code Structure

```csharp
namespace CleanTemplate.Domain.Entities;

public class Order : Entity
{
    private readonly List<OrderItem> _items = new();

    public Guid Id { get; private set; }
    public string CustomerName { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    private Order() { }

    public Order(string customerName)
    {
        CustomerName = customerName ?? throw new ArgumentNullException(nameof(customerName));
    }

    public void AddItem(OrderItem item)
    {
        // Business logic here
    }
}
```

### Error Handling

- Use **Result pattern** for expected business outcomes
- Use **exceptions** for unexpected failures and invariant violations
- Never use exceptions as control flow

```csharp
// Expected business failure - use Result
public Result<Order> CreateOrder(string customerName)
{
    if (string.IsNullOrWhiteSpace(customerName))
        return Result.Failure<Order>(Error.Validation("CustomerName is required"));
    
    return Result.Success(new Order(customerName));
}

// Unexpected failure - throw exception
public void ProcessPayment(Payment payment)
{
    if (payment == null)
        throw new ArgumentNullException(nameof(payment));
}
```

### Nullable Reference Types

Always handle nullability explicitly:

```csharp
// Non-nullable - must be assigned
public string Name { get; set; }

// Explicitly nullable
public string? MiddleName { get; set; }

// Use null-conditional and null-coalescing operators
var length = name?.Length ?? 0;
```

### Async/Await

- All I/O operations must be async
- Use `CancellationToken` for all async methods
- Use `.ConfigureAwait(false)` in library code

```csharp
public async Task<Result<User>> GetUserAsync(
    Guid userId, 
    CancellationToken cancellationToken = default)
{
    var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
    return user is null 
        ? Result.Failure<User>(Error.NotFound("User not found"))
        : Result.Success(user);
}
```

---

## Layer-Specific Guidelines

### SharedKernel

- Pure C# primitives only
- No external dependencies (no EF Core, no Mediora, no HTTP)
- Contains: `Result`, `Error`, `Guard`, base `Entity`, `ValueObject`

### Domain

- Business rules and invariants
- Rich domain models with behavior
- No infrastructure concerns
- Entities have private setters, enforce invariants

### Application

- Mediora handlers (CQRS pattern)
- DTOs for application flow
- Interface definitions for Infrastructure to implement
- Validation logic

### Infrastructure

- EF Core DbContext and configurations
- Repository implementations
- External service integrations
- No business logic

### Api / Host

- Minimal API endpoints
- Dependency injection configuration
- Middleware and authentication
- Maps HTTP to Application layer

---

## Commit Guidelines

- Write clear, descriptive commit messages
- Focus on the "why" not the "what"
- Keep commits atomic and focused

---

## Notes

- This template is intentionally minimal - add dependencies as needed
- Follow SOLID principles
- Prefer composition over inheritance
- Write tests for all new functionality
