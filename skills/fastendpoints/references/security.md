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

## 2. Permission-Based Authorization (Default)

Check **permissions**, never role names, using the canonical lowercase `<resource>:<action>` grammar defined as constants in code:

```csharp
public static class Permissions
{
    public const string OrdersRead = "orders:read";
    public const string OrdersCreate = "orders:create";
    public const string OrdersDelete = "orders:delete";
    public const string InvoicesApprove = "invoices:approve";
}
```

### Scoped RBAC via Dynamic Policy Provider (Recommended)
Avoid stuffing unbounded permission arrays into JWTs. Register the shared `PermissionPolicyProvider` (`"perm:"` prefix) + `PermissionAuthorizationHandler` backed by `IPermissionStore` + `HybridCache` (see the `dotnet` skill's `security.md` reference), and require permissions via `Policies(...)`:

```csharp
public override void Configure()
{
    Post("/orders");
    Policies($"perm:{Permissions.OrdersCreate}");
}
```

### Token-Claim Permissions (Small/Static Permission Sets Only)
If your IdP already emits bounded `permissions` claims in the access token, FastEndpoints' built-in `Permissions(...)` helper matches those claims directly:

```csharp
public override void Configure()
{
    Post("/orders");
    Permissions(Permissions.OrdersCreate);
}
```

---

## 3. Policies, Auth Schemes & Resource Ownership

Enforce policies and authentication schemes inside `Configure()`, and enforce resource ownership (BOLA/IDOR prevention) inside `HandleAsync()` using `IAuthorizationService`:

```csharp
public override void Configure()
{
    Delete("/orders/{id:guid}");

    // Require permission-backed or named ASP.NET Core authorization policies
    Policies($"perm:{Permissions.OrdersDelete}");

    // Specify authentication schemes if using multiple providers (e.g. Bearer, Cookie)
    AuthSchemes("Bearer");
}
```

- Avoid `Roles("Admin", "Manager")` in greenfield endpoints; roles should only group permissions in data, never be hard-coded in endpoints (preserve `Roles(...)` only in brownfield repos that already rely on it).
- For record-level ownership or state checks, load the entity in `HandleAsync()` and call `await authService.AuthorizeAsync(User, order, Operations.Delete)` — return `SendForbiddenAsync(ct)` when `!authResult.Succeeded`.

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
