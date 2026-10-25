# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
