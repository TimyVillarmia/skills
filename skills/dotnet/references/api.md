# API Engineering

Use this reference when a .NET application exposes or consumes HTTP APIs. For framework API details beyond the contracts and footguns below, use the **Live docs lookup** tools in `SKILL.md`.

## API shape & Minimal APIs

Preserve the API style already established in brownfield repos. Project EF persistence entities to explicit DTOs rather than exposing them directly when doing so couples the contract to storage concerns or risks accidental data exposure. Map HTTP status codes to externally observable HTTP semantics rather than internal exception type names.

For .NET 10 / C# 14 Minimal APIs:
- **`TypedResults` (`Results<Ok<T>, NotFound, ...>`):** Default for endpoints with 2–3 direct outcomes; the declared return type drives OpenAPI schema inference and direct delegate unit testing.
- **`Result` / `ErrorOr<T>` + `.ToIResult()`:** For domain-heavy endpoints with many expected failure modes (`NotFound`, `Validation`, `Conflict`, `Forbidden`), return `IResult` via a shared `.ToIResult()` mapper (calling `TypedResults.*` internally for consistent `ProblemDetails`) and attach explicit `.Produces<T>(...)` / `.ProducesProblem(...)` metadata.
- **Thin endpoint handlers:** Keep endpoint handlers thin and delegate business logic, queries, and authorization decisions to handlers/services.

### Endpoint and Route Group Organization

Organize endpoints into static classes with native `MapGroup` extension methods on `IEndpointRouteBuilder`, wired through a single `MapApiEndpoints()` hub so `Program.cs` stays one line, reflection scanning is avoided, and Native AOT + cascading authorization/filters work cleanly:

```csharp
// Endpoints/EndpointRegistration.cs
public static class EndpointRegistration
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api")
            .RequireAuthorization();

        api.MapUserEndpoints();
        return app;
    }
}

// Program.cs
app.MapApiEndpoints();
```

## ProblemDetails and Error Handling Pipeline

Use RFC 9457 `ProblemDetails` + `IExceptionHandler` + `UseStatusCodePages()` rather than catching unexpected exceptions in endpoints:

```csharp
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
        ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

app.UseExceptionHandler(); // Invokes registered IExceptionHandler chain
app.UseStatusCodePages(); // Emits ProblemDetails for non-exception 4xx/5xx status codes
```

In `GlobalExceptionHandler` (`IExceptionHandler`), inject `IHostEnvironment`: map known client exceptions (such as `BadHttpRequestException`) to their 4xx status with `LogWarning`, default unexpected exceptions to `500` with `LogError`, return detailed exception text only when `environment.IsDevelopment()`, and write via `IProblemDetailsService.TryWriteAsync(...)`.

> [!NOTE]
> **.NET 10 diagnostic suppression:** When `IExceptionHandler.TryHandleAsync` returns `true`, ASP.NET Core 10 suppresses framework unhandled-exception logs/metrics by default. Configure `app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = ctx => false })` if downstream telemetry or alerts rely on middleware diagnostics.

---

## Middleware Pipeline and Custom Middleware

Keep middleware registration in deliberate order:

```csharp
app.UseForwardedHeaders();    // 1. Resolve scheme/host/RemoteIpAddress from trusted proxies first
app.UseExceptionHandler();    // 2. Wrap downstream failures in ProblemDetails
app.UseStatusCodePages();
// 3. UseHsts() / UseHttpsRedirection() only when TLS terminates at Kestrel (skip inside HTTP-only containers)
app.UseRouting();             // 4. Explicit only if middleware runs before route matching (implicit in WebApplication)
app.UseRequestTimeouts();     // 5. After UseRouting so endpoint timeout metadata resolves
app.UseCors();                // 6. Before auth/rate-limiting/caching so preflight & error responses carry CORS headers
app.UseAuthentication();      // 7. Establish caller identity
app.UseRateLimiter();         // 8. After ForwardedHeaders + Authentication (per-user/IP); before Authorization
app.UseAuthorization();       // 9. Enforce policies
app.UseOutputCache();         // 10. After Cors + Auth/Authz
app.UseAntiforgery();         // 11. After Auth/Authz, before endpoints
// 12. Custom middleware, then endpoints (MapGroup / MapControllers)
```

- **Convention-based vs `IMiddleware`:** Convention-based middleware (`RequestDelegate next` in constructor) is instantiated as a **Singleton**—inject scoped dependencies into `InvokeAsync(HttpContext, ...)` rather than the constructor to avoid captive dependencies. Prefer factory-based **`IMiddleware`** (`builder.Services.AddScoped<T>()`) when scoped constructor injection is needed.
- **Route-scoped cross-cutting logic:** Prefer Minimal API **`IEndpointFilter`** over global middleware when behavior applies only to specific endpoints or `MapGroup`s.

---

## API Versioning

Use `Asp.Versioning.Http` + `Asp.Versioning.OpenApi` for Minimal API versioning and .NET 10 built-in OpenAPI integration (resolve latest stable versions via `dotnet package search Asp.Versioning.Http --exact-match`):

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
}).AddOpenApi();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();
}
```

Prefer additive-only DTO changes. When retiring an endpoint or version, emit RFC 9745 `Deprecation` (`@` + Unix epoch timestamp), RFC 8594 `Sunset` (`DateTimeOffset.ToString("r", CultureInfo.InvariantCulture)`), and a `Link` header (`rel="sunset"`).

---

## Validation

For .NET 10, enable built-in Minimal API source-generated DataAnnotations validation via `builder.Services.AddValidation()` (short-circuits invalid requests with `400 Bad Request` `ProblemDetails`; opt out per endpoint with `.DisableValidation()`). Preserve FluentValidation if the repo already uses it.

**`.NET 10` `AddValidation()` gotchas:**
- **Separate DTO assemblies:** Source generation only scans the assembly calling `AddValidation()`. DTOs in separate `Contracts`/`Application` projects silently skip validation unless marked with `[assembly: ValidatableType(typeof(...))]` or registered from that assembly.
- **Value types:** `[Required]` on non-nullable value types (`int`, `Guid`) still allows default `0` / `Guid.Empty`; use nullable `int?` + `[Required]`, `[Range]`, or `[JsonRequired]`.
- **JSON strictness & SSE (.NET 10):** Set `JsonSerializerOptions.AllowDuplicateProperties = false` on security-sensitive inputs to prevent JSON property smuggling, and use native `TypedResults.ServerSentEvents(...)` for SSE streams instead of third-party packages.

Enforce validation and authorization server-side; treat client-side validation as UX only.

## File uploads, streaming, and request size limits

`IFormFile` buffers payloads larger than 64 KB to memory or temp disk, risking memory or disk exhaustion on large uploads.

- **Small files only (`IFormFile`):** Acceptable for small bounded payloads when explicit size limits are enforced.
- **Large files:** Stream multipart payloads directly via `MultipartReader` (`Microsoft.AspNetCore.WebUtilities`) without buffering the whole file, or offload uploads to object storage using short-lived presigned URLs.
- **Minimal API antiforgery trap:** In ASP.NET Core 8+, Minimal API endpoints binding `IFormFile` or `IFormFileCollection` automatically enforce antiforgery validation and reject stateless Bearer-token requests with `400 Bad Request` unless `.DisableAntiforgery()` is chained.
- **Co-located request size limits:** Enforce tight bounds at Kestrel (`options.Limits.MaxRequestBodySize`), endpoint (`.WithMetadata(new RequestSizeLimitAttribute(...))` / `[RequestSizeLimit(...)]`), and form (`FormOptions.MultipartBodyLengthLimit`) levels.
- **Untrusted upload hygiene:** Generate storage keys with `Path.GetRandomFileName()` and validate allow-listed extensions plus file magic bytes instead of trusting `FileName` or `ContentType`.

## OpenAPI and Interactive Documentation

- **Built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`):** Use `builder.Services.AddOpenApi()` and `app.MapOpenApi()` (not deprecated `.WithOpenApi()` / `ASPDEPR002`).
- **Untyped `IResult` endpoints and OpenAPI schemas:** When an endpoint returns `IResult` (instead of `Results<...>` / `TypedResults`), `Microsoft.AspNetCore.OpenApi` cannot infer response schemas and emits empty/`void` responses in `/openapi/v1.json`. Always attach explicit `.Produces<T>(statusCode)`, `.Produces(statusCode, contentType: "text/csv")`, or `.ProducesProblem(statusCode)` metadata so generated clients (Orval, Kiota, NSwag) receive accurate types.
- **Scalar (`Scalar.AspNetCore`):** Prefer Scalar over legacy Swashbuckle in Development:
  ```csharp
  if (app.Environment.IsDevelopment())
  {
      app.MapOpenApi();
      app.MapScalarApiReference(options => options.ProxyUrl = null); // Accessible at /scalar/v1
  }
  ```
  - **Disable external proxy:** Scalar's "Try It" client routes requests through `proxy.scalar.com` by default; set `options.ProxyUrl = null` so local URLs and `Authorization` headers never leave the machine.
  - **Security schemes:** Define Bearer/OAuth2 security schemes in `AddOpenApi(options => options.AddDocumentTransformer(...))` rather than Scalar options so Scalar picks them up from the OpenAPI document automatically.

## Pagination, caching, and anti-forgery

- **Pagination:** Bound collection page sizes, apply stable ordering before pagination, allow-list sortable/filterable fields (never bind arbitrary client expressions), and prefer keyset/cursor pagination for large or frequently changing datasets.
- **Caching:** For `HybridCache`, L1/L2 coherence, and `UseOutputCache`, see [Caching](caching.md).
- **Minimal API Anti-Forgery (CSRF):** Call `AddAntiforgery()` and `app.UseAntiforgery()` between `UseAuthentication()`/`UseAuthorization()` and endpoints. `[FromForm]` bindings validate tokens automatically; manual `context.Request.ReadFormAsync()` does **not**—inject `IAntiforgery` and call `await antiforgery.ValidateRequestAsync(context)`. Use `.DisableAntiforgery()` for external webhooks/M2M form posts; do not use MVC `[ValidateAntiForgeryToken]` or non-existent `.RequireAntiforgery()`.

## HTTP Resilience, Idempotency, and Concurrency

For outbound HTTP client resilience, non-HTTP `ResiliencePipeline` policies, timeout budgets, and `Retry-After` handling, see [Resilience & Fault Tolerance](resilience.md).

### Idempotency & Concurrency

- Require an `Idempotency-Key` header for retryable operations that are not naturally idempotent, and do not add blind retries to non-idempotent endpoints.
- Define explicit conflict behavior for concurrent updates (typically `409 Conflict` via optimistic concurrency) rather than silently overwriting newer state.
