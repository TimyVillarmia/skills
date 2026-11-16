# Security & Governance in FastEndpoints

Use this reference when configuring endpoint authentication, authorization policies, permission sets, claims binding, rate limiting, and idempotency.

---

## 1. Authentication Defaults

FastEndpoints enforces authentication by default when JWT/Auth middleware is registered. 

To open an endpoint to public unauthenticated access, explicitly call `AllowAnonymous()`:

```csharp
public override void Configure()
{
    Get("/public/health");
    AllowAnonymous(); // Disables authentication check for this endpoint
}
```

### JWT Authentication Setup (`Program.cs`)
```csharp
using FastEndpoints.Security;

var builder = WebApplication.CreateBuilder(args);

// Register JWT authentication provider
builder.Services.AddAuthenticationJwtBearer(s =>
{
    s.SigningKey = builder.Configuration["Jwt:SigningKey"]!;
});

builder.Services.AddAuthorization();
builder.Services.AddFastEndpoints();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();

app.Run();
```

---

## 2. Role-Based & Policy-Based Authorization

Enforce access rules inside `Configure()`:

```csharp
public override void Configure()
{
    Delete("/users/{id}");

    // Require one or more roles
    Roles("Admin", "SuperUser");

    // Require specific ASP.NET Core authorization policies
    Policies("ElevatedAccessPolicy", "TenantAdmin");

    // Specify authentication schemes if using multiple providers (e.g. Bearer, Cookie)
    AuthSchemes("Bearer");
}
```

---

## 3. Permissions-Based Authorization

FastEndpoints provides fine-grained permission support. Define permissions as constants or enums:

```csharp
public static class Permissions
{
    public const string OrdersCreate = "Orders:Create";
    public const string OrdersDelete = "Orders:Delete";
    public const string OrdersRead = "Orders:Read";
}

// In endpoint:
public override void Configure()
{
    Post("/orders");
    // User must have this permission claim in their token
    Permissions(Permissions.OrdersCreate);
}
```

---

## 4. Automatic Claims-to-Property Binding

Bind claims directly from the authenticated `ClaimsPrincipal` into Request DTO properties using `[FromClaim]`:

```csharp
public sealed class CreateCommentRequest
{
    // Binds from 'sub' or ClaimTypes.NameIdentifier
    [FromClaim("sub")]
    public required string AuthorId { get; init; }

    // Binds from 'tenant_id' claim
    [FromClaim("tenant_id")]
    public required string TenantId { get; init; }

    // Binds from request JSON body
    public required string Content { get; init; }
}
```

---

## 5. Rate Limiting & Throttling

Protect endpoints against abuse using the built-in throttle mechanism or ASP.NET Core rate limiting:

```csharp
public override void Configure()
{
    Post("/auth/login");
    AllowAnonymous();

    // Limit to 5 requests per 60 seconds per client IP
    Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Forwarded-For");
}
```

---

## 6. API Versioning

FastEndpoints supports clean versioning without polluting route strings:

```csharp
public sealed class GetOrdersV1Endpoint : EndpointWithoutRequest<List<OrderDto>>
{
    public override void Configure()
    {
        Get("/orders");
        Version(1); // Final route: /v1/orders
    }
}

public sealed class GetOrdersV2Endpoint : EndpointWithoutRequest<List<OrderDtoV2>>
{
    public override void Configure()
    {
        Get("/orders");
        Version(2, deprecateAt: 3); // Final route: /v2/orders
    }
}
```
