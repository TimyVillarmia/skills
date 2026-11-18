---
name: dotnet
description: "Use when building, modifying, debugging, or reviewing .NET 10 / C# 14 backend code or .cs, .csproj, .sln, and .slnx files—including Minimal APIs, OpenAPI, Native AOT, EF Core, zero-downtime migrations, BackgroundService/channels, OpenTelemetry, Testcontainers/WebApplicationFactory, WolverineFx/MassTransit/outbox, multi-tenancy, concurrency, auth/security, rate limiting (AddRateLimiter), HybridCache, ResiliencePipeline, IOptions/ValidateOnStart, webhooks, performance, or NuGet license choices. DO NOT USE FOR: Aspire AppHost/CLI orchestration (use the official Aspire skill when available)."
license: MIT
metadata:
  author: Timy
  version: "1.2"
---

# .NET Engineering

Default engineering playbook for modern .NET backend work. Repository instructions, established conventions, and explicit task requirements take precedence over defaults here.

## Target version & Brownfield/Greenfield gate

- **Inspect first:** Read `<TargetFramework>`, `<LangVersion>`, `Directory.Build.props`, `global.json`, and `.slnx` or file-based entry files (`dotnet run app.cs` with `#:sdk`/`#:package`) before writing code.
- **Brownfield:** Preserve the repo's target framework, `LangVersion`, API style (`Controllers` vs `Minimal APIs`, `IResult` vs `TypedResults`), validator, mapper, test framework, and architecture. Flag EOL targets (`.NET 8` and `.NET 9` reached EOL on November 10, 2026) during reviews or upgrades without breaking compatibility.
- **Greenfield:** Default to `net10.0` (LTS) and C# 14, and apply the defaults below.

## Instruction precedence

1. Security and correctness constraints.
2. Explicit task requirements.
3. Repository conventions and existing architectural decisions.
4. This skill and its references.

## Classify the task

Read only the smallest combination of references covering the task:

| Task involves | Read |
|---|---|
| C# syntax, C# 14, type design, async, AOT JSON | `references/csharp.md` |
| HTTP APIs, Minimal APIs, middleware, OpenAPI, ProblemDetails, uploads, versioning | `references/api.md` |
| Observability, OpenTelemetry, `[LoggerMessage]`, redaction, health, containers | `references/observability.md` |
| `BackgroundService`, `PeriodicTimer`, `Channel<T>`, graceful shutdown | `references/hosted-services.md` |
| Architecture, DI lifetimes/keyed services, VSA/Clean/Modular Monolith, DTO mapping | `references/architecture.md` |
| EF Core, queries, modeling, transactions, execution strategies | `references/efcore.md` |
| Zero-downtime migrations, lock-safe DDL, backfills | `references/database-migrations.md` |
| Distributed messaging, Transactional Outbox, WolverineFx, MassTransit | `references/messaging.md` |
| Multi-tenancy, tenant isolation, global query filters | `references/multi-tenancy.md` |
| Testing, `WebApplicationFactory`, Testcontainers, Respawn, MTP | `references/testing.md` |
| Concurrency, `Lock`, `SemaphoreSlim`, `Parallel.ForEachAsync`, advisory locks | `references/concurrency.md` |
| Measured/suspected performance, allocations, BenchmarkDotNet | `references/performance.md` |
| Auth (JWT/OIDC), authorization, `AddRateLimiter`, CORS, webhooks, Data Protection | `references/security.md` |
| `IOptions<T>`, `[OptionsValidator]`, `ValidateOnStart()`, User Secrets, flags | `references/configuration.md` |
| `HybridCache`, L1/L2 coherence, stampede protection, `UseOutputCache` | `references/caching.md` |
| HTTP resilience, `ResiliencePipeline`, `UseRequestTimeouts`, retries, timeouts, `Retry-After` | `references/resilience.md` |
| Reviewing a diff, PR, completed change, or .NET 10 upgrade/breaking changes | `references/review.md` |
| .NET Aspire orchestration or AppHost wiring | Official Aspire skill when available (otherwise follow repo conventions + official Aspire docs) |

## Non-negotiable guardrails

- **Async & threads:** Return `Task` (or `ValueTask` only on measured hot paths); await all async I/O (never `.Result`/`.Wait()`/`async void` outside event handlers).
- **Time:** Inject `TimeProvider` and use `FakeTimeProvider` in tests rather than `DateTime.Now`/`DateTime.UtcNow`.
- **HTTP clients:** Inject `IHttpClientFactory` / typed clients with resilience handlers rather than `new HttpClient()`.
- **Configuration:** Bind strongly typed `IOptions<T>` validated with `[OptionsValidator]` + `ValidateOnStart()` rather than injecting `IConfiguration` into services.
- **Culture & uploads:** Pass `CultureInfo.InvariantCulture` for machine data; stream large uploads with `MultipartReader` or presigned URLs instead of buffering `IFormFile`.
- **EF Core & testing:** Inject `DbContext` directly (no generic repository wrappers); run migrations via scripts/bundles (not `Migrate()` at startup); test relational behavior with `Testcontainers` (never claim `UseInMemoryDatabase` verifies relational constraints).
- **Security & OpenAPI:** Enforce tenant/resource authorization server-side; use `AddOpenApi()` (never deprecated `.WithOpenApi()` / `ASPDEPR002`).

## Dependency decisions & license watchlist

Prefer capabilities in this order: **Framework/BCL** -> **Existing repo dependency** -> **Small explicit implementation** (non-sensitive code) -> **New dependency** (only with user approval, or stop and report if headless; always prefer established primitives for crypto/auth).

Do not add or upgrade these commercialized packages without explicit license approval:

| Package | Threshold | Action / Permissive Alternative |
|---|---|---|
| **MediatR** | >= 13.0 (commercial) | Direct handler injection, `Mediator.SourceGenerator` (MIT), or `WolverineFx` (MIT) |
| **FluentAssertions** | >= 8.0 (commercial) | MSTest/xUnit native assertions, `Shouldly` (BSD), or `AwesomeAssertions` (Apache-2.0) |
| **MassTransit** | >= 9.0 (commercial) | `WolverineFx` (MIT) or pin existing repos strictly `< 9.0.0` |
| **AutoMapper** | >= 15.0 (commercial) | Static DTO `From`/`Project` methods or `Riok.Mapperly` (Apache-2.0) |
| **Polly / Polly.Core** | Direct NuGet post-Nov 16, 2026 (OSMF) | Pre-Nov-16 v8.x (BSD-3) or transitive via `Microsoft.Extensions.Http.Resilience` / `Microsoft.Extensions.Resilience` |

## Modern greenfield defaults

Apply only when starting new projects or unconstrained features:
- Vertical slices with direct handler injection, `TypedResults`, and a `Result`/`ErrorOr` model for expected failures.
- Static `MapGroup` extensions on `IEndpointRouteBuilder` (avoids reflection scanning; Native AOT-friendly).
- `AddValidation()` + `[OptionsValidator]` / `ValidateOnStart()`.
- `AddOpenApi()` + `Scalar.AspNetCore` (`ProxyUrl = null` in Development).
- `AddRateLimiter`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Resilience`, and `HybridCache` (.NET 9+).
- `Guid.CreateVersion7()` (.NET 9+) for time-ordered keys (PostgreSQL/MySQL/SQLite; see `references/efcore.md` for SQL Server).
- `MSTest.Sdk` on Microsoft.Testing.Platform + `Testcontainers`.

## Validation

Run proportional standalone checks after edits:
1. `dotnet format --verify-no-changes` (when appropriate)
2. `dotnet build <project-or-sln> -c Debug`
3. `dotnet test -c Debug` (and `-c Release` when relevant; see `references/testing.md` for MTP/VSTest focused syntax)

Report using this contract (never claim unexecuted checks passed):
- **Build:** `Pass` / `Fail` / `Skipped`, configuration, warnings, errors.
- **Tests:** Executed, passed, failed, framework/runner.
- **Unverified Checks:** Skipped checks (e.g., Docker/Testcontainers unavailable) and reason.
- **Diff Review:** Summary of changes and confirmation of no unintended edits.
