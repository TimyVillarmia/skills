# Code Review

Use this reference when reviewing a diff, pull request, implementation, or completed change. Review as a senior engineer: identify concrete risks and regressions, not stylistic disagreements.

## Review workflow

Review only — do not modify code unless the user asks to apply fixes.

1. Read repository/project instructions.
2. Understand the task and intended behavior.
3. Inspect the complete diff, not only the first suspicious file.
4. Read surrounding code and relevant call sites.
5. Trace changed behavior across API, application, domain, persistence, and infrastructure boundaries.
6. Check tests and important untested behavior.
7. Continue through the entire change after finding an issue.
8. Classify findings by impact and provide actionable evidence.

Do not review from the diff alone when surrounding context is required to determine correctness.

## Finding standard

Raise a finding when the change introduces a concrete or strongly supported problem:

- incorrect behavior
- security vulnerability
- data loss/corruption risk
- concurrency bug
- broken API contract
- database/query regression
- reliability/resource-management issue
- significant performance regression
- architecture violation creating real coupling/maintenance risk
- unnecessary complexity that materially increases failure or change cost

Do not report a preference as a defect merely because another implementation looks different.

## Severity

- **Critical** — severe security, data integrity, availability, or production failure risk.
- **High** — likely serious defect, security issue, compatibility break, or major operational impact.
- **Medium** — meaningful correctness, maintainability, performance, or reliability problem with bounded impact.
- **Low** — minor issue worth fixing when practical.

Avoid inflating severity. A code smell without a concrete consequence is not automatically a bug.

## Correctness

Check normal and edge cases, null/empty inputs, boundaries, state transitions, exception/failure paths, cancellation, retries and duplicate execution, ordering, races, disposal/lifetimes, and resource limits.

Trace assumptions across method and process boundaries rather than evaluating each changed method in isolation.

## API review

Check HTTP semantics, contracts, validation, authorization, information disclosure, pagination, idempotency, concurrency, retries, and OpenAPI accuracy.

## Security review

Check authentication, authorization (both endpoint-level permissions and handler/query-level resource ownership to prevent BOLA/IDOR — see [Resource-Based Authorization](authorization.md#resource-based-authorization-bola--idor-prevention)), input handling, injection (`FromSqlRaw` interpolation), SSRF, deserialization (`BinaryFormatter`), weak crypto (`MD5`/`SHA1`), file handling, secrets, sensitive logging, transport security, wildcard CORS with credentials, rate limiting, endpoints missing explicit auth/`AllowAnonymous` intent, and tenant isolation where relevant.

Run `dotnet package list --vulnerable --include-transitive` when reviewing dependencies. Skip or mark `INFO` for test-fixture and `appsettings.Development.json` dev-only values rather than inflating them to High/Critical, and note that static review does not replace runtime or penetration testing.

Reject hand-rolled security primitives when established implementations are available.

## EF Core review

Check query shape/projection, tracking, N+1, cartesian explosion, unbounded results, pagination ordering, indexes, transaction scope, concurrency, migration safety, client-side materialization, and round-trip count.

## Architecture review

Check whether the change respects existing architecture and dependency direction.

Be alert for business logic leaking into infrastructure, persistence leaking into public contracts, generic repositories without a real boundary, abstractions without purpose, feature logic scattered across unrelated layers, circular dependencies, premature microservices, and accidental module coupling.

Do not demand Clean Architecture, Vertical Slice Architecture, CQRS, or another pattern when the project does not need it.

## Performance review

Do not flag performance from intuition alone unless the cost is obvious and significant. When relevant, identify the mechanism: allocations, blocking I/O, excessive DB/network calls, unbounded concurrency, large materialization, inefficient queries, or retry amplification.

Prefer evidence from profiling, tracing, metrics, query plans, or representative benchmarks.

## Modern .NET/C# review

When the target version supports newer capabilities, check whether the implementation is unnecessarily using obsolete or error-prone patterns. Examples include repeated `new HttpClient()`, blocking async, manual time abstractions where `TimeProvider` is clearly needed, custom resilience plumbing where supported framework infrastructure fits, or unnecessarily complex language constructs.

Do not require a newer syntax/API merely because it exists. A stable older construct can be correct and clearer in context.

When auditing or upgrading NuGet dependencies, run `dotnet package list --outdated` (updating `Directory.Packages.props` when CPM is enabled) and apply major-version upgrades one package at a time, building and testing between each.

### ASP.NET Core 10 Upgrade & Breaking Changes

| Breaking Change / Deprecation | Flag When Seen | .NET 10 Replacement / Action |
|---|---|---|
| `.WithOpenApi()` (`ASPDEPR002`) | Minimal API OpenAPI metadata | Use `AddOpenApi()` + document/operation transformers |
| `ForwardedHeadersOptions.KnownNetworks` / `Microsoft.AspNetCore.HttpOverrides.IPNetwork` | Reverse-proxy configuration | Migrate to `KnownIPNetworks` + `System.Net.IPNetwork` |
| Cookie redirects on API endpoints | `[ApiController]`, JSON Minimal APIs, `TypedResults` with cookie auth | Returns `401`/`403` via `IDisableCookieRedirectMetadata`; opt back in with `IAllowCookieRedirectMetadata` only if redirects are intentional |
| `IExceptionHandler` diagnostic suppression | Custom `IExceptionHandler` returning `true` | Framework diagnostics are suppressed by default; set `ExceptionHandlerOptions.SuppressDiagnosticsCallback` if telemetry/alerts depend on them |
| `WebHostBuilder` / `IWebHost` / `WebHost` | Legacy startup wiring | Migrate to `WebApplication.CreateBuilder()` or `Host.CreateApplicationBuilder()` |
| `IActionContextAccessor` / `ActionContextAccessor` | Controller/helper DI | Replace with `HttpContext`, `LinkGenerator`, or endpoint routing |
| `IncludeOpenAPIAnalyzers` / `Microsoft.Extensions.ApiDescription.Client` | `.csproj` / build tooling | Remove deprecated MVC OpenAPI analyzers and client generator package |

## Testing review

Check whether tests verify behavior rather than implementation details. Look for missing coverage around changed behavior, error paths, authorization, concurrency, persistence semantics, and API contracts when applicable.

Do not require tests solely to increase a coverage number; require confidence appropriate to change risk.

## Maintainability and dependencies

Review new dependencies based on justification:

- Does it solve meaningful complexity?
- Does the project already use it?
- Could the framework or a small implementation suffice?
- What maintenance/security/licensing cost does it add?
- Is migration or long-term coupling justified?

Do not request replacement of an established dependency merely because another library is preferred.

## Common anti-patterns to check

Check the **Non-negotiable guardrails** and **License watchlist** in `SKILL.md` (`async void`, sync-over-async, `DateTime.UtcNow`, unmanaged `HttpClient`, generic EF repositories, commercialized packages), plus:
- N+1 database queries, accidental lazy-loading loops, or unbounded `ToListAsync()`.
- `HybridCache`-cached reads missing matching key/tag invalidation in mutating commands.
- Domain/business behavior hidden in middleware, persistence hooks, or Aspire AppHost orchestration.
- API contracts leaking internal persistence entities or raw exception details.

Treat these as review signals, not automatic findings—confirm concrete impact first.

## Review output

Structure each finding using this exact output contract:

- **Severity:** `Critical` | `High` | `Medium` | `Low`
- **Location:** `<file-path>:<line-number>` (or symbol/method name)
- **Finding:** Concise statement of what is wrong.
- **Evidence:** Concrete explanation of why it matters, including failure mode, security risk, query execution plan, or race condition.
- **Recommended Fix:** Drop-in code snippet or specific remediation instructions.

Example:

- **Severity:** High
- **Location:** `src/Catalog/Endpoints/GetProductEndpoint.cs:24`
- **Finding:** Mass assignment vulnerability via direct binding to EF Core entity.
- **Evidence:** Client payload binds directly to the tracked `Product` entity, enabling callers to overwrite sensitive internal properties (e.g., `IsPublished`, `Price`).
- **Recommended Fix:** Bind to an explicit `UpdateProductRequest` record and map only allowed fields to the tracked entity.

Prefer a small number of high-confidence findings over speculative concerns.

Close every review with a one-line **Verdict:** `Ready to commit/merge` | `Ready with minor nits` | `Changes required` (naming the blocking items).

If no substantive problems are found, state so clearly after reviewing the full change and relevant context.
