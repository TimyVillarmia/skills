# Changelog

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
