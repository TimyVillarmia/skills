# Architecture

Use this reference when designing or changing application structure, boundaries, dependency direction, or major components.

## Start from requirements

Architecture is a means to satisfy requirements and constraints. Do not choose an architecture because it is fashionable or because a template exists.

Consider business/domain complexity, team ownership, deployment topology, operational requirements, change patterns, integration boundaries, testing needs, performance/scaling, security, and compliance.

Prefer the least complex architecture that preserves the boundaries the system actually needs.

## Architecture decision flow

```text
Is there a real boundary or invariant to protect?
├─ No -> keep the code local and simple.
└─ Yes
   ├─ Is the boundary a business capability/use case?
   │  └─ Consider a vertical slice/module.
   ├─ Does dependency direction/domain isolation matter?
   │  └─ Consider Clean Architecture principles.
   ├─ Do commands and queries materially differ?
   │  └─ Consider CQRS.
   └─ Does deployment/ownership/scaling require separation?
      └─ Consider a service boundary.
```

A pattern is justified by the problem it solves, not by its popularity.

## Pragmatic default

A modular monolith is often a strong starting point for business applications. Keep modules cohesive and boundaries explicit before introducing distributed deployment boundaries.

Do not introduce microservices merely to create architectural separation. A service boundary should have a concrete reason such as independent scaling, deployment, ownership, isolation, or integration requirements.

## Vertical Slice Architecture

Vertical slices organize code around business capabilities or use cases rather than technical layers.

A useful shape is:

```text
Feature
├── Command / Query
├── Handler
├── Validation
├── Mapping
└── Tests
```

Use this when feature cohesion and independent change are more valuable than shared horizontal layers.

CQRS fits naturally when commands and queries have materially different behavior, data access, performance, or models. It does not require separate databases or services.

```text
// Direct handler injection (primary default for Minimal APIs):
Command -> Endpoint -> Handler -> Domain / Persistence
Query   -> Endpoint -> Handler / QueryService -> Persistence -> Projection -> DTO

// With in-process mediator (when cross-cutting pipeline behaviors justify it):
Command -> Endpoint -> Mediator -> Pipeline -> Handler -> Domain / Persistence
Query   -> Endpoint -> Mediator -> Pipeline -> Persistence -> Projection -> DTO
```

An in-process mediator is justified when reusable cross-cutting pipeline behaviors (logging, validation, audit, unit of work) across handlers provide concrete value. When those pipeline behaviors are not needed, direct handler injection into the Minimal API endpoint is simpler, faster, and Native AOT friendly.

## Clean Architecture

Clean Architecture is useful when dependency direction and domain isolation are important. Its value comes from controlling dependencies and protecting business rules, not from creating a fixed number of projects.

Avoid layers that contain no meaningful behavior merely to satisfy an architectural diagram. Combining Clean Architecture principles with vertical slices can be a pragmatic choice.

## Dependency direction

Ask:

- Who owns this behavior?
- Who needs to change together?
- Which dependency is likely to vary?
- Is this boundary protecting a real invariant or merely adding indirection?

Dependencies should point toward stable business rules when isolation is required, while infrastructure remains replaceable where that boundary has real value.

Do not create abstractions solely because a dependency is technically replaceable.

## Repositories and persistence abstractions

EF Core already provides substantial abstraction over database access. Do not introduce a generic repository or unit-of-work wrapper by default.

Good abstraction:

```csharp
public interface IAssetNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken);
}
```

This can represent a meaningful external/business boundary.

Poor abstraction:

```csharp
public interface IRepository<T>
{
    Task<T?> GetAsync(Guid id);
    Task AddAsync(T entity);
    Task SaveAsync();
}
```

If it merely renames `DbSet`, LINQ, and `SaveChangesAsync`, it adds indirection without protecting a useful boundary.

## Mapping

Keep mappings explicit, compile-time verified, and aligned with domain encapsulation.

### Response / Read Mapping
For response DTOs, prefer a static factory method `From` and a reusable EF Core LINQ `Project` expression defined directly on the DTO:

```csharp
public sealed record UserResponse(Guid Id, string Email, string FullName)
{
    // Single instance factory (e.g. after domain operations)
    public static UserResponse From(User user) =>
        new(user.Id, user.Email, user.FullName);

    // Reusable EF Core Projection expression (zero tracking, SQL-translated)
    public static Expression<Func<User, UserResponse>> Project =>
        u => new UserResponse(u.Id, u.Email, u.FullName);
}
```

- `UserResponse.From(user)` provides clean, discoverable single-instance mapping without polluting entity IntelliSense.
- `UserResponse.Project` enables SQL-translated database projection without materializing full tracked entities: `db.Users.Select(UserResponse.Project).ToListAsync(ct)`.

### Request / Write Path
Command/request handlers extract properties from request DTOs and pass them to domain factory methods or constructors (e.g., `User.Create(request.Email, request.FullName, clock)`).

### Automated Mapping
When mapping large models or across multiple architectural layers, prefer compile-time Roslyn source generators such as **Riok.Mapperly**. Avoid runtime reflection-based mappers (such as AutoMapper) which incur startup/memory overhead and fail under Native AOT compilation. Existing projects may use Mapster or AutoMapper; do not replace an established mapper without a concrete reason.

## Dependency selection

Apply the authoritative [Dependency decisions](../SKILL.md#dependency-decisions) order from the main `.NET Engineering` skill: prefer platform capabilities and existing project dependencies, then consider a small explicit implementation for simple non-sensitive functionality, and finally evaluate a new third-party dependency when its value justifies the added cost.

For architecture-specific decisions, consider whether the dependency creates a meaningful boundary, introduces coupling or lock-in, changes deployment/ownership concerns, or materially reduces complexity.

Do not introduce a new dependency merely to satisfy an architectural preference. Follow the main skill's interactive/headless approval rule before adding one.

Do not implement security-sensitive primitives (cryptography, token parsing, password hashing) yourself merely to avoid a package; custom implementations frequently introduce subtle vulnerabilities.

## Architecture anti-patterns

The following are common architectural warning signs, not automatic defects. Evaluate them against the project's requirements, constraints, existing conventions, and actual abstraction boundaries before recommending changes. Do not refactor an established pattern without a concrete reason tied to the current task.

❌ Microservices without independent deployment, ownership, scaling, or isolation needs.

❌ Generic repositories that only wrap EF Core.

❌ An abstraction for every class “just in case” it needs mocking.

❌ A shared utility/project that becomes a dumping ground for unrelated behavior.

❌ Business rules implemented in controllers/endpoints, persistence hooks, or AppHost orchestration merely because those layers are convenient.

❌ Architecture where every feature crosses many projects for trivial changes.

## Review questions

Before introducing a layer, project, service, abstraction, or dependency:

- What problem does this solve?
- What coupling does it remove?
- What invariant does it protect?
- What complexity does it introduce?
- Could the existing framework or architecture solve this cleanly?
- Will common changes become easier or harder?
- Is the boundary likely to remain stable?

If the answer is primarily “future flexibility,” prefer waiting until the need is demonstrated.
