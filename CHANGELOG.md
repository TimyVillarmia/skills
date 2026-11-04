# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-11-04

### Added
- **FastEndpoints Skill (`skills/fastendpoints`):**
  - Dedicated agent skill for FastEndpoints and the REPR (Request-Endpoint-Response) pattern in ASP.NET Core (.NET 8/9/10).
  - Progressive disclosure hierarchy with `SKILL.md` under 150 lines and specialized deep references:
    - `references/endpoint.md`: Endpoint generic variants, route groups, model binding, `Send*Async` status methods, domain mapping, and Native AOT support.
    - `references/validation.md`: Integrated FluentValidation, automatic 400 ProblemDetails interception, and in-handler programmatic validation.
    - `references/security.md`: JWT authentication, roles, policies, permission sets, claims-to-property binding, rate limiting, and API versioning.
    - `references/messaging.md`: Built-in in-process Command Bus, Event Bus (pub/sub), persistent Job Queues, and Server-Sent Events (SSE).
    - `references/testing.md`: Strongly-typed integration testing using `FastEndpoints.Testing`, `AppFixture`, and type-safe `POSTAsync`/`GETAsync` HTTP clients.
- **Ecosystem Routing (`skills/dotnet`):**
  - Added router entry in `skills/dotnet/SKILL.md` and `skills/dotnet/references/api.md` deferring to `fastendpoints` when FastEndpoints packages or patterns are detected.
- **Native Endpoint Organization (`skills/dotnet`, `skills/dotnet-scaffold`):**
  - Standardized Minimal API routing on native BCL `MapGroup` static extension methods (`Microsoft.AspNetCore.Routing`), replacing reflection-based `IEndpoint` interfaces.
  - Rebranded "Personal defaults" to "Modern greenfield defaults" for public credibility and Native AOT trim safety.
  - Updated `SliceTemplate` in `dotnet-scaffold` to use static `MapOrderEndpoints` and removed `IEndpoint.cs`.
- **Concurrency & Synchronization Reference (`skills/dotnet/references/concurrency.md`):**
  - Added dedicated reference for in-process async synchronization (`SemaphoreSlim`, keyed locks, `Interlocked`), thread pool safety, bounded `Parallel.ForEachAsync` limits, EF Core optimistic concurrency tokens (`[Timestamp]`), and PostgreSQL advisory locks.
- **Zero-Downtime Database Migrations Reference (`skills/dotnet/references/database-migrations.md`):**
  - Added reference for 3-phase Expand/Contract (parallel run) schema changes, lock-safe DDL (PostgreSQL `CONCURRENTLY` with `suppressTransaction: true`, `ADD CONSTRAINT ... NOT VALID`), CI/CD idempotent SQL script generation (`--idempotent`), and batched raw SQL backfills.
- **Distributed Messaging & Transactional Outbox Reference (`skills/dotnet/references/messaging.md`):**
  - Added reference for solving the dual-write problem via the Transactional Outbox pattern, MassTransit EF Core Outbox integration, idempotent consumers (the Inbox pattern), exponential backoff with jitter, dead-letter queues, and in-memory test harnesses (`ITestHarness`).
- **Multi-Tenancy & Tenant Isolation Reference (`skills/dotnet/references/multi-tenancy.md`):**
  - Added reference for automated tenant isolation via EF Core Global Query Filters, `SaveChangesInterceptor` automatic tenant assignment, ambient tenant resolution (JWT/headers), multi-tenant cache partitioning (`tenant:{id}:key`), and tenant context propagation across background queues.

## [1.2.0] - 2026-11-03

### Added
- **Options Validation & Startup Enforcement (`skills/dotnet/references/architecture.md`):**
  - Mandated fail-fast startup validation (`ValidateOnStart()`) and options validation for required external infrastructure and security configurations (connection strings, secrets, external APIs, bounded channels).
  - Added compile-time source-generated validation (`[OptionsValidator]`) recipes for .NET 8+ and Native AOT zero-reflection startup.
  - Added complex and cross-field options validation patterns via `IValidateOptions<T>` and `.Validate(...)`.
- **Integration Test Guidance (`skills/dotnet/references/testing.md`):**
  - Documented industry-standard in-memory configuration overrides (`builder.UseSetting`, `AddInMemoryCollection`) to satisfy `ValidateOnStart()` in `WebApplicationFactory` test fixtures without bypassing validation pipelines or mutating DI.

### Changed
- **Options Policy (`skills/dotnet/SKILL.md`):**
  - Promoted options validation from advisory recommendation to hard requirement for required infrastructure/secrets while keeping optional flags exempt.

## [1.1.0] - 2026-10-25

### Added
- **Deterministic Scaffolding (`dotnet-scaffold`):**
  - Cross-platform single-file C# scaffolding runner (`scripts/scaffold.cs`) supporting .NET 10 LTS (`10.0.402`).
  - Canonical project assets under `assets/`: `Directory.Build.props`, `Directory.Packages.props` (Central Package Management), `global.json` (`MSTest.Sdk` 4.5.1 with MTP runner), and `.editorconfig`.
  - Self-contained Minimal API vertical slice template under `assets/SliceTemplate/` with local Result type, `IEndpoint`, typed results, and handler DI registration.
  - Automated CPM migration and dual Debug/Release build & test verification.
- **Observability Reference (`skills/dotnet/references/observability.md`):**
  - OpenTelemetry tracing and metrics setup (`AddOpenTelemetry`).
  - Zero-allocation compile-time `[LoggerMessage]` logging.
  - Kubernetes liveness vs readiness health checks using built-in JSON response writer, explicitly forbidding external dependencies in liveness probes.
  - PII data redaction via `Microsoft.Extensions.Compliance.Redaction`.
- **Hosted Services Reference (`skills/dotnet/references/hosted-services.md`):**
  - Resilient `BackgroundService` execution loop with `PeriodicTimer`.
  - Safe scoped DI resolution via `IServiceScopeFactory` (preventing DbContext concurrency and lifetime violations).
  - Bounded `Channel<T>` producer/consumer patterns and graceful shutdown handling.
- **Repository Hygiene & CI:**
  - Standard MIT `LICENSE`.
  - GitHub Actions CI workflow (`.github/workflows/ci.yml`) pinning `actions/checkout@v7.0.1` and `actions/setup-dotnet@v6.0.0` with 40-character commit SHAs.
  - Automated frontmatter, line limit, reference integrity, and scaffolding verification.
- **Structured Output Contracts:**
  - Standardized code review finding contract in `references/review.md` (Severity, Location, Finding, Evidence, Recommended Fix).
  - Standardized validation summary contract in `skills/dotnet/SKILL.md` (Build, Tests, Unverified Checks, Diff Review).
- **Comprehensive Evals:**
  - 14 realistic, uncued evaluation cases with objective and executable assertions in `evals/evals.json`.
  - 20-query portable trigger evaluation matrix in `evals/trigger-queries.json` achieving 100% recall, 100% precision, and zero variance in benchmark evaluations.

### Changed
- **Target Framework:** Modernized default target framework from `net9.0` to .NET 10 LTS (`net10.0`). .NET 9 reaches End of Support on 2026-11-10.
- **Solution Format:** Switched solution generation to modern `.slnx` format (`dotnet new sln --format slnx`), gated on .NET SDK 10 / 9.0.200+.
- **OpenAPI Modernization:** Removed deprecated `.WithOpenApi()` (`ASPDEPR002`) in favor of built-in endpoint metadata and `AddOpenApiOperationTransformer`.
- **Testing Runner:** Migrated test configuration from legacy VSTest mode to `Microsoft.Testing.Platform` via `global.json` and current `MSTest.Sdk`.
- **API Error Handling:** Replaced uncompilable Minimal API slice snippets with compilable C# returning `Results<Created<T>, ProblemHttpResult>`. Added centralized `IExceptionHandler` pipeline.
- **Security & Authorization:** Added default-deny `FallbackPolicy`, resource-based authorization (`IAuthorizationService`), and BOLA / mass-assignment prevention guidelines.
- **EF Core Guidance:** Softened complex types vs owned entities into a version-aware trade-off table citing official Microsoft Learn documentation for EF Core 8, 9, and 10. Added Global Query Filters and Transactional Outbox patterns.
- **Style & Rationale:** Replaced bare absolutes and ALL-CAPS ("MUST", "NEVER") with concise one-line rationales across all reference files.
- **Skill Conciseness:** Trimmed `skills/dotnet-scaffold/SKILL.md` from 477 lines down to 109 lines (<200 lines) by moving large inline assets into dedicated files.

### Removed
- Removed hard-coded package version pins from archetypes in favor of Central Package Management.
- Removed CodeGraph block from `AGENTS.md` (repository is not indexed with `.codegraph/`).
- Removed unsupported performance and OpenAPI claims without documentation backing.

## [1.0.0] - 2026-09-19

### Added
- Initial release of `dotnet` and `dotnet-scaffold` skills.
