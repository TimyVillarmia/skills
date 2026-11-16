# Endpoint Engineering in FastEndpoints

Use this reference when authoring, modifying, or refactoring FastEndpoints classes, routing, model binding, and response dispatch.

---

## 1. Endpoint Class Variants

Choose the base class that matches your request/response semantics:

| Base Class | When to Use | Typical HTTP Verb |
|---|---|---|
| `Endpoint<TRequest, TResponse>` | Standard request body and JSON response payload | `POST`, `PUT`, `PATCH`, `GET` (with query DTO) |
| `EndpointWithoutRequest<TResponse>` | No request body or route params; returns JSON | `GET` (list, status), `POST` (trigger) |
| `Endpoint<TRequest>` | Accepts request body; returns 204 No Content or empty response | `POST`, `PUT`, `DELETE` |
| `EndpointWithoutRequest` | No request body; returns 204 No Content | `DELETE`, `POST` (webhook acknowledgement) |

### Example: `EndpointWithoutRequest<TResponse>`
```csharp
public sealed class GetSystemStatusEndpoint : EndpointWithoutRequest<StatusResponse>
{
    public override void Configure()
    {
        Get("/system/status");
        AllowAnonymous();
        Summary(s => s.Summary = "Gets current health status");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendOkAsync(new StatusResponse { Status = "Healthy", ServerTime = DateTime.UtcNow }, ct);
    }
}
```

---

## 2. Route Configuration & Verb Binding

Configure routing entirely inside the `Configure()` override:

```csharp
public override void Configure()
{
    // Single verb and path
    Get("/users/{id:guid}");

    // Or multiple verbs / paths
    Verbs(Http.POST, Http.PUT);
    Routes("/users", "/users/create-or-update");

    // Route grouping
    Group<UserRouteGroup>();

    // Versioning
    Version(1);
}
```

### Route Groups
Use `Group` classes to avoid repeating route prefixes, policies, and tags across related endpoints:

```csharp
public sealed class CustomersGroup : Group
{
    public CustomersGroup()
    {
        Configure("customers", ep =>
        {
            ep.Policies("CustomerAccessPolicy");
            ep.Description(d => d.WithTags("Customers"));
        });
    }
}

// In endpoint:
public override void Configure()
{
    Get("/{id}");
    Group<CustomersGroup>(); // Effective route: /customers/{id}
}
```

---

## 3. Model Binding & Parameter Sources

FastEndpoints binds route params, query params, headers, claims, and JSON bodies automatically into request properties.

```csharp
public sealed class UpdateUserProfileRequest
{
    // 1. Bound from route template: /users/{id}
    public required Guid Id { get; init; }

    // 2. Bound from query string: ?notify=true
    [QueryParam]
    public bool Notify { get; init; }

    // 3. Bound from incoming HTTP header
    [FromHeader("X-Correlation-ID")]
    public string? CorrelationId { get; init; }

    // 4. Bound from authenticated JWT ClaimsPrincipal
    [FromClaim("sub")]
    public required string UserId { get; init; }

    // 5. Bound from JSON request body
    public required string DisplayName { get; init; }
    public string? Bio { get; init; }
}
```

### Custom Model Binding
For complex types or query collection formats (e.g. comma-separated lists), implement custom binders or parse methods:
```csharp
public static bool TryParse(string? value, out FilterOptions? result)
{
    // Custom string parsing logic
}
```

---

## 4. Response Serialization (`Send*Async`)

Always use built-in `Send*Async` helper methods. They configure proper status codes, content-type headers, and response serialization:

```csharp
// 200 OK with payload
await SendOkAsync(response, ct);

// 201 Created with Location header pointing to another endpoint
await SendCreatedAtAsync<GetUserByIdEndpoint>(
    routeValues: new { id = user.Id },
    responseBody: response,
    cancellation: ct);

// 204 No Content
await SendNoContentAsync(ct);

// 404 Not Found
await SendNotFoundAsync(ct);

// 401 Unauthorized & 403 Forbidden
await SendUnauthorizedAsync(ct);
await SendForbiddenAsync(ct);

// File Download & Streaming
await SendFileAsync(new FileInfo("reports/annual.pdf"), contentType: "application/pdf", cancellation: ct);
await SendStreamAsync(memoryStream, fileName: "export.csv", contentType: "text/csv", cancellation: ct);

// Redirects
await SendRedirectAsync("/new-dashboard", allowRemoteRedirects: false, cancellation: ct);
```

---

## 5. Domain Entity Mappers

Keep DTO mapping out of handlers by implementing `Mapper<TRequest, TResponse, TEntity>`:

```csharp
public sealed class OrderMapper : Mapper<CreateOrderRequest, CreateOrderResponse, Order>
{
    public override Order ToEntity(CreateOrderRequest r) => new()
    {
        CustomerId = r.CustomerId,
        TotalAmount = r.Amount,
        CreatedAtUtc = DateTime.UtcNow
    };

    public override CreateOrderResponse FromEntity(Order e) => new()
    {
        OrderId = e.Id.ToString(),
        CreatedAtUtc = e.CreatedAtUtc
    };
}

// In Endpoint:
public sealed class CreateOrderEndpoint : Endpoint<CreateOrderRequest, CreateOrderResponse, OrderMapper>
{
    public override async Task HandleAsync(CreateOrderRequest req, CancellationToken ct)
    {
        Order order = Map.ToEntity(req);
        await _repository.SaveAsync(order, ct);
        await SendCreatedAtAsync<GetOrderEndpoint>(new { id = order.Id }, Map.FromEntity(order), ct: ct);
    }
}
```

---

## 6. Pre-Processors & Post-Processors

Use processors to execute cross-cutting concerns (logging, audit trails, tenant validation) before or after endpoint execution:

```csharp
public sealed class AuditLoggingPostProcessor : PostProcessor<CreateOrderRequest, CreateOrderResponse>
{
    public override async Task PostProcessAsync(IPostProcessorContext<CreateOrderRequest, CreateOrderResponse> context, CancellationToken ct)
    {
        var logger = context.HttpContext.Resolve<ILogger<AuditLoggingPostProcessor>>();
        logger.LogInformation("Order created: {OrderId}", context.Response?.OrderId);
    }
}

// Attach to endpoint:
public override void Configure()
{
    Post("/orders");
    PostProcessors(new AuditLoggingPostProcessor());
}
```

---

## 7. Native AOT & Trimming Support

When targeting Native AOT:
1. Reference the source generator package:
   ```xml
   <PackageReference Include="FastEndpoints.Generator" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
   ```
2. Define a `JsonSerializerContext` covering all Request and Response DTOs:
   ```csharp
   [JsonSerializable(typeof(CreateOrderRequest))]
   [JsonSerializable(typeof(CreateOrderResponse))]
   [JsonSerializable(typeof(ProblemDetails))]
   public partial class AppJsonSerializerContext : JsonSerializerContext
   {
   }
   ```
3. Register the serializer context in `Program.cs`:
   ```csharp
   builder.Services.ConfigureHttpJsonOptions(o =>
   {
       o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
   });
   ```

---

## 8. OpenAPI Documentation & Scalar UI

For interactive documentation, prefer **Scalar** (`Scalar.AspNetCore`) over legacy Swagger UI.

### 1. Documenting Endpoints (`EndpointSummary`)
Prefer a standalone `Summary<TEndpoint>` class over bloated `Configure()` methods to preserve vertical slice readability:

```csharp
public sealed class CreateOrderSummary : Summary<CreateOrderEndpoint>
{
    public CreateOrderSummary()
    {
        Summary = "Creates a new customer order";
        Description = "Validates inventory availability, initiates payment processing, and creates order";
        Response<CreateOrderResponse>(201, "Order created successfully");
        Response<ProblemDetails>(400, "Validation failure or invalid order payload");
        Response(401, "Unauthorized client");
    }
}
```

Or configure inline within `Configure()` for simple endpoints:

```csharp
public override void Configure()
{
    Post("/orders");
    Summary(s =>
    {
        s.Summary = "Creates a new customer order";
        s.Responses[201] = "Order created successfully";
        s.Responses[400] = "Validation failure";
    });
}
```

### 2. Scalar & Native OpenAPI Pipeline (`Program.cs`)
Use `FastEndpoints.OpenApi` instead of legacy `FastEndpoints.Swagger` (NSwag). It registers with ASP.NET Core's native `Microsoft.AspNetCore.OpenApi` engine, so `app.MapOpenApi()` serves the spec and Scalar discovers it directly:

```csharp
using FastEndpoints;
using FastEndpoints.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddFastEndpoints()
    .OpenApiDocument(); // FastEndpoints.OpenApi package

var app = builder.Build();

app.UseFastEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // Serves /openapi/v1.json via framework
    app.MapScalarApiReference(); // Accessible at /scalar/v1
}

app.Run();
```
