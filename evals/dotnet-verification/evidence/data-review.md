# EF Core and concurrency snippet proof

Date: 2026-11-04. SDK10.0.302/C#14, net10.0 runtime10.0.13. Packages: Microsoft.EntityFrameworkCore.SqlServer10.0.13 and Npgsql10.0.0. Proof source/logs are in this directory. No skill/repository files changed.

## Result

All **13 positive C# blocks compile** across `references/efcore.md` and `references/concurrency.md`; the fourteenth block is a deliberately negative EF read-shape example and was excluded from positive gates. Build exit0,0errors,2warnings: NU1902/NU1903 from transitive System.Security.Cryptography.Pkcs9.0.11. These warnings were not suppressed. No C# warnings.

Real SQL Server diagnostic execution exited0. It used only the assigned disposable database `DataProof`, guarded by an InitialCatalog check before any schema/database mutation. It never touched the parent's `Audit` database. The proof resets and creates its own `DataProof` database on execution and leaves the final state available for inspection.

### Verified outcomes

- Original ResourceGate excludes a competing entrant while a deterministic barrier keeps the first inside, then releases the waiter.
- Original KeyedLockManager excludes a second operation on the same key while allowing a different key. After1002 unique keys its dictionary still contains1002 entries: the claimed bounded dictionary is demonstrably unbounded.
- Original Interlocked members produce8000 counter updates across8tasks and allow TryStart only once.
- Original Parallel.ForEachAsync block compiles/runs with eight dummy work items. This is execution evidence, not a throughput claim.
- Original Order class, including `[Timestamp] public byte[] Version { get; init; } = [];`, receives an8-byte SQL Server rowversion.
- Two separately loaded DbContexts retain the same starting rowversion. In each commit order, the first save succeeds, the stale save is rejected and translated by the exact documented catch handler, and a fresh context observes the winner's value.
- A separately deleted row triggers the exact handler's EntityDeletedConflictException branch.
- Original EF projected query returns only the seeded active user; original pagination executes; original ExecuteDelete removes the archived row; original interpolated FromSql returns the remaining owner-scoped row.
- An explicit N+1 baseline over3orders/6lines issues4reads. A single-query `.Include(x => x.Lines).AsSingleQuery()` returns the same3orders/6lines in1read. This is a concrete counterexample to the eval rubric excluding ordinary eager loading as an N+1 remedy.
- A child query with no Include returns2rows. Including a required Order navigation where one parent is soft-deleted returns1row. Generated SQL explicitly contains INNER JOIN over the filtered parent subquery. This contradicts `efcore.md:53`'s unconditional assertion that a filtered navigation simply becomes null/empty.

Raw output is `run-escalated.log`; it contains20 diagnostic PASS records, including compile/execution-only checks. These are assert-based checks in a console diagnostic, not20 framework tests. The stale-write scenarios deliberately sequence commits after both contexts load; they prove optimistic stale-write behavior in both orders, not arbitrary simultaneous transaction contention.

## Every block disposition

Block numbers count fenced C# blocks in document order. All14 blocks are accounted for.

| File/block/start | Classification | Compile/runtime disposition |
|---|---|---|
| efcore1:46 | Positive model filter | Exact filter compiled in FilterDb with `Order` alias to supporting FilteredOrder and currentTenantId as a context property. Real SQL required-navigation case executed |
| efcore2:74 | Positive projected read | Compiles; active-only projection executes on SQL Server |
| efcore3:85 | Deliberate negative read-shape example | Read/reviewed; not a positive gate. It loads a full entity/profile graph before mapping; no claim every Include is bad |
| efcore4:109 | Positive pagination | Compiles and executes with offset0/limit200 capped to100; negative-input validation is a caller-boundary obligation not shown |
| efcore5:126 | Positive set-based delete | Compiles; deletes archived row on SQL Server |
| efcore6:155 | Illustrative positive conflict-handling skeleton | Compiles; only no-conflict path executed. Empty catch comment is not a completed error translation, so do not copy as a silently swallowing production handler |
| efcore7:213 | Positive interpolated FromSql | Compiles; real parameterized owner query executes on SQL Server |
| concurrency1:12 | Positive ResourceGate | Compiles; deterministic mutual exclusion/release checks pass |
| concurrency2:38 | Positive KeyedLockManager | Compiles; same-key/different-key behavior passes; retained-key-growth reproduces unbounded dictionary |
| concurrency3:63 | Positive Interlocked members | Wrapped in supporting class; compiles; concurrent counter8000/start-once checks pass |
| concurrency4:79 | Positive bounded parallel loop | Compiles/runs with supplied items/ProcessItemAsync/ct |
| concurrency5:105 | Positive Order model | Compiles; exact model SQL rowversion and stale-write behavior pass. PostgreSQL xmin claim needs different uint/provider mapping |
| concurrency6:118 | Positive conflict handler | Compiles; stale-update and deleted-record translation branches both run against real SQL Server |
| concurrency7:144 | Positive Npgsql advisory-lock coordinator | Compiles unchanged against actual Npgsql10.0.0. PostgreSQL exclusion, post-commit release, exception rollback, and cancellation rollback subsequently passed; callback transaction sharing is not established |

## Supporting additions and environmental failures

- Added a small console csproj with actual EF SQL Server/Npgsql references, using directives, missing domain DTOs/entities, DbContexts/model configuration, and dummy work implementations. Original positive snippet logic retained.
- Added ResourceGate's absent PerformExclusiveWorkAsync as a callback-forwarding helper so barriers can drive the original gate logic. Added public counter read accessor solely for proof.
- Added missing domain exceptions, optional user profile, assets, eager-order lines, and filtered required parent/child models. Main rowversion Order class is the original snippet. Decimal precision configured in model support; no concurrency-token property changed.
- Global-filter Order name is isolated in a separate namespace with an alias to its supporting FilteredOrder, preventing an unrelated harness name collision with the exact concurrency Order model.
- Added command-count interceptor and deterministic TaskCompletionSource barriers. Retained-key introspection is diagnostic-only reflection to demonstrate lifetime growth, not proposed production code.
- First build failed sandbox NuGet access for missing Npgsql; escalated restore/build succeeded. First integration run failed sandbox socket permission; escalated run succeeded. These are environmental restrictions, not skill defects. Initial alias collision was a harness defect corrected before final build.

## Commands and retained evidence

- Build: `dotnet build /tmp/dotnet-data-proof/DataProof.csproj --no-restore --nologo`, exit0. `build-final.log` preserves exact warnings/counts. Earlier restore/build log: `build-escalated.log`.
- Run: set `DATAPROOF_SQL` to the assigned disposable SQL Server connection with database DataProof, then `dotnet /tmp/dotnet-data-proof/bin/Debug/net10.0/DataProof.dll`, exit0. Credentials intentionally omitted from this report. `run-escalated.log` preserves results and generated required-navigation SQL.
- This executable **resets DataProof** before running; use only a disposable database. It refuses any other InitialCatalog.

## Primary-source corroboration and fixes

- Required-navigation/filter behavior: https://learn.microsoft.com/en-us/ef/core/querying/filters#query-filters-and-required-navigations. Fix `efcore.md:53`: required navigations may produce inner joins that remove parent result rows; optional navigations can become null, with matching filters/relationship design chosen deliberately.
- Eager loading as an N+1 remedy: https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#beware-of-lazy-loading. Fix eval1: accept deliberate eager loading, projection, or split queries when they satisfy data/query-count/consistency requirements.
- SQL Server token behavior: https://learn.microsoft.com/en-us/ef/core/saving/concurrency. Current exact byte[] example is valid for SQL Server.
- PostgreSQL token mapping: https://www.npgsql.org/efcore/modeling/concurrency.html uses a uint Timestamp/IsRowVersion property for xmin. Fix `concurrency.md:111-113` to distinguish providers instead of implying the byte[] mapping covers both.
- Keyed-lock sample needs a bounded lifetime design or explicit limitation. Do not implement naive removal-after-release: queued waiters can retain the old semaphore while a newly created one admits a competing holder.

Not checked: callback transaction sharing, generic EF catch skeleton's conflict behavior, complete performance benchmarks, arbitrary pagination input validation, or broad transactional contention. Existing NuGet vulnerability warnings remain visible; no production suitability claim is made for this proof dependency graph.


## Follow-up PostgreSQL16 advisory-lock execution

The exact original DistributedTaskCoordinator type was reused from the compiled DataProof assembly, with two independent NpgsqlDataSource instances/coordinators/connections against the assigned disposable PostgreSQL16 endpoint. `/tmp/dotnet-pg-lock-proof/` contains the separate project, source, build/run logs. No SQL Server suite was rerun and no tables or schema were changed by this follow-up.

Build: `dotnet build /tmp/dotnet-pg-lock-proof/PgProof.csproj --nologo`, exit0. Because the project references the full DataProof assembly, the same two distinct transitive PKCS advisories repeat across project/restore phases (8warning occurrences,0errors). No C# diagnostics.

Run: set PGPROOF_CONNECTION to the assigned disposable endpoint, then `dotnet /tmp/dotnet-pg-lock-proof/bin/Debug/net10.0/PgProof.dll`, exit0. First sandbox run was denied socket access; escalated loopback run passed. `run-escalated.log` records distinct backend process IDs and eight passing assertions:

1. With a deterministic first-callback barrier holding the transaction open, an independent coordinator rejects the same key and does not invoke its callback.
2. The first callback completes and commits successfully.
3. After commit, the independent coordinator acquires the same key.
4. A deliberate callback exception propagates to the caller.
5. After that exception, transaction disposal/rollback releases the lock for the independent coordinator.
6. While a cancellable callback is held at its barrier, the independent coordinator still rejects the same key.
7. Cancellation propagates OperationCanceledException to the caller.
8. After cancellation, transaction disposal/rollback releases the lock for the independent coordinator.

This proves transaction-scoped cooperative exclusion and release behavior for the shown coordinator. The callback receives only CancellationToken, not the connection/transaction; this proof does not claim callback business writes participate in the transaction holding the advisory lock. No retries, lease expiry, crash/network partition, or nonparticipating writers were tested.
