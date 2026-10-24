---
name: dotnet
description: Use when building, modifying, debugging, architecting, or reviewing .NET/C# — C# language and async, ASP.NET Core Minimal APIs/endpoints/OpenAPI, EF Core/DbContext queries and migrations, architecture/CQRS/Vertical Slice, performance, auth/authz and security, code review, or NuGet/project files (.cs, .csproj, .sln/.slnx). Use even when the request describes the symptom without naming .NET (slow Includes, N+1, endpoint auth/status codes, ValueTask, tenant boundaries, idempotency). For creating a new solution/project/feature slice use dotnet-scaffold; for package/API evidence use dotnet-inspect; for Aspire workflows use the Aspire skill.
license: MIT
metadata:
  author: Timy
  version: "1.0"
---

# .NET Engineering

Use this skill as the default engineering guide for modern .NET work. It provides general .NET engineering principles, task classification, reference routing, dependency decisions, and validation. Repository-specific instructions, established conventions, and explicit task requirements take precedence.

## Core principles

- Understand the repository before changing it.
- Prefer the simplest correct solution that satisfies the requirement.
- Preserve existing project conventions unless there is a concrete reason to change them.
- Prefer framework/platform capabilities before introducing custom infrastructure.
- Minimize unnecessary dependencies, not dependencies at all costs.
- Do not add a third-party dependency merely because a library exists.
- Do not implement security-sensitive primitives yourself merely to avoid a dependency.
- Prefer explicit behavior over magic, hidden conventions, and unnecessary abstraction.
- Optimize for correctness, maintainability, observability, and performance together.
- Measure performance before making optimization claims.
- Make surgical changes; avoid unrelated refactoring.

## Instruction precedence

When guidance conflicts, use this order:

1. Security and correctness constraints.
2. Explicit task requirements.
3. Repository/project-specific instructions and conventions.
4. Existing architectural decisions.
5. This skill and its references.
6. Personal/default technology preferences.
7. Agent preferences.

An existing dependency or architecture is not automatically wrong because it differs from a preferred default. Change it only when the task requires it or the benefit clearly justifies the migration cost.

## Classify the task

Before implementation, determine the smallest set of concerns involved:

| Task involves | Action |
|---|---|
| C# syntax, idioms, type-system design, async patterns, or modernization | Read `references/csharp.md` when the task depends on language behavior or version-sensitive syntax |
| ASP.NET Core HTTP APIs, endpoints, contracts, OpenAPI | Read `references/api.md` when the task touches HTTP surface, status codes, validation, or API contracts |
| Architecture, boundaries, CQRS, Vertical Slice | Read `references/architecture.md` when the task changes structure, dependency direction, or module boundaries |
| EF Core, queries, modeling, transactions, migrations | Read `references/efcore.md` when the task touches DbContext, LINQ, persistence, or schema evolution |
| A measured or suspected performance problem | Read `references/performance.md` when performance is a requirement; if the bottleneck is database/EF, also read `references/efcore.md` |
| Authentication, authorization, untrusted input, secrets | Read `references/security.md` when the task handles identity, access control, or attacker-reachable input |
| Reviewing a diff, PR, or completed change | Read `references/review.md` when reviewing rather than implementing |
| Finding evidence for packages, APIs, or version diffs | Use the `dotnet-inspect` skill instead of guessing |
| .NET Aspire-specific workflow or current Aspire APIs | Use the official Aspire Agent Skill when available |

If a task crosses multiple areas, read only the smallest combination of references that covers the affected decisions. Do not load unrelated references merely because they exist.

## Before implementation

1. Inspect repository instructions and relevant agent guidance.
2. Identify the target .NET SDK, C# language version, frameworks, and package versions.
3. Locate application boundaries, entry points, tests, and affected code paths.
4. Understand existing architecture, dependency direction, and data flow.
5. Translate the task into concrete behavioral and technical requirements.
6. Classify the task and read only the relevant references.
7. Check whether the required capability already exists in the framework or an existing dependency.
8. For a new dependency, evaluate complexity, maintenance, security, performance, licensing, and whether a small explicit implementation would be sufficient.

Do not ask questions that repository inspection can answer.

## Scale the process to the task

Match investigation, implementation, and validation depth to scope, risk, and uncertainty.

- **Tiny change:** inspect the necessary context, make the smallest correct change, and run focused validation.
- **Normal feature:** inspect affected architecture, dependencies, call sites, and relevant tests; use applicable references.
- **Cross-cutting, security-sensitive, performance-sensitive, data-sensitive, or architectural change:** broaden investigation and validation proportionally.

Do not perform heavyweight architectural analysis or broad refactoring for a trivial change without a concrete reason.

## Gotchas

- Target framework is not the language version: verify `LangVersion` and SDK defaults independently before using newer C# syntax.
- EF Core in-memory providers are not relational substitutes: do not claim relational behavior (constraints, transactions, concurrency) is verified without a relational provider.
- Startup migration is a deployment decision: with multiple instances, restricted DB permissions, or a pipeline that can run migrations separately, prefer an explicit migration mechanism over racing at startup.
- `TypedResults` vs `IResult`/`Results`: follow the surrounding API's established return style; do not mix both without a reason.
- `new HttpClient()` per request exhausts sockets: use `IHttpClientFactory` or the project's configured HTTP infrastructure.
- Client-supplied tenant/role identifiers are not authorization proof: enforce scope server-side at the resource boundary.

## Modern C# and .NET

Use language and framework features supported by the repository's actual target version. Use newer syntax when it improves clarity, correctness, or maintainability—not merely because it is new. Do not rewrite stable code solely to adopt newer syntax unless modernization is part of the task.

For detailed language-feature guidance, use `references/csharp.md` when the task involves C# syntax, idioms, language-version decisions, type-system design, async patterns, or modernization.

Prefer framework-provided abstractions for common concerns. Examples include `TimeProvider` for testable time, `IHttpClientFactory`/HTTP resilience infrastructure for outbound HTTP, built-in validation/OpenAPI/problem-details facilities where they fit, and `Channel<T>` for in-process producer/consumer workflows.

Always verify version-sensitive APIs and behavior against the repository's actual target and installed packages before relying on them.

## API and application structure

For Minimal API applications, prefer clear endpoint organization that matches the size and architecture of the application.

For larger APIs or feature/vertical-slice-oriented applications, a small endpoint abstraction such as `IEndpoint` is a useful personal default when it improves discoverability, registration, and separation of HTTP concerns from application logic. Keep the abstraction small and framework-aligned.

Example:

```csharp
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder endpoints);
}
```

This is a personal/project pattern, not a universal .NET requirement. Do not introduce `IEndpoint` into an existing project merely because this skill recommends it. Follow an established endpoint organization unless there is a concrete reason to change it. For small APIs, direct route registration may be clearer.

When designing endpoints, read `references/api.md`.

## Configuration and hosted work

For application configuration, prefer modern .NET configuration and Options patterns rather than scattering ad hoc `IConfiguration` access throughout application code. Choose the appropriate options lifetime and validation behavior for the configuration's semantics, and validate required configuration early when appropriate.

For hosted/background work, account for cancellation, graceful shutdown, service lifetimes and scoped dependencies, exception behavior, retries, and overlap/concurrency requirements. Follow the repository's established hosting pattern rather than introducing infrastructure without a concrete need.

## Implementation guidance

- Use modern idiomatic C# appropriate to the repository's language version.
- Prefer clear types, explicit contracts, small cohesive components, and dependency injection where it improves composition and testability.
- Use async APIs for asynchronous I/O; do not introduce blocking waits to hide asynchronous work.
- Pass `CancellationToken` through meaningful asynchronous boundaries.
- Treat logging, metrics, tracing, configuration, and health behavior as production concerns where applicable.
- Keep public API contracts deliberate and stable.
- Keep persistence concerns appropriate to the application's architecture; do not add generic abstractions over EF Core without a concrete benefit.
- Keep security decisions explicit and verify authorization at the resource boundary.
- Avoid premature abstractions, speculative extensibility, and ceremony without a demonstrated need.

For domain-specific implementation guidance, route to the relevant reference rather than expanding this skill with duplicate material.

## Dependency decisions

Use this decision order:

```text
Framework/platform capability
        ↓
Existing project dependency
        ↓
Small explicit implementation for simple, non-sensitive functionality
        ↓
New third-party dependency when its value justifies the added cost
```

Before adding a new dependency, consider complexity, reliability, ecosystem support, security, performance, licensing, and migration/lock-in cost.

For a new dependency that is not already established by the repository, explain the trade-off and ask the user before adding it unless the task explicitly requested it or the repository requires it. Do not ask before using an existing dependency.

If execution is explicitly autonomous/headless and no user response is possible, do not silently introduce a new third-party dependency. Prefer an existing dependency or suitable framework capability when reasonable; otherwise stop at the dependency decision and report what is required, why it is justified, and what alternatives were considered.

Security-sensitive primitives are an explicit exception: prefer established framework/library implementations for cryptography, password hashing, token validation, OAuth/OIDC protocol behavior, and similar primitives rather than writing them yourself to save a dependency.

## Testing

Choose tests by the behavior and boundary being verified:

- Unit tests for pure domain/application logic and deterministic transformations.
- Integration tests for HTTP wiring, serialization, dependency injection, authentication/authorization, and externally observable API behavior.
- Relational database tests for behavior that depends on real database semantics when practical.
- Contract tests when compatibility with external consumers is important.

Do not mock EF Core into behaving like a relational database. Do not test implementation details merely to increase coverage. Test risk and behavior.

## .NET Aspire

When a task involves .NET Aspire, use the official Aspire Agent Skill when it is installed and available in the environment. Do not duplicate Aspire-specific workflows, API details, resource configuration guidance, or current Aspire documentation in this skill.

This skill still applies to general .NET engineering decisions around an Aspire codebase: correctness, architecture, dependencies, C#, APIs, persistence, testing, security, and performance. The official Aspire skill owns Aspire-specific workflows and APIs.

If the official Aspire skill is unavailable, follow the repository's existing Aspire conventions and verify version-sensitive behavior against official Aspire documentation rather than relying on remembered APIs.

## Personal defaults

These are defaults for genuinely new projects or new architecture where the repository does not already establish a pattern. They are not universal rules and must not override existing project conventions.

- CQRS where separate command/query models provide value.
- Martinothamar.Mediator for mediator-based application flow.
- A Result/ErrorOr-style result model for expected application failures.
- Explicit `ToEntity()` / `ToDto()` mapping rather than mapping magic.
- `IEndpoint` for feature-oriented Minimal API endpoint organization when the application is large enough to benefit from the abstraction.
- Fewer dependencies and explicit implementations for small, non-sensitive functionality.

For an existing project, follow its established architecture and dependencies. Do not introduce these defaults merely because they are preferred here.

## Validation

After changes, validate proportionally to risk:

1. Format/analyze the affected project when appropriate (`dotnet format --verify-no-changes`).
2. Build the smallest useful scope first (`dotnet build <project>`), then the relevant solution when warranted.
3. Run focused tests (`dotnet test --filter "<name>"`), followed by broader tests when practical and relevant.
4. Verify API contracts, database behavior, migrations, or security behavior when affected.
5. Review the final diff for unintended changes, unnecessary complexity, and regressions.
6. If performance is relevant, compare measurements before and after.

Do not claim a change is verified when the relevant validation was not actually performed. Report checks that could not be run and why.

Routing reminder: re-check `Classify the task` above and load only the reference needed; do not load unrelated references.

If the task is primarily scaffolding a new solution, project, or complete feature, use the `dotnet-scaffold` skill for the scaffolding workflow and use this skill for general .NET engineering guidance.
