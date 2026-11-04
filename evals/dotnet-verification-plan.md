# .NET skill verification plan

Status: audit executed; overall Fail with explicit unverified boundaries. See [verification report](dotnet-verification/report.md). Skill remediation and permanent CI remain separate work.

## Goal and scope

Verify that `skills/dotnet/SKILL.md` and all 14 files in `skills/dotnet/references/` give accurate, current, practical .NET guidance. Check that named APIs and packages exist, examples work, recommendations fit their context, and generated code preserves security and correctness.

Use .NET 10 LTS as the primary baseline, matching the repository's stated scope. At execution time, record the SDK patch, C# version, package versions, database versions, support status, and verification date. Check older targets only where the skill explicitly claims compatibility. Treat preview features separately; newest syntax is not automatically best practice.

This is a verification task. Do not rewrite the skill, change dependencies, or expand sibling skills during the audit. Inspect `dotnet-scaffold`, `fastendpoints`, README, and evaluations only where they consume or contradict the core skill's guidance.

## Existing evidence and its limits

- `.github/workflows/ci.yml` validates skill metadata/reference paths and builds/tests a generated scaffolder solution. It does not compile all examples in the core skill's references.
- `evals/evals.json` contains seven `dotnet` scenarios and seven scaffolder scenarios. Expectations are prose assertions; their correctness and execution mechanism must also be verified.
- `evals/trigger-queries.json` and `evals/trigger-results.md` concern activation/routing. Reported routing success does not establish code quality or runtime correctness. Check whether raw run evidence is available before relying on historical results.

## 1. Inventory the claims and examples

Read the complete core skill and every reference. Produce a ledger with one row per independently verifiable claim or example:

| Field | Required evidence |
|---|---|
| Location | Repository commit, file, line, and exact claim or snippet |
| Classification | API fact, version claim, safety rule, contextual recommendation, preference, performance claim, or license claim |
| Applicability | SDK/framework/package/provider versions and necessary assumptions |
| Authority | Exact primary-source URL and section, source tag/commit when relevant, verification date |
| Proof | Fixture/test identifier, commands, exit codes, diagnostics, and observable result |
| Verdict | Pass, Fail, Needs qualification, or Unverified, with reason |

Resolve every local reference and route. Classify each code block as runnable example, illustrative fragment, or deliberately unsafe counterexample. Do not treat omitted domain types as invented APIs; record the minimum supporting types needed. Do not silently repair a snippet before assessing it.

Completion: all 15 core files inventoried, all code blocks classified, and all material claims assigned a proof method.

## 2. Verify against independent primary sources

Use version-specific Microsoft Learn/API documentation, .NET/ASP.NET Core/EF Core source and release notes, C# specifications, HTTP standards, database vendor documentation, and package-owner documentation/source/license files.

- Confirm symbol names, namespaces, extension-method packages, overloads, registration, lifetimes, supported targets, obsolescence, and behavior.
- Resolve actual package versions and inspect their dependency graphs. A permissive wrapper does not establish the license of its transitive dependencies. Record license conditions without inferring the user's commercial eligibility.
- Distinguish platform requirements from choices such as CQRS, Result libraries, DTO factory methods, test frameworks, and mediator/repository patterns. A preference can be valid without being an industry requirement.
- Challenge absolute rules with supported counterexamples: long-lived configured `HttpClient`, direct time reads where deterministic time is irrelevant, valid persistence boundaries, and different worker failure policies.
- Verify performance language such as “zero-allocation” with a defined scope and measurements, or mark it unsupported. Compilation alone cannot establish performance or AOT compatibility.

Completion: each material factual claim has independent evidence; unsupported or context-dependent advice is explicitly identified. An inaccessible source remains Unverified.

## 3. Compile and run the documented code

Create a small verification solution in an isolated working directory. Reuse the repository's fixture conventions where suitable; add only the projects required by the examples. Pin resolved SDK/package versions for reproducibility.

- Compile every positive C# example with minimal supporting scaffolding and document all additions. Verify JSON/XML project examples and shell/SQL commands in their intended environment.
- Keep deliberately unsafe examples out of positive build gates. Where useful, reproduce their failure as a negative control.
- Check analyzers and obsolete APIs. Record warnings and errors rather than hiding them or treating every stylistic diagnostic as a correctness defect.
- Detect each test project's actual runner before selecting CLI arguments. Prove focused filters select the intended tests and report a nonzero execution count; a successful zero-test invocation is not a pass.
- Run behavior checks for API registration/binding, serialization, ProblemDetails, authentication/authorization, options validation, DI scopes, cancellation, worker shutdown, and channel backpressure as applicable.
- Publish and smoke-test the examples that claim Native AOT/trim safety using an appropriate real runtime target. Do not extend that claim to arbitrary endpoint dependencies.

Completion: every positive example compiles and its relevant behavior passes, or has an explicit failure/unverified record. Build and test totals, logs, and environment details are retained.

## 4. Prove security, persistence, and concurrency invariants

Use disposable real PostgreSQL and SQL Server instances for provider-specific claims. Never substitute EF InMemory or SQLite as proof of another provider's semantics. Use real broker delivery where durability is claimed; an in-memory messaging harness proves a narrower boundary.

| Area | Required observable proof |
|---|---|
| Tenant isolation | Alternate tenant A/B requests and fresh DbContexts against a reused EF model; reject spoofed tenant headers and unresolved context; exercise reads, inserts, updates, deletes, attached entities, filter bypasses, bulk writes, cache keys, and background scopes |
| HTTP security | Authenticated and anonymous requests, permitted and forbidden resources, over-posting, invalid input, and production error responses without sensitive details |
| Concurrency | Competing operations with separate contexts/connections and deterministic barriers; verify persisted results, cancellation, lost-update handling, and lock scope |
| Outbox/inbox | Business data and outgoing messages commit atomically; rollback, publish failure, restart, redelivery, and simultaneous duplicates do not lose required messages or duplicate effects |
| Migrations/backfills | Old/new application compatibility, rerun and interruption recovery, actual batch commit boundaries, lock behavior, and failure recovery on disposable data |

For every invariant, include a negative case that would fail if its protection were removed. Do not use timing sleeps as concurrency proof. For keyed locks, check key growth and lifecycle as well as mutual exclusion.

Completion: security, data integrity, and delivery claims have runtime evidence at the claimed boundary. Missing Docker, database editions, broker access, or other prerequisites produce Unverified results, never silent fallback passes.

## 5. Verify the skill's actual output and evaluation criteria

Review the existing evaluation expectations against the independent evidence before using them as an oracle. For example, test whether an N+1 rubric wrongly excludes valid eager loading, and whether routing expectations agree with the instruction to combine core and scaffolding skills.

Run a compact scenario matrix against the audited skill revision:

1. Existing controllers/MediatR conventions remain intact.
2. Already-correct code receives no unnecessary refactor.
3. A greenfield API uses supported APIs and complete DI/configuration.
4. EF query changes produce the required data with justified query counts.
5. Tenant and resource authorization withstand adversarial requests.
6. A background worker respects scope, shutdown, and failure requirements.
7. Concurrent updates and duplicate messages preserve their invariants.
8. Version-sensitive C#, AOT, and test-runner advice matches the supplied project.
9. Unrelated-stack requests remain outside this skill's scope.

Build and execute generated code where code is requested; evaluate decisions against observable requirements rather than keyword presence. Capture the prompt, model/agent version, loaded skills, tool access, output, and execution evidence. Use a held-out variation for each scenario. Repeat failed or inconsistent cases to establish the failure mode; do not claim statistical reliability from this small matrix.

Completion: each scenario has an evidence-backed verdict, and any rubric that rewards incorrect or unnecessarily prescriptive output is identified.

## First verification targets from the initial inspection

These are questions to investigate, not confirmed findings:

| Location | Verification question |
|---|---|
| `SKILL.md`: anti-patterns and greenfield defaults | Which prohibitions are safety requirements, and which need context or supported exceptions? |
| `SKILL.md`: endpoint organization | Are the “zero-allocation” and “100% Native AOT trim-safe” claims supportable at the stated scope? |
| `SKILL.md`: license watchlist | Are package/version/license claims current, and do recommended alternatives retain relevant transitive dependencies? |
| `SKILL.md` and `references/testing.md` | Do MSTest/xUnit project files and test/filter commands match their actual runner and package versions? |
| `references/api.md` | Do exception-to-status mappings, ProblemDetails writing, customization, and content negotiation match the stated HTTP contract? |
| `references/multi-tenancy.md` | Is header fallback authorized? Does model caching preserve per-context tenant identity? Are writes protected beyond changing `TenantId`, and are unresolved/default-connection paths safe? |
| `references/concurrency.md` | Is the keyed-lock dictionary actually bounded? Are PostgreSQL `xmin` and SQL Server rowversion modeled correctly for their providers? Are thread-use explanations accurate? |
| `references/messaging.md` | Are outbox entities/model configuration and consumer inbox middleware complete? Does the example prevent concurrent duplicate effects? |
| `references/database-migrations.md` | Does the backfill commit between batches? Are idempotency and online/zero-downtime guarantees qualified for provider, edition, and deployment conditions? |
| `evals/evals.json` | Do expectations measure correctness rather than require one preferred architecture or API style? |

## 6. Report results and propose focused corrections

Produce an audit report plus the claim ledger and runnable proof artifacts. Group findings by security/data integrity, broken or obsolete APIs/examples, misleading guidance, and optional improvements. Each finding needs location, impact, evidence, applicable versions, and the smallest responsible correction.

Report separate verdicts for source accuracy, example compilation, runtime invariants, and agent output. Do not replace them with an aggregate score that can hide a security failure.

Acceptance conditions:

- Every core file, material claim, and code block has a recorded disposition.
- No unresolved security, data-loss, broken-API, or incorrect-provider behavior findings remain for an overall Pass.
- Contextual preferences are not presented as universal requirements.
- All positive examples compile; relevant runtime checks pass; AOT/performance claims have the proof they require.
- Actual skill-output scenarios pass against independently checked criteria.
- Unverified checks remain visible and prevent a claim of complete verification.

Stop after presenting the evidence and proposed corrections. Implementing fixes or adding permanent CI coverage is separate work; prioritize only demonstrated failures for that follow-up.
