# API Engineering

Use this reference when a .NET application exposes or consumes HTTP APIs.

## Contents

- [Official references](#official-references)
- [API shape](#api-shape)
- [Minimal APIs and controllers](#minimal-apis-and-controllers)
- [HTTP semantics](#http-semantics)
- [ProblemDetails and Error Handling Pipeline](#problemdetails-and-error-handling-pipeline)
- [Middleware Pipeline and Custom Middleware](#middleware-pipeline-and-custom-middleware)
- [API Versioning](#api-versioning)
- [Validation](#validation)
- [File uploads, streaming, and request size limits](#file-uploads-streaming-and-request-size-limits)
- [OpenAPI and Interactive Documentation](#openapi-and-interactive-documentation)
- [Pagination, filtering, and sorting](#pagination-filtering-and-sorting)
- [Caching and anti-forgery](#caching-and-anti-forgery)
- [HTTP Resilience, Idempotency, and Concurrency](#http-resilience-idempotency-and-concurrency)
- [API anti-patterns](#api-anti-patterns)
- [API checklist](#api-checklist)

## Official references

Use version-matched Microsoft documentation as the primary implementation reference:

- ASP.NET Core: https://learn.microsoft.com/aspnet/core/
- HTTP requests: https://learn.microsoft.com/aspnet/core/fundamentals/http-requests
- ASP.NET Core OpenAPI: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi
- .NET: https://learn.microsoft.com/dotnet/
- .NET API browser: https://learn.microsoft.com/dotnet/api/
- OpenAPI specification: https://spec.openapis.org/oas/latest.html

Prefer official Microsoft documentation for runtime/framework behavior and the OpenAPI Initiative specification for contract semantics. Inspect the actual target framework and package versions before relying on version-sensitive behavior.

## API shape

Treat the API as a public contract, even when the immediate consumer is an internal frontend.

Design deliberately:

- resource and operation names
- HTTP methods and status codes
- request/response DTOs
- validation and error behavior
- authentication/authorization
- pagination/filtering/sorting
- idempotency and concurrency
- OpenAPI documentation

Do not expose persistence entities directly when doing so couples the contract to storage concerns or risks accidental data exposure.

## Minimal APIs and controllers

Use the style already established by the project. For new minimal APIs, prefer endpoint handlers that are thin and delegate application behavior to a handler/service/use case.


For .NET 10/C# 14, use modern APIs such as `TypedResults`, endpoint filters, built-in validation facilities, `ProblemDetails`, and built-in OpenAPI support where they fit the project.

`TypedResults` (`Results<Ok<T>, NotFound, ...>`) is the default for endpoints with 2–3 direct outcomes because the declared return type drives OpenAPI schema inference and enables direct delegate unit testing. When a domain-oriented codebase uses a `Result`/`ErrorOr<T>` pattern with many expected failure modes (`NotFound`, `Validation`, `Conflict`, `Forbidden`), `IResult` via a shared `.ToIResult()` mapper + `.Produces<T>(...)`/`.ProducesProblem(...)` is a clean alternative that avoids verbose multi-type `Results<...>` unions on every endpoint (return `TypedResults.*` inside `.ToIResult()` for consistent `ProblemDetails` behavior).

Good:

```csharp
app.MapGet("/users/{id}", async Task<Results<Ok<UserDto>, NotFound>> (
    Guid id,
    IUserQueries queries,
    CancellationToken cancellationToken) =>
{
    var user = await queries.GetAsync(id, cancellationToken);
    return user is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(user);
});
```

Avoid mixing substantial business logic, persistence queries, and authorization decisions into a large endpoint lambda.

### Endpoint and Route Group Organization

For applications with multiple endpoints per domain/resource, organize endpoints into static classes with native `MapGroup` extension methods on `IEndpointRouteBuilder`. This groups routing, common prefixes, tags, and authorization policies cohesively without reflection overhead:

```csharp
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users")
            .WithTags("Users");

        group.MapGet("/{id:guid}", GetUserById);
        group.MapPost("/", CreateUser);

        return group;
    }

    private static async Task<Results<Ok<UserDto>, NotFound>> GetUserById(
        Guid id,
        IUserQueries queries,
        CancellationToken ct)
    {
        var user = await queries.GetAsync(id, ct);
        return user is not null ? TypedResults.Ok(user) : TypedResults.NotFound();
    }
}
```

Wire feature groups through a single `MapApiEndpoints()` hub so `Program.cs` stays one line and shared prefixes, authorization, and filters live in one place:

```csharp
// Endpoints/EndpointRegistration.cs
public static class EndpointRegistration
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api")
            .RequireAuthorization();

        api.MapUserEndpoints();
        // Register additional feature groups here

        return app;
    }
}
```

In `Program.cs`:
```csharp
app.MapApiEndpoints();
```

This pattern avoids reflection scanning, is Native AOT-friendly, and cascades authorization, filters, and OpenAPI metadata across the group.

## HTTP semantics

Use semantics based on externally observable behavior:

- `GET` retrieves and should not mutate state.
- `POST` creates or performs a non-idempotent operation where appropriate.
- `PUT` replaces a representation and is idempotent when the contract defines it that way.
- `PATCH` partially updates according to explicit patch semantics.
- `DELETE` removes or logically removes according to the contract.

Typical status codes:

- `200 OK` — successful representation.
- `201 Created` — resource created; provide a location when meaningful.
- `202 Accepted` — asynchronous work accepted.
- `204 No Content` — success with no body.
- `400 Bad Request` — malformed/invalid request semantics.
- `401 Unauthorized` — authentication required or invalid.
- `403 Forbidden` — authenticated caller is not permitted.
- `404 Not Found` — resource absent or intentionally undiscoverable.
- `409 Conflict` — state or concurrency conflict.
- `422 Unprocessable Content` — only when the contract deliberately distinguishes semantic validation.
- `429 Too Many Requests` — rate limit exceeded.
- `500 Internal Server Error` — unexpected server failure.

Do not select a status code merely because an internal exception or framework type has a particular name.

## ProblemDetails and Error Handling Pipeline

For HTTP APIs, use RFC 9457 / RFC 7807 `ProblemDetails` as the uniform error representation.

### Centralized Exception Handling with `IExceptionHandler`
Configure centralized exception handling rather than catching unexpected exceptions in endpoints:

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

#### Exception Handler Implementation:
```csharp
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            BadHttpRequestException badRequest => badRequest.StatusCode,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode >= 500)
        {
            logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);
        }
        else
        {
            logger.LogWarning(exception, "Request failed with status {StatusCode}: {Message}", statusCode, exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(statusCode) is { Length: > 0 } phrase
                ? phrase
                : "Internal Server Error",
            Detail = environment.IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred."
        };

        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
```

> [!NOTE]
> **.NET 10 diagnostic suppression:** When `IExceptionHandler.TryHandleAsync` returns `true`, ASP.NET Core 10 suppresses framework unhandled-exception logs/metrics by default. Configure `app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = ctx => false })` if downstream telemetry or alerts rely on middleware diagnostics.

### Result-to-HTTP Status Code Mapping
If the application uses a Result/ErrorOr-style model (such as `ErrorOr`), translate expected domain failures at the endpoint boundary into explicit typed HTTP results:

```csharp
return result.MatchFirst<Results<Ok<UserResponse>, NotFound, Conflict, ProblemHttpResult>>(
    success => TypedResults.Ok(UserResponse.From(success)),
    error => error.Type switch
    {
        ErrorType.NotFound => TypedResults.NotFound(),
        ErrorType.Conflict => TypedResults.Conflict(),
        _ => TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            detail: error.Description,
            title: error.Code)
    });
```

---

## Middleware Pipeline and Custom Middleware

Keep middleware registration in deliberate order so proxy headers, timeouts, CORS, authentication, rate limiting, caching, and antiforgery compose predictably:

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

- **Convention-based vs `IMiddleware`:** Convention-based middleware (`RequestDelegate next` in constructor) is instantiated as a **Singleton**—inject scoped dependencies into `InvokeAsync(HttpContext, ...)` rather than the constructor to avoid captive dependencies. Prefer factory-based **`IMiddleware`** (registered via `builder.Services.AddScoped<T>()`) when middleware needs scoped constructor injection or strict DI activation.
- **Route-scoped cross-cutting logic:** Prefer Minimal API **`IEndpointFilter`** over global middleware when behavior applies only to specific endpoints or `MapGroup`s.

---

## API Versioning

Use `Asp.Versioning.Http` paired with `Asp.Versioning.OpenApi` for Minimal API versioning and .NET 10 built-in OpenAPI integration (resolve the latest stable versions via `dotnet package search Asp.Versioning.Http --exact-match`):

```xml
<PackageReference Include="Asp.Versioning.Http" />
<PackageReference Include="Asp.Versioning.OpenApi" />
```

Define versions through route version sets and expose per-version OpenAPI documents:

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

var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .ReportApiVersions()
    .Build();

var orders = app.MapGroup("/api/v{version:apiVersion}/orders")
    .WithApiVersionSet(versionSet);

orders.MapGet("/", GetOrdersV1).MapToApiVersion(1, 0);
orders.MapGet("/", GetOrdersV2).MapToApiVersion(2, 0);
```

### Backward-compatible evolution and deprecation headers

Prefer additive-only DTO changes (new optional properties, new endpoints/versions). Never remove, rename, or repurpose the semantics of an existing field in a released version.

When retiring an endpoint or version, emit both RFC 9745 `Deprecation` and RFC 8594 `Sunset` response headers alongside a `Link` header pointing to migration docs:

```http
Deprecation: @1780272000
Sunset: Thu, 31 Dec 2026 23:59:59 GMT
Link: <https://api.example.com/docs/v2-migration>; rel="sunset"
```

- `Deprecation` (RFC 9745) uses an HTTP Structured Fields Date (`@` followed by a Unix epoch integer timestamp).
- `Sunset` (RFC 8594) uses an HTTP IMF-fixdate in GMT (`DateTimeOffset.ToString("r", CultureInfo.InvariantCulture)`).

---

## Validation

Validate request shape and basic constraints at the boundary. Enforce business invariants in the application/domain layer.

Avoid duplicating identical rules across multiple layers. Each layer should validate the invariants it owns.

For .NET 10, enable built-in Minimal API source-generated DataAnnotations validation via `builder.Services.AddValidation()` (short-circuits invalid requests with `400 Bad Request` `ProblemDetails`; opt out per endpoint with `.DisableValidation()`). If the project already uses FluentValidation or another established validator, follow that convention.

```csharp
builder.Services.AddValidation();
app.MapPost("/products", (CreateProductRequest req) => TypedResults.Ok(req));
app.MapPost("/imports/raw", (RawPayload req) => TypedResults.Ok()).DisableValidation();
```

**`AddValidation()` gotchas (.NET 10):**
- **Separate DTO assemblies:** Source generation only scans the assembly calling `AddValidation()`. DTOs in separate `Contracts`/`Application` projects silently skip validation unless marked with `[assembly: ValidatableType(typeof(...))]` or registered from that assembly.
- **Value types:** `[Required]` on non-nullable value types (`int`, `Guid`) still allows default `0` / `Guid.Empty`; use nullable `int?` + `[Required]`, `[Range]`, or `[JsonRequired]`.
- **JSON strictness & SSE (.NET 10):** Set `JsonSerializerOptions.AllowDuplicateProperties = false` on security-sensitive inputs to prevent JSON property smuggling, and use native `TypedResults.ServerSentEvents(...)` for SSE streams instead of third-party packages.

Never treat client-side validation as a security control.

## File uploads, streaming, and request size limits

`IFormFile` buffers payloads larger than 64 KB to memory or temp disk, risking memory or disk exhaustion on large uploads.

- **Small files only (`IFormFile`):** Acceptable for small bounded payloads when explicit size limits are enforced.
- **Large files:** Stream multipart payloads directly via `MultipartReader` (`Microsoft.AspNetCore.WebUtilities`) without buffering the whole file, or offload uploads to object storage using short-lived presigned URLs.
- **Minimal API antiforgery trap:** In ASP.NET Core 8+, Minimal API endpoints binding `IFormFile` or `IFormFileCollection` automatically enforce antiforgery validation and reject stateless Bearer-token requests with `400 Bad Request` unless `.DisableAntiforgery()` is chained (see [Minimal API Anti-Forgery](#minimal-api-anti-forgery-csrf)).
- **Co-located request size limits:** Enforce tight bounds at Kestrel, endpoint, and form levels:
  - Kestrel global limit: `options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;`
  - Endpoint override: `.WithMetadata(new RequestSizeLimitAttribute(50 * 1024 * 1024))` or `[RequestSizeLimit(...)]`
  - Multipart form limit: `builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);`
- **Untrusted upload hygiene:** Never trust `FileName` (generate storage keys with `Path.GetRandomFileName()`) or `ContentType`. Validate allow-listed extensions and inspect file magic bytes before processing.

## OpenAPI and Interactive Documentation

Keep generated documentation aligned with actual behavior. Document meaningful descriptions, parameters, request/response schemas, auth requirements, and error responses.

- **Built-in ASP.NET Core OpenAPI (`Microsoft.AspNetCore.OpenApi`):**
  In .NET 10 / ASP.NET Core 10, OpenAPI 3.1 is generated by the framework directly:
  ```csharp
  builder.Services.AddOpenApi();
  ```
  Never use `.WithOpenApi()`; it is deprecated in .NET 10 (`ASPDEPR002`). ASP.NET Core automatically discovers and documents Minimal API endpoints.

- **Untyped `IResult` endpoints and OpenAPI schemas:**
  When an endpoint returns `IResult` (instead of `Results<...>` / `TypedResults`)—for example, when replaying raw idempotent JSON or returning mixed JSON and `text/csv` responses—`Microsoft.AspNetCore.OpenApi` cannot infer response schemas and emits empty/`void` responses in `/openapi/v1.json`. Always attach explicit `.Produces<T>(statusCode)`, `.Produces(statusCode, contentType: "text/csv")`, or `.ProducesProblem(statusCode)` metadata so generated clients (Orval, Kiota, NSwag) receive accurate types.

- **Scalar API Reference (`Scalar.AspNetCore`):**
  For developer-facing interactive documentation, prefer **Scalar** over legacy Swashbuckle:
  ```csharp
  if (app.Environment.IsDevelopment())
  {
      app.MapOpenApi();
      app.MapScalarApiReference(options => options.ProxyUrl = null); // Accessible at /scalar/v1
  }
  ```
  - **Disable external proxy for internal APIs:** Scalar's "Try It" client routes requests through `proxy.scalar.com` by default; set `options.ProxyUrl = null` so local URLs and `Authorization` headers never leave the machine.
  - **Security schemes:** Define Bearer/OAuth2 security schemes in `AddOpenApi(options => options.AddDocumentTransformer(...))` rather than in Scalar options—Scalar picks them up from the generated OpenAPI document automatically.

## Pagination, filtering, and sorting

For collections:

- bound result sizes
- define pagination semantics explicitly
- apply stable ordering before pagination
- validate page size and filters
- allow-list sortable/filterable fields
- avoid arbitrary database expressions from clients

Offset pagination is simple and often sufficient. Keyset/cursor pagination is worth considering for large or frequently changing datasets.

## Caching and anti-forgery

For application data caching (`HybridCache`), L1/L2 coherence, and HTTP Output Caching (`UseOutputCache`), see [Caching](caching.md).

### Minimal API Anti-Forgery (CSRF)
In ASP.NET Core Minimal APIs, anti-forgery is enforced via the native antiforgery middleware:

```csharp
builder.Services.AddAntiforgery();
// ...
app.UseAntiforgery(); // Must be placed between UseAuthentication/UseAuthorization and endpoints
```

- **Native Form Binding (`[FromForm]`):** Endpoints that accept form parameters via `[FromForm]` automatically validate the antiforgery token. Tokenless or invalid requests are rejected with `400 Bad Request`.
- **Manual Form Parsing:** If an endpoint reads `context.Request.ReadFormAsync()` directly without `[FromForm]`, automatic token validation does **not** occur. Inject `IAntiforgery antiforgery` into the endpoint handler and call `await antiforgery.ValidateRequestAsync(context)`.
- **Opt-out:** Call `.DisableAntiforgery()` on specific endpoints that accept external webhooks or machine-to-machine form posts.
- **Do not use MVC attributes:** Attributes such as `[ValidateAntiForgeryToken]` or non-existent methods like `.RequireAntiforgery()` do not protect Minimal APIs.

## HTTP Resilience, Idempotency, and Concurrency

For outbound HTTP client resilience, non-HTTP `ResiliencePipeline` policies, timeout budgets, and `Retry-After` handling, see [Resilience & Fault Tolerance](resilience.md).

### Idempotency & Concurrency
- For retryable operations, determine whether the operation is naturally idempotent. If not, require an idempotency key (`Idempotency-Key` header).
- For concurrent updates, define conflict behavior rather than silently overwriting newer state. Optimistic concurrency commonly maps to `409 Conflict`.
- Do not add retries blindly to non-idempotent endpoints.

## API anti-patterns

❌ Returning EF entities directly from public endpoints.

❌ A single endpoint that performs validation, business rules, database queries, mapping, and external calls inline.

❌ Unbounded collection endpoints.

❌ Trusting client-supplied role/tenant identifiers for authorization.

❌ Catching `Exception` in every endpoint.

❌ Creating `new HttpClient()` repeatedly for application requests.

❌ Documenting an OpenAPI contract that differs from the actual endpoint behavior.

## API checklist

- Target runtime and package versions are known.
- Contract and DTOs are intentional.
- HTTP semantics and status codes are correct.
- Validation and errors are consistent.
- Authorization is enforced at the resource boundary.
- Collections are bounded and deterministically ordered.
- Retries/idempotency/concurrency are understood.
- OpenAPI matches implementation and consumer compatibility.
- Responses do not expose internal or sensitive data.
- Tests verify externally observable behavior.
