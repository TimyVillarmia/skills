# Agent Skills

Curated, tool-agnostic AI agent skills for modern software engineering and architecture. Designed for use with Antigravity, Claude Code, Cursor, GitHub Copilot, and any LLM coding assistant supporting agent skills.

## Installation

### Via `npx skills` ([skills.sh](https://skills.sh))

Install the entire skills collection into your current workspace:

```bash
npx skills add TimyVillarmia/skills
```

Or install individual skills:

```bash
# Core .NET engineering skill (C#, EF Core, API, architecture, performance, security, observability, hosted services)
npx skills add TimyVillarmia/skills/skills/dotnet

# .NET solution, project, and vertical slice scaffolding
npx skills add TimyVillarmia/skills/skills/dotnet-scaffold
```

### Manual Installation

Clone or copy the desired skill directory into your project's `.agents/skills/` or `~/.agents/skills/`:

```bash
git clone git@github.com:TimyVillarmia/skills.git ~/.agents/skills/personal
```

---

## Included Skills

### 1. `dotnet`
Comprehensive engineering rules and task routing for modern .NET (targeting .NET 10 LTS):
- **`csharp.md`:** Modern C# idioms, primary constructors, pattern matching, async ValueTask vs Task guidance, System.Text.Json source generation, and Native AOT constraints.
- **`api.md`:** ASP.NET Core Minimal APIs, OpenAPI transformers, centralized `IExceptionHandler` / `ProblemDetails`, and API versioning with `Asp.Versioning.Http`.
- **`observability.md`:** OpenTelemetry tracing and metrics, zero-allocation `[LoggerMessage]`, data redaction, and built-in JSON liveness/readiness health check probes.
- **`hosted-services.md`:** Resilient `BackgroundService` execution model, scoped DI resolution via `IServiceScopeFactory`, `PeriodicTimer`, and bounded `Channel<T>` producer/consumer patterns.
- **`efcore.md`:** EF Core query optimization, split queries vs single queries, complex types vs owned entities, global query filters, and transactional outbox pattern.
- **`architecture.md`:** Vertical Slice architecture, CQRS boundaries, mapper generator trade-offs (Riok.Mapperly), and dependency direction.
- **`performance.md`:** Allocation auditing, Span/Memory usage, and BenchmarkDotNet standards.
- **`security.md`:** Authentication, default-deny `FallbackPolicy`, resource-based authorization (`IAuthorizationService`), BOLA / mass-assignment prevention, and cryptographic safety.
- **`testing.md`:** Modern testing architecture with current `MSTest.Sdk` (Microsoft.Testing.Platform runner mode), xUnit v3, and Testcontainers with Respawn for relational databases.
- **`review.md`:** Structured code review checklist and strict output contract (Severity, Location, Finding, Evidence, Recommended Fix).

### 2. `dotnet-scaffold`
Deterministic scaffolding for new .NET solutions, test projects, and vertical slices:
- **`scripts/scaffold.cs`:** Cross-platform single-file C# scaffolding runner (`dotnet run scaffold.cs`) targeting .NET 10 LTS.
- **`assets/`:** Pre-configured `Directory.Build.props`, `Directory.Packages.props` (Central Package Management), `global.json` (`MSTest.Sdk` + MTP runner), `.editorconfig`, and `SliceTemplate/` (local Result type, `IEndpoint`, typed Minimal API endpoint, handler, and request/response DTOs).
- **Solution layout:** Modern `.slnx` solution format with strict build & test verification in both Debug and Release configurations.

---

## Evaluations

Skill routing and trigger behavior are validated using test matrices under `evals/`:
- `evals/evals.json`: Comprehensive test cases with objective and executable assertions for `dotnet` and `dotnet-scaffold`.
- `evals/trigger-queries.json`: Portable 20-query evaluation set (60/40 train/validation split).
- `evals/trigger-results.md`: Benchmarked activation rates, recall (100%), precision (100%), and zero-variance stability across independent runs.

---

## License

MIT (c) 2026 Timy
