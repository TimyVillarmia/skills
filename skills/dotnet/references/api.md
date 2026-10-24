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

## ProblemDetails and errors

For HTTP APIs, use a consistent error representation. `ProblemDetails` is the standard ASP.NET Core-friendly mechanism for many APIs and aligns with RFC 9457.

If the application uses a Result/ErrorOr-style model, translate expected application failures at the HTTP boundary:

```csharp
return result.Match(
    success => TypedResults.Ok(success.ToDto()),
    error => error.Type switch
    {
        ErrorType.NotFound => TypedResults.NotFound(),
        ErrorType.Conflict => TypedResults.Conflict(),
        _ => TypedResults.Problem(statusCode: 400, title: error.Code)
    });
```

Keep unexpected exceptions on centralized exception-handling paths. Do not catch every exception in every endpoint just to turn it into a result.

## Validation

Validate request shape and basic constraints at the boundary. Enforce business invariants in the application/domain layer.

Avoid duplicating identical rules across multiple layers. Each layer should validate the invariants it owns.

For .NET 10, evaluate built-in validation support before adding a validation dependency. If the project already uses FluentValidation or another established validator, follow that convention.

Never treat client-side validation as a security control.

## OpenAPI

Keep generated documentation aligned with actual behavior. Document meaningful descriptions, parameters, request/response schemas, auth requirements, and error responses.

OpenAPI version must match the target runtime and downstream tooling:

- .NET 10 / ASP.NET Core 10: use OpenAPI 3.1 by default.
- .NET 11 / ASP.NET Core 11: prefer OpenAPI 3.2 unless compatibility requires 3.1.
- If consumers require an older compatible version, configure it deliberately rather than silently breaking them.

For OpenAPI 3.1+, use JSON Schema semantics correctly; do not blindly carry forward OpenAPI 3.0-era `nullable` patterns.

If using `Microsoft.AspNetCore.OpenApi`, inspect the generated document after changing schema metadata or transformers.

Scalar may be used as an API UI when the project chooses it. It is presentation tooling and does not replace contract design.

## Pagination, filtering, and sorting

For collections:

- bound result sizes
- define pagination semantics explicitly
- apply stable ordering before pagination
- validate page size and filters
- allow-list sortable/filterable fields
- avoid arbitrary database expressions from clients

Offset pagination is simple and often sufficient. Keyset/cursor pagination is worth considering for large or frequently changing datasets.

## Idempotency, retries, and concurrency

For retryable operations, determine whether the operation is naturally idempotent. If not, consider an explicit idempotency mechanism.

For concurrent updates, define conflict behavior rather than silently overwriting newer state. Optimistic concurrency commonly maps to `409 Conflict` when that matches the contract.

For outbound HTTP, use managed clients and the project's configured resilience strategy. Do not add retries blindly; retries can amplify load and duplicate non-idempotent operations.

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
