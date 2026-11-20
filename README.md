<p align="center">
  <img src="assets/logo.png" alt=".NET Engineering Skills logo" width="128" />
</p>

# .NET Engineering Agent Skills

Agent skills for .NET software engineering and architecture. Works with Antigravity, Claude Code, Cursor, GitHub Copilot, and other tools that support the Agent Skills format.

## Installation

### Antigravity (`agy` CLI)

Install all skills as an Antigravity plugin:

```bash
agy plugin install https://github.com/TimyVillarmia/skills
```

### Using `npx skills` ([skills.sh](https://skills.sh))

Install all skills into your current workspace:

```bash
npx skills add TimyVillarmia/skills
```

Or install a single skill:

```bash
# Core .NET engineering rules
npx skills add TimyVillarmia/skills/skills/dotnet

# FastEndpoints REPR pattern
npx skills add TimyVillarmia/skills/skills/fastendpoints

# .NET solution, project, and vertical slice scaffolding
npx skills add TimyVillarmia/skills/skills/dotnet-scaffold
```

### Manual installation

Clone the repository into `.agents/skills/` or `~/.agents/skills/`:

```bash
git clone git@github.com:TimyVillarmia/skills.git ~/.agents/skills/personal
```

---

## Included skills

| Skill | Purpose | Contents |
|---|---|---|
| [`dotnet`](skills/dotnet/SKILL.md) | Engineering standards and task routing for .NET 10 LTS | 17 reference guides |
| [`fastendpoints`](skills/fastendpoints/SKILL.md) | REPR endpoint architecture for ASP.NET Core APIs | 5 reference guides |
| [`dotnet-scaffold`](skills/dotnet-scaffold/SKILL.md) | Project generation for `.slnx` solutions, tests, and vertical slices | C# runner and project templates |

### 1. `dotnet`

| Reference | Covers |
|---|---|
| `csharp.md` | C# idioms, primary constructors, pattern matching, `ValueTask` vs `Task`, and JSON source generation for Native AOT |
| `api.md` | Minimal APIs, OpenAPI transformers, `ProblemDetails` error handlers, file upload streaming and size limits, and API versioning |
| `architecture.md` | Vertical slice design, CQRS boundaries, and compile-time mapper source generators using `Riok.Mapperly` |
| `configuration.md` | Options pattern with `[OptionsValidator]` and `ValidateOnStart()`, source precedence, `__` environment variable binding, User Secrets, and feature flags |
| `caching.md` | `HybridCache` in-memory and distributed coherence, stampede protection, `[ImmutableObject(true)]`, tag eviction, and HTTP Output Caching |
| `resilience.md` | Outbound HTTP resilience with `AddStandardResilienceHandler`, non-HTTP `ResiliencePipeline`, timeout budgets, and `Retry-After` handling |
| `concurrency.md` | Async locking with `SemaphoreSlim` and keyed locks, thread pool safety, `Parallel.ForEachAsync` bounds, optimistic concurrency, and PostgreSQL advisory locks |
| `database-migrations.md` | Expand and Contract schema changes, lock-safe DDL (`CONCURRENTLY`, `NOT VALID`), idempotent SQL scripts, and bulk backfills |
| `messaging.md` | Native EF Core and WolverineFx Transactional Outbox, pinned MassTransit v8, idempotent consumers, retries, and dead-letter queues |
| `multi-tenancy.md` | Tenant isolation, EF Core Global Query Filters, `SaveChangesInterceptor` enforcement, and tenant-scoped cache keys |
| `observability.md` | OpenTelemetry tracing and metrics, `[LoggerMessage]`, data redaction, health probes, and container defaults |
| `hosted-services.md` | `BackgroundService` loops, scoped DI via `IServiceScopeFactory`, `PeriodicTimer`, and bounded channels |
| `efcore.md` | EF Core query tuning, split queries, complex types, global query filters, audit interceptors, and `Guid.CreateVersion7()` |
| `performance.md` | Allocation tracking, `Span<T>` and `Memory<T>`, and BenchmarkDotNet rules |
| `security.md` | Identity and passkeys, `JsonWebTokenHandler`, default-deny policies, `AddRateLimiter`, CORS, and webhook HMAC verification |
| `testing.md` | `MSTest.Sdk` on Microsoft.Testing.Platform, xUnit v3, Testcontainers with Respawn, `FakeTimeProvider`, and OpenAPI contract tests |
| `review.md` | Code review checklist with structured findings (severity, location, finding, evidence, fix) |

> [!TIP]
> Pair `dotnet` with the official [Aspire agent skills](https://github.com/microsoft/aspire). `dotnet` covers general .NET engineering; Aspire owns local orchestration, AppHost wiring, and deployment. `dotnet` works standalone without Aspire.

### 2. `fastendpoints`

| Reference | Covers |
|---|---|
| `endpoint.md` | Class-per-endpoint definitions (`Endpoint<TReq, TRes>`), route groups, `[FromClaim]`, `Send*Async` status methods, and Native AOT |
| `validation.md` | FluentValidation rules via `Validator<TRequest>`, automatic 400 Problem Details, and in-handler error accumulation |
| `security.md` | Authentication defaults, `AllowAnonymous()`, policies, permissions, claims binding, and endpoint throttling |
| `messaging.md` | In-process Command Bus (`ICommand<TResult>`), Event Bus, background Job Queues, and Server-Sent Events |
| `testing.md` | Integration testing with `FastEndpoints.Testing`, `AppFixture`, and generic `POSTAsync` and `GETAsync` clients |

### 3. `dotnet-scaffold`

| Component | Covers |
|---|---|
| Greenfield intake | Asks auth/IdP first (no default); confirms database, architecture, and extras before scaffolding a new solution |
| `scripts/scaffold.cs` | Cross-platform C# runner (`dotnet run scaffold.cs`) targeting .NET 10 LTS, with `--database` selecting the Testcontainers module |
| `assets/` | Central Package Management (`Directory.Packages.props`), `Directory.Build.props`, `global.json`, `.editorconfig`, and `SliceTemplate` |
| Solution layout | `.slnx` solution format with Debug and Release build validation |

---

## License

MIT (c) 2026 Timy
