# Code Review

Use this reference when reviewing a diff, pull request, implementation, or completed change. Review as a senior engineer: identify concrete risks and regressions, not stylistic disagreements.

## Review workflow

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

Check authentication, authorization, input handling, injection, SSRF, deserialization, file handling, secrets, sensitive logging, transport security, rate limiting, and tenant isolation where relevant.

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

❌ Blocking asynchronous work.

❌ Unbounded concurrency or unbounded collection/database queries.

❌ Repeated unmanaged `HttpClient` creation.

❌ N+1 database queries or accidental lazy-loading loops.

❌ Generic repository abstractions that only rename EF Core.

❌ Domain/business behavior hidden in middleware, persistence hooks, or Aspire AppHost orchestration.

❌ API contracts leaking internal persistence models.

❌ Broad exception catching that hides failures or turns unexpected errors into normal results.

❌ Security-sensitive custom implementations.

❌ New dependencies added without a concrete justification.

These are review signals, not automatic findings. Confirm the actual impact first.

## Review output

Each finding should state:

1. What is wrong.
2. Why it matters.
3. Where it occurs.
4. A concrete remediation when reasonably clear.

Prefer a small number of high-confidence findings over speculative concerns.

If no substantive problems are found, say so clearly after actually reviewing the full change and relevant context.
