# API Engineering

Use this reference when a .NET application exposes or consumes HTTP APIs.

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

`TypedResults` is often preferable because the declared return type communicates the response contract, but `IResult`/`Results` remain valid when the surrounding API intentionally uses them or when the broader contract is clearer that way.

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

For applications with multiple endpoints per domain/resource, prefer route groups to organize routing, common prefixes, tags, and authorization policies cohesively:

```csharp
public interface IEndpointGroup
{
    void Map(RouteGroupBuilder group);
}

// Extension for clean Program.cs registration:
public static class EndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapGroup(
        this IEndpointRouteBuilder endpoints,
        string prefix,
        Action<RouteGroupBuilder> configure)
    {
        var group = endpoints.MapGroup(prefix);
        configure(group);
        return group;
    }
}
```

Example grouped feature:
```csharp
var users = app.MapGroup("/api/users")
    .WithTags("Users")
    .RequireAuthorization();

users.MapGet("/{id:guid}", GetUserById);
users.MapPost("/", CreateUser);
```

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
```

#### Exception Handler Implementation:
```csharp
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Status = exception switch
            {
                BadHttpRequestException => StatusCodes.Status400BadRequest,
                UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status500InternalServerError
            },
            Title = exception switch
            {
                BadHttpRequestException => "Bad Request",
                UnauthorizedAccessException => "Unauthorized",
                _ => "Internal Server Error"
            },
            Detail = httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred."
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
```

### Result-to-HTTP Status Code Mapping
If the application uses a Result/ErrorOr-style model, translate expected domain failures at the endpoint boundary into explicit typed HTTP results:

```csharp
return result.Match<Results<Ok<UserResponse>, NotFound, Conflict, ProblemHttpResult>>(
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

## API Versioning

Use `Asp.Versioning.Http` for Minimal API versioning. Define versions through route sets:

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
});

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

---

## Validation

Validate request shape and basic constraints at the boundary. Enforce business invariants in the application/domain layer.

Avoid duplicating identical rules across multiple layers. Each layer should validate the invariants it owns.

For .NET 10, evaluate built-in validation support before adding a validation dependency. If the project already uses FluentValidation or another established validator, follow that convention.

Never treat client-side validation as a security control.

## OpenAPI and Interactive Documentation

Keep generated documentation aligned with actual behavior. Document meaningful descriptions, parameters, request/response schemas, auth requirements, and error responses.

- **Built-in ASP.NET Core OpenAPI (`Microsoft.AspNetCore.OpenApi`):**
  In .NET 10 / ASP.NET Core 10, OpenAPI 3.1 is generated by the framework directly:
  ```csharp
  builder.Services.AddOpenApi();
  ```
  Never use `.WithOpenApi()`; it is deprecated in .NET 10 (`ASPDEPR002`). ASP.NET Core automatically discovers and documents Minimal API endpoints.

- **Scalar API Reference (`Scalar.AspNetCore`):**
  For developer-facing interactive documentation, prefer **Scalar** over legacy Swashbuckle:
  ```csharp
  if (app.Environment.IsDevelopment())
  {
      app.MapOpenApi();
      app.MapScalarApiReference(); // Accessible at /scalar/v1
  }
  ```

## Pagination, filtering, and sorting

For collections:

- bound result sizes
- define pagination semantics explicitly
- apply stable ordering before pagination
- validate page size and filters
- allow-list sortable/filterable fields
- avoid arbitrary database expressions from clients

Offset pagination is simple and often sufficient. Keyset/cursor pagination is worth considering for large or frequently changing datasets.

## Caching and state

### 1. Application / Data Caching: `HybridCache` (.NET 9+)
For in-memory and distributed data caching, prefer `Microsoft.Extensions.Caching.Hybrid` over raw `IDistributedCache` or `IMemoryCache`. It delivers a unified L1 (in-process) + L2 (distributed) cache with built-in stampede protection (coalesced concurrent requests):

```csharp
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});

// Consuming in endpoints/handlers:
public sealed class GetUserHandler(HybridCache cache, AppDbContext db)
{
    public async Task<UserResponse?> HandleAsync(Guid id, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            $"user-{id}",
            async token =>
            {
                var user = await db.Users.FindAsync([id], token);
                return user is null ? null : UserResponse.From(user);
            },
            tags: ["users"],
            cancellationToken: ct);
    }
}
```

### 2. HTTP Output Caching: `app.UseOutputCache()`
For caching rendered HTTP responses on the server side, prefer Output Caching over legacy `ResponseCaching`. Output Caching supports distributed backends, eviction policies, and route cache tags (`.CacheOutput(p => p.Tag("products"))`).

- **Minimal API Anti-Forgery**: Endpoints accepting HTML form posts should enforce anti-forgery using `.RequireAntiforgery()` or the `[ValidateAntiForgeryToken]` attribute.

## HTTP Resilience, Idempotency, and Concurrency

### HTTP Client Resilience (`Microsoft.Extensions.Http.Resilience`)
For outbound HTTP communication, use Microsoft's official `Microsoft.Extensions.Http.Resilience` package (MIT licensed):

```csharp
builder.Services.AddHttpClient<InventoryClient>(client =>
{
    client.BaseAddress = new Uri("https://inventory.service.local");
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddStandardResilienceHandler();
```

The standard resilience handler automatically configures a Polly v8 pipeline:
1. **Total Request Timeout** (overall call deadline)
2. **Standard Retry** (exponential backoff with jitter on transient 5xx/408/network errors)
3. **Circuit Breaker** (fails fast when failure rate exceeds threshold)
4. **Attempt Timeout** (deadline per HTTP attempt)
5. **Rate Limiter** (prevents overwhelming downstream services)

> [!NOTE]
> Consuming `Microsoft.Extensions.Http.Resilience` carries standard Microsoft MIT licensing. Avoid adding direct dependencies on `Polly` or `Polly.Core` packages, which enforce an Open Source Maintenance Fee (OSMF) EULA on commercial organizations earning $\ge \$20\text{k}$ revenue.

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
