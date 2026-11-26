# Changelog

## [1.4.3] - 2026-11-26

### Added
- `dotnet` (`SKILL.md`): Added `Live docs lookup` section routing version-specific `.NET 10+` / `C# 14` / `EF Core` / `ASP.NET Core` / Azure API queries to `microsoft-learn` MCP (`microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch`) with `npx @microsoft/learn-cli` (`mslearn`) fallback.

### Changed
- `README.md`: Updated `dotnet` reference guide count to `18`, added missing `authorization.md` table entry, and documented optional companion tools (Microsoft Learn MCP / `mslearn` CLI and Aspire).
- `dotnet` (`references/csharp.md`, `references/configuration.md`, `references/hosted-services.md`, `references/api.md`): Pruned generic textbook prose, static URL lists, and redundant boilerplate while preserving all `.NET 10` / `C# 14` guardrails, footguns, and cross-file anchors.

## [1.4.2] - 2026-11-25

### Changed
- `dotnet-scaffold` (v1.3):
  - `scripts/scaffold.cs`: Allowed scaffolding into directories containing VCS/repo metadata (`.git`, `.github`, `.gitignore`, `.gitattributes`, `README.md`, `LICENSE`, `AGENTS.md`, `CLAUDE.md`, `mise.toml`), preserved existing `.gitignore`, and reported blocking project files clearly when present.
  - `assets/Directory.Build.props`: Scoped `.NET` C# properties with `Condition="'$(MSBuildProjectExtension)' == '.csproj'"` so polyglot Aspire/React `.esproj` solutions build cleanly.
  - `assets/.editorconfig`: Suppressed `CA1861` on EF Core CLI-generated `**/Migrations/*.cs` files for Release builds.
  - `SKILL.md`: Added 3-way greenfield routing (backend-first `scaffold.cs` + optional `aspire init`, Aspire-native `aspire new` + **Harden Aspire Solution** checklist, and brownfield existing `.sln`/`.slnx`).

### Fixed
- `dotnet` (`references/authorization.md`, `references/testing.md`): Added `CA1711` suppression on `RolePermission` for `TreatWarningsAsErrors` Release builds and documented running `dotnet test --project` from the repo root containing `global.json` for MTP.

## [1.4.1] - 2026-11-25

### Added
- `dotnet` (`references/authorization.md`): Split authorization out of `security.md` and added the Admin Role Management API contract (`GET /api/permissions`, `/api/roles`, `/api/users/{userId}/roles`), `PermissionDefinition` catalog metadata, 6 invariants (no privilege escalation, system role protection, last-admin lockout prevention, catalog validation, atomic `HybridCache` tag/key invalidation, audit logging), and Role Management regression test cases.

### Changed
- `dotnet` (`SKILL.md`, `references/security.md`, `references/review.md`), `dotnet-scaffold`, `fastendpoints`: Routed authorization and BOLA/IDOR references to `references/authorization.md`.

## [1.4.0] - 2026-11-25

### Added
- `dotnet` (`SKILL.md`, `references/security.md`): Standardized greenfield authorization on tenant-scoped, permission-based RBAC (`<resource>:<action>` grammar, scoped `Role`/`RolePermission`/`RoleAssignment` schema, dynamic `PermissionPolicyProvider` + `PermissionAuthorizationHandler` + `.RequirePermission(...)`, `HybridCache` permission caching, ReBAC/PBAC escalation criteria, and 4-case AuthZ test matrix).

### Changed
- `dotnet-scaffold` (`SKILL.md`): Aligned post-scaffold auth wiring to permission-based RBAC via `RequirePermission(...)`.
- `fastendpoints` (`SKILL.md`, `references/security.md`): Aligned endpoint authorization to lowercase `<resource>:<action>` permissions via `PermissionPolicyProvider` (`Policies($"perm:...")`), discouraged hard-coded `Roles(...)` in greenfield endpoints, and added `IAuthorizationService` resource-ownership guidance.

## [1.3.1] - 2026-11-19

### Changed
- `SKILL.md` & `api.md`: Clarified `TypedResults` (`Results<...>`) for 2–3 direct outcomes vs. `Result`/`ErrorOr` + `.ToIResult()` + `.Produces(...)` for domain-heavy endpoints, and documented the `MapApiEndpoints()` hub pattern to keep `Program.cs` to one line with `MapGroup`.

## [1.3.0] - 2026-11-19

### Added
- `dotnet-scaffold` (v1.1): Greenfield Intake step before `scripts/scaffold.cs` — always asks for auth/IdP (no assumed default), recommends + confirms database (PostgreSQL 18), architecture (Vertical Slice), and extras (Testcontainers, outbox, Aspire); stops and reports when headless.
- `scaffold.cs`: `--database postgres|sqlserver|sqlite|none` selects `Testcontainers.PostgreSql` or `Testcontainers.MsSql` (none for `sqlite`/`none`) and rejects unknown values.

### Changed
- `dotnet` (v1.3): Greenfield defaults now require asking for auth first, and add `AddProblemDetails()` + `IExceptionHandler`, OpenTelemetry + `[LoggerMessage]` + health checks, `JsonSerializerContext`, `AddRequestTimeouts()`, `global.json` SDK pin, and Native EF Core Outbox / `WolverineFx`; `TreatWarningsAsErrors` wording aligned with the Release-only scaffold asset.

### Fixed
- `dotnet-scaffold/assets/Directory.Packages.props`: Pinned `Microsoft.OpenApi` to `2.13.1` (transitive pinning) to resolve `NU1903` (GHSA-v5pm-xwqc-g5wc) in Release builds.

## [1.2.1] - 2026-11-18

### Added
- `api.md`: Documented `.Produces<T>()` / `.Produces(..., contentType)` / `.ProducesProblem()` requirement on untyped `IResult` endpoints so `Microsoft.AspNetCore.OpenApi` emits schemas for generated clients.
- `security.md`: Added `Outbound webhooks` guidance covering `{timestamp}.{payload}` HMAC-SHA256 signing headers and `EndpointUrl` validation beyond `[Url]` (rejecting `#` fragments, enforcing `https`, and blocking private/loopback IPs).

## [1.2.0] - 2026-11-18

### Added
- `scripts/validate_skills.py`, `.github/workflows/validate.yml`, and `mise.toml`: Automated skill validation for frontmatter, line/token budgets, anchor links, and formatting across all skills.
- `SKILL.md`: Greenfield defaults for `.slnx`, `Directory.Build.props` (`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`), Central Package Management (`Directory.Packages.props`), PostgreSQL 18, `global.json` (Microsoft.Testing.Platform), NuGet version resolution / `NU19xx` audit hygiene, and "default + 1-line why + ask" guidance.
- `efcore.md` & `database-migrations.md`: Guardrail requiring `dotnet ef` CLI commands for migrations and compiled models instead of hand-editing generated files (`*ModelSnapshot.cs`, `*.Designer.cs`).
- `review.md`: Concrete static security checks (`dotnet package list --vulnerable --include-transitive`, `FromSqlRaw` interpolation, `BinaryFormatter`/weak hashes, wildcard CORS, unmarked endpoints), dev-fixture noise filtering, and one-at-a-time major NuGet upgrade guidance (`dotnet package list --outdated`).

### Changed
- `dotnet` skill (v1.2): Pruned `SKILL.md` to ~101 lines with a `DO NOT USE FOR` Aspire boundary, deduplicated overlapping prose across references, and added `## Contents` TOCs to long references.

## [1.1.2] - 2026-11-17

### Fixed
- `caching.md`: Replaced captured request-scoped `AppDbContext` in `GetUserHandler` with `IDbContextFactory<AppDbContext>` to prevent `ObjectDisposedException` on coalesced `HybridCache` cancellation; switched single-key eviction from `RemoveByTagAsync` to `RemoveAsync`.
- `testing.md`: Applied `MigrateAsync()` before `Respawner.CreateAsync` and inside `CustomApiFactory.InitializeAsync`, changed `public new async ValueTask DisposeAsync()` to `override`, and added `Contract testing for API evolution` to `## Contents`.
- `observability.md` & `security.md`: Added `.AllowAnonymous()` to `/healthz/live` and `/healthz/ready` probes, replaced reflection `JsonSerializer.SerializeAsync` in `WriteJsonResponse` with an AOT-safe string write, replaced culture-sensitive `{TotalAmount:C}` in `[LoggerMessage]` with `{TotalAmount} {Currency}`, updated `KnownNetworks` to `KnownIPNetworks` (.NET 10+ / `ASPDEPR005`), and ordered `UseRateLimiter()` before `UseAuthorization()`.
- `resilience.md`: Called `DisableForUnsafeHttpMethods()` after assigning `ShouldHandle` so custom `Retry-After` checks do not re-enable unsafe HTTP method retries, and removed the redundant `DelayGenerator` block.
- `messaging.md`: Replaced invalid `Version="[8.3.*,9.0.0)"` with `Version="8.3.6"` / `Version="[8.3.0,9.0.0)"`, moved `SaveChangesAsync` outside the per-message `foreach` loop, and added a composite `[Index]` on `OutboxMessage`.
- `database-migrations.md`: Aligned Phase 2 mapped property names (`FullName`, `DisplayName`) with Section 4's `ExecuteUpdateAsync` backfill and removed the $O(N^2)$ `.OrderBy(u => u.Id)` on the batch loop.
- `concurrency.md`: Executed `pg_try_advisory_xact_lock` on `AppDbContext`'s transaction instead of an isolated connection, and changed `[Timestamp]` properties from `init` to `set`.
- `multi-tenancy.md`: Documented `UseAuthentication()` -> `TenantResolutionMiddleware` -> `UseAuthorization()` order, guarded `OriginalValue` in `TenantSaveChangesInterceptor`, and noted EF Core 8/9's 1-argument `HasQueryFilter` overload.
- `api.md`, `configuration.md`, `hosted-services.md`, `SKILL.md`: Injected `IHostEnvironment` in `GlobalExceptionHandler` with 4xx `LogWarning` and `app.UseStatusCodePages()`, clarified `ErrorOr` and `.DisableValidation()` route, noted `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` and `AddOptionsWithValidateOnStart`, passed `TimeProvider` to `PeriodicTimer`, and added `TypedResults`, `AddValidation()`, and `[OptionsValidator]` to `SKILL.md` greenfield defaults.

## [1.1.1] - 2026-11-17

### Fixed
- `resilience.md`: Fixed `CS0150` compile error in `Retry-After` delay pattern, removed conflicting `HttpClient.Timeout` so `AddStandardResilienceHandler` owns the timeout budget, and noted `ShouldHandle` fail-fast when `Retry-After` exceeds the caller SLA.
- `security.md`: Captured `MapGroup("/api").RequireRateLimiting(...)` and mapped an endpoint onto the group so the rate limit applies.
- `testing.md`: Added `UseMicrosoftTestingPlatformRunner` and `xunit.v3.mtp-v2` to the xUnit v3 project snippet to match the native MTP CLI examples.
- `api.md`: Preserved `BadHttpRequestException.StatusCode` in `GlobalExceptionHandler` (avoiding `413` to `400` rewrites), set `Deprecation` timestamp before `Sunset`, replaced `options.WithProxy(null)` (`CS8625`) with `options.ProxyUrl = null`, and qualified `MapGroup` AOT wording.
- `observability.md`: Removed high-cardinality `customer.id` tag from `OrdersCreatedCounter.Add(1)`.
- `multi-tenancy.md`: Replaced `IConfiguration` injection in `TenantConnectionFactory` with `IOptionsMonitor<TenantDatabaseOptions>` and added `## Contents` TOC.
- `messaging.md`: Added `.NET 9+` label on `Guid.CreateVersion7()`, retry-cap note, and 30-second backoff lease on failed outbox publish.
- `efcore.md`, `caching.md`, `hosted-services.md`, `database-migrations.md`, `csharp.md`, `SKILL.md`: Qualified SQL Server `uniqueidentifier` vs UUIDv7 locality, `HybridCache` scoped `DbContext` lifetime on cancellation, `Channel<T>` volatile scope, provider-neutral `AddColumn<string>`, `ONLINE = ON` support caveat, and `(C# 12+)` section labels.

## [1.1.0] - 2026-11-17

### Added
- `dotnet` skill (v1.1):
  - Explicit `.NET 10 / C# 14` greenfield target version guidance and `< net10.0` EOL awareness (Nov 10, 2026).
  - Three new domain references: `configuration.md` (options validation, lifetimes, User Secrets, `__` env binding, feature flags), `caching.md` (`HybridCache` L1/L2 coherence, stampede protection, Output Caching), and `resilience.md` (HTTP & non-HTTP `ResiliencePipeline`, timeout budgets, `Retry-After`).
  - `api.md`: `MultipartReader` file upload streaming, co-located request size limits, `IFormFile` antiforgery guidance, RFC 9745 `Deprecation` / RFC 8594 `Sunset` headers, and Scalar `.WithProxy(null)` + `AddDocumentTransformer` notes.
  - `security.md`: Identity/passkeys, `JsonWebTokenHandler`, `AddRateLimiter` + `UseForwardedHeaders()` guidance, CORS tightening, and raw-body HMAC webhook verification.
  - `efcore.md`, `csharp.md`, `observability.md`, `testing.md`: `Guid.CreateVersion7()`, `SaveChangesInterceptor` audit fields, dual-provider filtered unique indexes, `CultureInfo.InvariantCulture`, non-root port `8080` / `PublishContainer` defaults, and OpenAPI contract testing.
- Official Aspire skill integration note in `README.md` and `SKILL.md`.

### Changed
- Consolidated options, caching, and HTTP resilience out of `architecture.md`, `api.md`, and `performance.md` into dedicated reference guides with zero duplication.
- Removed stale cross-skill references (`fastendpoints`, `run-tests`) from `dotnet` so it remains standalone.

## [1.0.0] - 2026-11-16

### Added
- `dotnet` skill (v1.0) with 14 domain reference guides for .NET 10 LTS (C#, APIs, architecture, concurrency, database migrations, EF Core, hosted services, messaging, multi-tenancy, observability, performance, code review, security, and testing).
- `fastendpoints` skill (v1.0) covering REPR endpoints, FluentValidation, security, in-process messaging, and integration testing.
- `dotnet-scaffold` skill (v1.0) with `scripts/scaffold.cs` and project templates (`Directory.Build.props`, `Directory.Packages.props`, `global.json`, `TestProject.csproj`, and `SliceTemplate`).
- Repository setup with `README.md`, MIT `LICENSE`, and `.gitignore`.
