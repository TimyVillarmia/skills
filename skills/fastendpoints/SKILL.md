---
name: fastendpoints
description: Build, modify, refactor, or test APIs using FastEndpoints in ASP.NET Core (.NET 8/9/10). Covers REPR pattern (Request-Endpoint-Response), Endpoint<TRequest, TResponse>, EndpointWithoutRequest, FluentValidation auto-validation, permissions/policies, FastEndpoints.Testing AppFixture, or projects referencing FastEndpoints NuGet packages. Trigger whenever FastEndpoints is mentioned, when creating class-per-endpoint APIs, or when FastEndpoints packages are present in project dependencies.
license: MIT
metadata:
  author: Timy
  version: "1.0"
---

# FastEndpoints Engineering

Use this skill when building, modifying, refactoring, or testing HTTP APIs using **FastEndpoints** in modern .NET (.NET 8, 9, 10).

## Official References

- Documentation: https://fast-endpoints.com/
- Getting Started: https://fast-endpoints.com/docs/get-started
- GitHub Repository: https://github.com/FastEndpoints/FastEndpoints
- API Documentation: https://fast-endpoints.com/docs/openapi-documents
- Testing: https://fast-endpoints.com/docs/integration-unit-testing

---

## Core Philosophy: The REPR Pattern

FastEndpoints replaces traditional controller actions and inline Minimal API lambdas with the **REPR (Request-Endpoint-Response)** pattern:

- **Class-per-endpoint**: Each HTTP endpoint is a single, isolated C# class.
- **Vertical slice cohesion**: Group `Request.cs`, `Response.cs`, `Endpoint.cs`, and `Validator.cs` together in one folder per feature.
- **No Fat Controllers**: Eliminate sprawling controller classes and merge conflicts.
- **No MediatR ceremony**: The endpoint itself acts as the handler, dispatching directly to domain models or calling an in-process command.

```text
Features/
└── Orders/
    └── CreateOrder/
        ├── CreateOrderEndpoint.cs
        ├── CreateOrderRequest.cs
        ├── CreateOrderResponse.cs
        └── CreateOrderValidator.cs
```

---

## Task Classification & Reference Routing

| Task Area | Reference File | When to Read |
|---|---|---|
| Endpoint structure, verbs, route params, model binding, Send* responses, Native AOT | `references/endpoint.md` | Writing or editing endpoint classes, route groups, binders, file streaming |
| Request validation, FluentValidation, auto 400 ProblemDetails, custom errors | `references/validation.md` | Adding or updating validators, error codes, in-handler validation |
| Authentication, JWT claims binding, policies, permissions, throttling | `references/security.md` | Securing endpoints, permission checks, rate limiting, idempotency |
| In-process CommandBus, EventBus, JobQueues, Server-Sent Events (SSE) | `references/messaging.md` | Using FastEndpoints messaging, pub/sub events, background queues, SSE |
| Integration tests, `AppFixture`, `TestClass`, typed HTTP clients | `references/testing.md` | Designing, writing, repairing, or running tests with `FastEndpoints.Testing` |
| EF Core persistence, general C#, Docker, CI/CD | `dotnet` skill | Database migrations, DbContext queries, solution wiring, general .NET tasks |

Load only the reference file needed for the immediate task.

---

## Quickstart & Boilerplate

### 1. Project Registration (`Program.cs`)

```csharp
using FastEndpoints;
using FastEndpoints.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Register FastEndpoints services and native ASP.NET Core OpenAPI generator
builder.Services
    .AddFastEndpoints()
    .OpenApiDocument(); // FastEndpoints.OpenApi package (built on Microsoft.AspNetCore.OpenApi)

var app = builder.Build();

// Enable FastEndpoints middleware
app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = "api";
    c.Errors.UseProblemDetails();
});

// Native OpenAPI JSON and Scalar API Reference in Development
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // Serves /openapi/v1.json via Microsoft.AspNetCore.OpenApi
    app.MapScalarApiReference(); // Accessible at /scalar/v1
}

app.Run();
```

---

### 2. Standard Endpoint Anatomy

```csharp
using FastEndpoints;

namespace MyApp.Features.Orders.CreateOrder;

public sealed class CreateOrderRequest
{
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
}

public sealed class CreateOrderResponse
{
    public required string OrderId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

public sealed class CreateOrderEndpoint : Endpoint<CreateOrderRequest, CreateOrderResponse>
{
    public override void Configure()
    {
        Post("/orders");
        AllowAnonymous(); // Or Policies("RequireAdmin"), Roles("Manager"), etc.
        Summary(s =>
        {
            s.Summary = "Creates a new customer order";
            s.Description = "Validates request, persists order, and returns generated order identifier.";
            s.Responses[201] = "Order created successfully";
            s.Responses[400] = "Validation failure";
        });
    }

    public override async Task HandleAsync(CreateOrderRequest req, CancellationToken ct)
    {
        // Business logic or service dispatch
        var orderId = Guid.NewGuid().ToString();

        var response = new CreateOrderResponse
        {
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow
        };

        await SendCreatedAtAsync<GetOrderByIdEndpoint>(
            routeValues: new { id = orderId },
            responseBody: response,
            cancellation: ct);
    }
}
```

---

## Engineering Rules for FastEndpoints

1. **Keep `Configure()` Pure**: Use `Configure()` strictly for metadata, route definitions, HTTP verbs, security requirements, and OpenAPI documentation. Never perform I/O or resolve dependencies inside `Configure()`.
2. **Prefer Explicit DTOs**: Avoid accepting domain entities directly in `HandleAsync()`. Use strongly-typed request and response records/classes.
3. **Automatic Validation**: Subclass `Validator<TRequest>`. FastEndpoints automatically scans and registers validators in the assembly. Never manually invoke validator instances unless doing complex conditional validation.
4. **Prefer Built-in `Send*Async` Over Manual `HttpContext.Response`**: Use `SendOkAsync()`, `SendCreatedAtAsync()`, `SendNotFoundAsync()`, `SendNoContentAsync()`, etc., for standard HTTP status code and response payload generation.
5. **Scoped Dependency Resolution**: Inject services via constructor injection or use `Resolve<T>()` inside `HandleAsync()`. Do not resolve scoped services in singleton processors.
6. **Native AOT & Trimming**: When targeting Native AOT, annotate request and response DTOs with `[JsonSerializable]` contexts and enable source generation via `FastEndpoints.Generator`.

---

## Validation Summary Contract

When verifying FastEndpoints changes, run native CLI commands:
```bash
dotnet build --configuration Release /p:TreatWarningsAsErrors=true
dotnet test --configuration Release --no-build
```

Report findings following the standard validation contract:
- **Build**: Exit code, warning count, AOT trim warnings.
- **Tests**: Test framework (`FastEndpoints.Testing`), passed/failed count, execution time.
- **Endpoints**: Newly added routes, verbs, and security configurations.
