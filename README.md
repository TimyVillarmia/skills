# Agent skills

AI agent skills for software engineering and architecture. Built for Antigravity, Claude Code, Cursor, GitHub Copilot, and LLM coding tools that support skills.

## Installation

### Via `npx skills` ([skills.sh](https://skills.sh))

Install the full skills collection into your current workspace:

```bash
npx skills add TimyVillarmia/skills
```

Or install individual skills:

```bash
# Core .NET engineering rules (C#, EF Core, APIs, architecture, security, testing)
npx skills add TimyVillarmia/skills/skills/dotnet

# FastEndpoints REPR pattern (endpoints, validation, security, testing)
npx skills add TimyVillarmia/skills/skills/fastendpoints

# .NET solution, project, and vertical slice scaffolding
npx skills add TimyVillarmia/skills/skills/dotnet-scaffold
```

### Manual installation

Clone or copy the skill directory into `.agents/skills/` or `~/.agents/skills/`:

```bash
git clone git@github.com:TimyVillarmia/skills.git ~/.agents/skills/personal
```

---

## Included skills

### 1. `dotnet`
Engineering standards and task routing for .NET 10 LTS. Includes 13 domain reference guides:
- `csharp.md`: C# idioms, primary constructors, pattern matching, async ValueTask vs Task choices, and JSON source generation for Native AOT.
- `api.md`: Minimal APIs, OpenAPI transformers, centralized Problem Details error handlers, and API versioning with Asp.Versioning.Http.
- `architecture.md`: Vertical slice design, CQRS boundaries, mapper source generators (Riok.Mapperly), and mandatory `ValidateOnStart()` options validation for external infrastructure.
- `concurrency.md`: In-process async locking (SemaphoreSlim, keyed locks), thread pool safety, Parallel.ForEachAsync limits, EF Core optimistic concurrency tokens, and PostgreSQL advisory locks.
- `database-migrations.md`: Zero-downtime Expand/Contract schema evolution, lock-safe DDL (CONCURRENTLY, NOT VALID), CI/CD idempotent SQL scripts, and bulk backfills.
- `messaging.md`: Distributed messaging, solving dual-write with MassTransit Transactional Outbox, idempotent consumers (the Inbox), exponential retry, and dead-letter queues.
- `observability.md`: OpenTelemetry tracing and metrics, zero-allocation `[LoggerMessage]`, data redaction, and built-in health probes.
- `hosted-services.md`: Resilient BackgroundService loops, scoped DI resolution via IServiceScopeFactory, PeriodicTimer, and bounded channels.
- `efcore.md`: EF Core query tuning, split queries, complex types, global query filters, and transactional outbox.
- `performance.md`: Allocation tracking, Span and Memory usage, and BenchmarkDotNet rules.
- `security.md`: Authentication, default-deny policies, resource authorization, and cryptographic safety.
- `testing.md`: MSTest.Sdk on Microsoft.Testing.Platform, xUnit v3, and Testcontainers with Respawn for relational databases.
- `review.md`: Code review checklist with structured findings (severity, location, finding, evidence, fix).

### 2. `fastendpoints`
REPR (Request-Endpoint-Response) pattern architecture for ASP.NET Core APIs:
- `endpoint.md`: Class-per-endpoint definitions (`Endpoint<TReq, TRes>`), route groups, model binding attributes (`[FromClaim]`), `Send*Async` status methods, and Native AOT support.
- `validation.md`: Integrated FluentValidation rules via `Validator<TRequest>`, automatic 400 Problem Details interception, and in-handler error accumulation.
- `security.md`: Authentication defaults, `AllowAnonymous()`, policies, permissions, claims binding, and endpoint throttling.
- `messaging.md`: In-process Command Bus (`ICommand<TResult>`), Event Bus (pub/sub), background Job Queues, and Server-Sent Events.
- `testing.md`: Type-safe integration testing with FastEndpoints.Testing, AppFixture, and generic `POSTAsync`/`GETAsync` HTTP clients.

### 3. `dotnet-scaffold`
Deterministic project generation for solutions, test projects, and vertical slices:
- `scripts/scaffold.cs`: Cross-platform C# runner (`dotnet run scaffold.cs`) targeting .NET 10 LTS.
- `assets/`: Central Package Management (`Directory.Packages.props`), common build properties (`Directory.Build.props`), `global.json`, `.editorconfig`, and a vertical slice template.
- Solution layout: Modern `.slnx` format with automated Debug and Release validation.

---

## Evaluations

Skill routing and trigger behavior are validated using test matrices under `evals/`:
- `evals/evals.json`: Test cases with executable assertions for `dotnet`, `fastendpoints`, and `dotnet-scaffold`.
- `evals/trigger-queries.json`: Portable evaluation query set across training and validation splits.
- `evals/trigger-results.md`: Benchmarked activation rates, recall (100%), and precision (100%).

---

## License

MIT (c) 2026 Timy
