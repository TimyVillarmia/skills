# Independent .NET skill forward test

## Prompt

> Build a small .NET 10 Minimal API with a tenant-scoped documents store using EF Core. Tenants come from validated authentication claims. Include document lookup/update and integration proof that alternating requests from two tenants cannot read or alter each other's documents. Preserve ordinary HTTP status semantics. Also add a background worker that uses scoped services and shuts down cleanly.

This was an independent realistic implementation, not a review of the skill. All authored application/test/report files are under `/tmp/dotnet-forward-test`; no live application/database was changed. Normal SDK/NuGet caches were populated by restore. Testcontainers created and disposed its own SQL Server containers; it did not use the parent's or existing app's database.

## Skill files loaded

Primary `/home/timydev/projects/skills/skills/dotnet/SKILL.md` (metadata version 1.6).

Routed supporting files:

- `/home/timydev/projects/skills/skills/dotnet-scaffold/SKILL.md`
- `/home/timydev/projects/skills/skills/dotnet/references/api.md`
- `/home/timydev/projects/skills/skills/dotnet/references/efcore.md`
- `/home/timydev/projects/skills/skills/dotnet/references/multi-tenancy.md`
- `/home/timydev/projects/skills/skills/dotnet/references/security.md`
- `/home/timydev/projects/skills/skills/dotnet/references/testing.md`
- `/home/timydev/projects/skills/skills/dotnet/references/hosted-services.md`
- `/home/timydev/projects/skills/skills/dotnet-scaffold/scripts/scaffold.cs`
- `/home/timydev/.agents/skills/code-testing/SKILL.md`
- `/home/timydev/.agents/skills/run-tests/SKILL.md`
- `/home/timydev/.codex/RTK.md`

RTK `bulk-read` failed because `GEMINI_API_KEY` was not set. Used `rtk bypass cat` to read the routed references; some combined terminal outputs were truncated. Testing reference was subsequently read separately in full. No memory was needed for this standalone sample.

## Decisions and generated behavior

- Used deterministic scaffold script because the scaffold skill explicitly required it. Script generated global SDK/MTP configuration, central packages, analyzer build policies, a Minimal API project and MSTest test project; its initial generated smoke test passed Debug/Release, but was then deleted and replaced by useful tests.
- Used framework cookie authentication and Data Protection rather than implementing JWT parsing or adding a security primitive. The application accepts only framework-validated authentication tickets and exactly one nonempty GUID tenant claim. There is no `X-Tenant-Id` fallback. Login/identity issuance is outside this narrow sample; tests issue protected tickets via the application's actual CookieAuthenticationOptions.TicketDataFormat.
- Used `AddDbContext`, not pooling: request-sensitive tenant state stays in scoped dependencies. The filter references a DbContext instance property, following `efcore.md`'s dynamic parameter guidance. This is not a verbatim reproduction of the `multi-tenancy.md` dynamic Expression.Constant helper, and successful generated code does not certify that original snippet.
- Query filter applies to lookup/update. Composite tenant/document key and SaveChangesAsync ownership checks provide write constraints; HTTP bodies accept only the mutable Title DTO.
- Used native route group extensions, dedicated DocumentService, typed HTTP results, cancellation propagation, problem details for validation/errors, fail-fast Store options validation, and built-in OpenAPI.
- Ordinary statuses remain 200 lookup, empty 204 update, 404 absent/foreign document, 400 invalid title, 401 unauthenticated, 403 authenticated without valid tenant.
- Worker creates/disposes async scopes each pass, awaits PeriodicTimer with cancellation, and exits normally on shutdown. Scoped maintenance task logs only; it performs no tenant data access and needs no fabricated ambient tenant.
- No generic repository, mediator, mapping dependency, UI package, live startup migration, or EF InMemory provider was introduced.

## Exact versions

- .NET SDK 10.0.302, target net10.0 (SDK default language version).
- MSTest.Sdk 4.5.1 (Microsoft.Testing.Platform 2.5.0 resolved by SDK).
- Microsoft.AspNetCore.OpenApi 10.0.13 (template-generated).
- Microsoft.AspNetCore.Mvc.Testing 10.0.14 (scaffolder resolved current compatible package).
- Microsoft.EntityFrameworkCore.SqlServer 10.0.13 (installed cache version).
- Testcontainers.MsSql 4.16.0 (installed cache version).
- System.Security.Cryptography.Pkcs 10.0.13 (central transitive pin to remediate audit warnings from original 9.0.11 resolution).
- SQL image: mcr.microsoft.com/mssql/server:2022-latest (existing locally cached image; tag is not a reproducible digest pin).

## Commands and results

Initial environment:

```sh
dotnet --list-sdks
# 10.0.302

dotnet run /home/timydev/projects/skills/skills/dotnet-scaffold/scripts/scaffold.cs -- -n TenantDocs -o /tmp/dotnet-forward-test/app
```

The first sandboxed scaffold attempt failed with a read-only SDK runfile cache. Retried the same command with approved escalation: exit 0. Generated baseline built with 0 warnings/errors in Debug and Release; baseline scaffold smoke passed 1/1 in each. Those are baseline scaffolder results, not the tenant proof.

Sandboxed Docker discovery failed permission denied on `/var/run/docker.sock`; approved escalation succeeded. Existing application containers were only listed, never mutated. Initial sandboxed build exited 1 with a terse 0-error failure; rerun from sample working directory with escalation succeeded.

All remaining commands ran from `/tmp/dotnet-forward-test/app`:

```sh
dotnet build TenantDocs.slnx -c Debug > /tmp/dotnet-forward-test/build-debug.log 2>&1
```

Exit 0, 9 warnings, 0 errors. Included NU1902/NU1903 for transitive System.Security.Cryptography.Pkcs 9.0.11 and CA1848 for ordinary logging. Added central transitive pin 10.0.13 and converted logger call to source-generated LoggerMessage; no warning suppression.

```sh
dotnet test --project tests/TenantDocs.Tests/TenantDocs.Tests.csproj -c Debug --no-build --report-trx --results-directory /tmp/dotnet-forward-test/results-debug > /tmp/dotnet-forward-test/test-debug.log 2>&1
```

Initial behavioral run: exit 0, 2 executed, 2 passed, 0 failed/skipped.

Final verification after remediation:

```sh
dotnet build TenantDocs.slnx -c Debug > /tmp/dotnet-forward-test/build-debug-final.log 2>&1
dotnet test --project tests/TenantDocs.Tests/TenantDocs.Tests.csproj -c Debug --no-build --report-trx --results-directory /tmp/dotnet-forward-test/results-debug-final > /tmp/dotnet-forward-test/test-debug-final.log 2>&1
dotnet build TenantDocs.slnx -c Release > /tmp/dotnet-forward-test/build-release-final.log 2>&1
dotnet test --project tests/TenantDocs.Tests/TenantDocs.Tests.csproj -c Release --no-build --report-trx --results-directory /tmp/dotnet-forward-test/results-release-final > /tmp/dotnet-forward-test/test-release-final.log 2>&1
```

- Build Debug: Pass, exit 0, 0 warnings/errors.
- Build Release: Pass, exit 0, 0 warnings/errors (Release warnings as errors).
- Tests Debug: Pass, exit 0, 2 executed, 2 passed, 0 failed/skipped, MSTest/native MTP.
- Tests Release: Pass, exit 0, 2 executed, 2 passed, 0 failed/skipped, MSTest/native MTP.
- Both final TRX files parsed; expected exact test names and 2/2 successful counters verified. `trx-validation.txt` preserves Release parse evidence.

| Requirement | Evidence |
|---|---|
| Tenant isolation across cached EF model and alternating requests | `AlternatingTenantsCannotReadOrModifyEachOthersDocuments`: 3 A/B alternations on same HTTP client/application; own reads succeed, foreign GET/PUT return 404; final own titles remain A-2 and B-2 |
| Claims-only tenant identity and ordinary auth semantics | Same test asserts unauthenticated 401; protected ticket without tenant plus spoofed X-Tenant-Id produces 403 |
| Update semantics and validation | Same test asserts own update 204 with zero-byte body, blank title 400, nonexistent lookup 404 |
| Real relational persistence | Same test starts its own SQL Server Testcontainer, creates schema explicitly in test setup, seeds via actual EF provider, and reads persisted final state through fresh HTTP request scopes |
| Scoped service lifetime and clean shutdown | `WorkerDisposesScopedDependencyAndStopsPromptly`: actual scoped disposable probe completes, worker StopAsync completes inside 2 seconds, ExecuteTask completes successfully |

## Generated artifacts

See `files.txt` for authored/generated configuration, source, and test files. Main application files:

- `app/src/TenantDocs/Program.cs`
- `app/src/TenantDocs/Documents.cs`
- `app/src/TenantDocs/MaintenanceWorker.cs`
- `app/tests/TenantDocs.Tests/DocumentsApiTests.cs`
- `app/Directory.Build.props`, `Directory.Packages.props`, `global.json`, `TenantDocs.slnx`

Raw final logs and Debug/Release TRX reports are retained in this directory. Test planning/review notes are under `testagent/`.

## Limits

This is a small integration sample, not a deployed identity service. No real external identity provider, login flow, schema migration deployment, production cookie key sharing, SQL schema upgrade, token revocation, rate limiting or network/load/concurrent-writer testing was executed. The sample proves tested request/query behavior; it is not an independent proof of every reference snippet, every EF bypass API, synchronous SaveChanges, raw SQL, or privileged offline database access. Worker maintenance is intentionally a harmless scoped log pass. No repo diff was made.

Formatter verification: `dotnet format TenantDocs.slnx --verify-no-changes --no-restore` initially failed in sandbox with named-pipe `SocketException (13): Permission denied` (`format.log`). Retried unchanged with approved escalation: exit 0, no diagnostics (`format-final.log`). No formatting changes were required.
