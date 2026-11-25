# Security

Use this reference whenever code handles authentication, authorization, untrusted input, secrets, sensitive data, network boundaries, serialization, or security-sensitive infrastructure.

## Contents

- [Security principles](#security-principles)
- [Authentication](#authentication)
- [Authorization](#authorization)
- [ASP.NET Core security pipeline](#aspnet-core-security-pipeline)
- [Input and output](#input-and-output)
- [Injection](#injection)
- [SSRF and outbound requests](#ssrf-and-outbound-requests)
- [Deserialization and files](#deserialization-and-files)
- [Inbound webhooks](#inbound-webhooks)
- [Outbound webhooks](#outbound-webhooks)
- [Browser security and CORS](#browser-security-and-cors)
- [Data Protection and key persistence](#data-protection-and-key-persistence)
- [Secrets and telemetry](#secrets-and-telemetry)
- [Rate limiting and abuse](#rate-limiting-and-abuse)
- [Dependencies](#dependencies)
- [Security anti-patterns](#security-anti-patterns)
- [Threat modeling](#threat-modeling)
- [Security checklist](#security-checklist)

## Security principles

- Treat external input as untrusted.
- Prefer established .NET/framework/library security primitives over custom cryptography or protocol implementations.
- Fail closed for authentication and authorization decisions.
- Minimize privileges, data exposure, and trust boundaries.
- Make security behavior explicit and testable.
- Do not weaken security merely to simplify development or tests.

## Authentication

Choose authentication from the actual client and deployment model:

- **JWT bearer:** Stateless APIs and machine-to-machine callers.
- **Cookies (`HttpOnly`, `Secure`, `SameSite`):** Browser-oriented sessions, BFFs, and interactive Blazor apps.
- **OpenID Connect / OAuth 2.0:** Delegated or federated identity with an external IdP.
- **ASP.NET Core Identity:** Application-managed user stores (including built-in WebAuthn / passkey support in .NET 10). Never build custom password hashing or credential tables.
- **API keys:** Only when their operational/security properties are acceptable (hash stored keys like passwords, scope permissions, support rotation).

### Token validation and identity rules

- **Use `JsonWebTokenHandler`:** Prefer `JsonWebTokenHandler` (`Microsoft.IdentityModel.JsonWebTokens`) over legacy `JwtSecurityTokenHandler` for custom token validation.
- **Keep validation strict:** Never disable `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, or `ValidateIssuerSigningKey` in production. Keep a small `TokenValidationParameters.ClockSkew` (e.g., 30–60 seconds rather than `TimeSpan.Zero`) to tolerate minor NTP drift across distributed nodes.
- **Signing keys:** Prefer an external IdP or asymmetric signing (`RS256`/`ES256` via JWKS) over self-issued `HS256` tokens with a shared symmetric secret across services.
- **Current user abstraction:** Read claims through a small scoped `ICurrentUser` wrapper over `ClaimsPrincipal` / `IHttpContextAccessor` rather than scattering raw `FindFirst(...)` lookups. Never inject `IConfiguration` into token/auth services; bind validated `IOptions<T>` instead.
- **Permission-based authorization:** Check permissions via `.RequirePermission(...)` / permission-backed policies rather than scattered `Roles = "..."` or `IsInRole(...)` magic strings.

Validate tokens using established framework components. Never manually implement JWT parsing/signature validation, key rotation, OAuth/OIDC protocol behavior, password hashing, or cryptography.

### JWT Bearer, OIDC & Claims Mapping

Configure JWT Bearer authentication against the IdP's OIDC metadata (`Authority`) and disable legacy Microsoft XML claim-type remapping (`MapInboundClaims = false`) so standard JWT claims (`sub`, `role`, `scp`, `email`) stay intact:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authOptions.Authority; // Enables OIDC discovery + JWKS key rotation
        options.Audience = authOptions.Audience;
        options.MapInboundClaims = false; // Preserve 'sub', 'role', 'scp' instead of WCF schema URIs
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
```

- **OIDC web/BFF flows:** Use authorization code flow with PKCE (`ResponseType = OpenIdConnectResponseType.Code`, `UsePkce = true`), `MapInboundClaims = false`, and `SaveTokens = false` unless tokens are required server-side (in which case persist tokens in a server-side `ITicketStore` rather than bloating browser cookies).
- **Multiple authentication schemes:** Use `AddPolicyScheme` with `ForwardDefaultSelector` to route incoming requests between schemes (e.g., Cookies vs JWT Bearer, or multiple IdPs) cleanly.

### API Endpoint Authentication Behavior (.NET 10)

In ASP.NET Core 10, cookie authentication automatically returns `401 Unauthorized` and `403 Forbidden` instead of `302` login/access-denied redirects for known API endpoints (tagged via `IDisableCookieRedirectMetadata`, applied automatically to `[ApiController]`, JSON Minimal APIs, `TypedResults` endpoints, and SignalR). Opt a specific endpoint back into cookie redirects only when intentional via `IAllowCookieRedirectMetadata`.

Authentication answers **who is the caller**. Authorization answers **what the caller may do**.

## Authorization

### Choosing an Authorization Model

Standardize on a two-tier decision rule for greenfield projects:

| Model | When to Use |
|---|---|
| **Tenant-scoped, permission-based RBAC + resource ownership** *(Default)* | 80–90% of SaaS, B2B, and CRUD APIs. Roles group permissions; code checks permissions and resource policies. |
| **ReBAC** (OpenFGA / SpiceDB) | Only when the domain needs nested inheritance trees (folders → docs) or arbitrary user-to-user sharing graphs. |
| **External PBAC** (OPA / Cedar) | Polyglot microservices or regulated enterprises requiring decoupled, centrally audited policy-as-code. |

### Default-Deny Authorization Policy
Enforce default-deny across the entire API by configuring `FallbackPolicy`. Endpoints that should be public (such as `/healthz/live`, `/healthz/ready`, or development OpenAPI/Scalar routes) must explicitly declare `.AllowAnonymous()`:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Require authentication by default on all endpoints
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

### Standard Permission-Based RBAC (Greenfield Default)

Follow these six invariants on every greenfield project so authorization stays consistent across services:

1. **Check permissions, never role names:** Endpoints and handlers check permissions (`.RequirePermission(Permissions.InvoicesApprove)`), never `RequireRole("Admin")` or `User.IsInRole(...)`. Roles exist only as human-managed bundles of permissions.
2. **Canonical permission grammar:** Use lowercase `<resource>:<action>` keys (e.g., `invoices:read`, `invoices:create`, `invoices:approve`, `members:invite`), with an optional `:own` / `:any` suffix only when distinguishing self vs. tenant-wide access. Stick to a consistent verb vocabulary (`read`, `create`, `update`, `delete`, `manage`, plus explicit domain verbs) defined as constants in code.
3. **Scoped role assignments (never a `Role` column on `Users`):** Model authorization with four concepts: `Role`, code-defined `Permissions`, `RolePermission`, and `RoleAssignment` bound to a scope (`("tenant", tenantId)` or `("global", null)`).
4. **One centralized AuthZ boundary:** Use a dynamic `IAuthorizationPolicyProvider` + `PermissionAuthorizationHandler` for endpoint-level permission checks, and `IAuthorizationService` for contextual/ownership checks. Never invent fake roles (e.g., `InvoiceOwnerEditor`) to encode resource state or ownership.
5. **Cache permission lookups, don't bloat JWTs:** Resolve effective permissions per `(userId, scopeId)` through `HybridCache` using tenant-namespaced keys (`tenant:{tenantId}:perms:{userId}`, see [Multi-Tenant Cache Partitioning](multi-tenancy.md#4-multi-tenant-cache-partitioning)) and invalidate on role or assignment changes. Avoid embedding unbounded permission arrays in JWTs (token bloat + delayed revocation).
6. **Verify with a 4-case AuthZ test matrix:** For every protected resource, test: unauthenticated (`401`), wrong tenant (`403`/`404`), authenticated without permission (`403`), and authenticated with permission (`200`/`201`/`204`).

#### Canonical Permission Catalog & EF Core Schema

```csharp
public static class Permissions
{
    public const string InvoicesRead = "invoices:read";
    public const string InvoicesCreate = "invoices:create";
    public const string InvoicesApprove = "invoices:approve";
    public const string MembersInvite = "members:invite";
}

public sealed class Role
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Slug { get; set; }        // e.g., "billing-admin"
    public required string ScopeType { get; set; }   // "global" | "tenant"
    public bool IsSystem { get; init; }
    public List<RolePermission> Permissions { get; init; } = [];
}

public sealed class RolePermission
{
    public Guid RoleId { get; init; }
    public required string PermissionKey { get; init; } // FK/validated against Permissions catalog
}

public sealed class RoleAssignment
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
    public required string ScopeType { get; init; }  // "global" | "tenant"
    public Guid? ScopeId { get; init; }              // null for global; TenantId for tenant
}
```

#### Dynamic Permission Policy Provider & Endpoint Extension

Use a single `IAuthorizationPolicyProvider` with a `"perm:"` prefix so every permission works automatically without manually registering policies in `Program.cs`:

```csharp
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return base.GetPolicyAsync(policyName);
    }
}

public interface IPermissionStore
{
    ValueTask<bool> HasPermissionAsync(
        Guid userId,
        Guid? scopeId,
        string permission,
        CancellationToken ct = default);
}

public sealed class PermissionAuthorizationHandler(
    IPermissionStore permissionStore,
    ITenantContext tenantContext) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var sub = context.User.FindFirst("sub")?.Value
               ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(sub, out var userId))
            return;

        Guid? scopeId = tenantContext.IsResolved ? tenantContext.TenantId : null;
        if (await permissionStore.HasPermissionAsync(userId, scopeId, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization($"{PermissionPolicyProvider.Prefix}{permission}");
}

// DI registration:
// builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
// builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Endpoint usage:
// invoices.MapPost("/{id:guid}/approve", ApproveInvoice)
//     .RequirePermission(Permissions.InvoicesApprove);
```

### Resource-Based Authorization (BOLA / IDOR Prevention)
Endpoint permission checks confirm *capability*, not *whether* the caller owns or may act on a specific record (or whether its state permits the action). Broken Object-Level Authorization (BOLA/IDOR) occurs when code queries an entity by ID without checking ownership or contextual rules.

Enforce access at the resource boundary using `IAuthorizationService`:

```csharp
app.MapGet("/orders/{id:guid}", async Task<Results<Ok<OrderResponse>, NotFound, ForbidHttpResult>> (
    Guid id,
    OrderDbContext db,
    IAuthorizationService authService,
    ClaimsPrincipal user,
    CancellationToken ct) =>
{
    var order = await db.Orders.FindAsync([id], ct);
    if (order is null)
    {
        return TypedResults.NotFound();
    }

    var authResult = await authService.AuthorizeAsync(user, order, Operations.Read);
    if (!authResult.Succeeded)
    {
        return TypedResults.Forbid();
    }

    return TypedResults.Ok(OrderResponse.From(order));
});
```

#### Authorization Handler Implementation
```csharp
public static class Operations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
}

public sealed class OrderAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, Order>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Order resource)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userId, out var callerId) && resource.CustomerId == callerId)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
```

### Mass-Assignment & Over-Posting Prevention
Never bind public HTTP request bodies directly to EF Core entities or domain models. Malicious clients can overwrite sensitive internal properties (e.g. `IsAdmin`, `AccountBalance`, `TenantId`).

Always accept dedicated, flat request DTOs and explicitly copy allowed mutable fields:

```csharp
// ❌ VULNERABLE: Direct entity binding permits overwriting arbitrary database columns
app.MapPut("/users/{id}", async (Guid id, User user, UserDbContext db) => { ... });

// ✅ SECURE: Explicit request contract determines allowed mutations
public sealed record UpdateUserRequest(string DisplayName, string PhoneNumber);

app.MapPut("/users/{id:guid}", async Task<Results<NoContent, NotFound>> (
    Guid id,
    UpdateUserRequest request,
    UserDbContext db,
    CancellationToken ct) =>
{
    var user = await db.Users.FindAsync([id], ct);
    if (user is null) return TypedResults.NotFound();

    // Explicit field assignment
    user.DisplayName = request.DisplayName;
    user.PhoneNumber = request.PhoneNumber;

    await db.SaveChangesAsync(ct);
    return TypedResults.NoContent();
});
```

---

## ASP.NET Core security pipeline

Keep security middleware order deliberate—see the canonical pipeline in [Middleware Pipeline and Custom Middleware](api.md#middleware-pipeline-and-custom-middleware). Authentication must establish the user before authorization evaluates policies, and `UseRateLimiter()` / `UseOutputCache()` / `UseAntiforgery()` must sit in their required positions.

- **HTTPS / HSTS behind reverse proxies:** When TLS terminates at a reverse proxy, ingress, or load balancer, configure `UseForwardedHeaders()` (with explicit `KnownIPNetworks` / `KnownProxies`) and enforce HTTPS/HSTS at the edge rather than blindly calling `UseHttpsRedirection()` inside HTTP-only containers (port `8080`), which causes redirect loops or missing-HTTPS-port warnings.

CORS is not an authentication mechanism.

## Input and output

Validate shape and basic constraints at the boundary; enforce business invariants in the appropriate application/domain layer.

Avoid mass assignment and over-posting. Explicit request models should determine which fields a client may change.

Do not expose internal entities, stack traces, database details, secrets, tokens, or sensitive fields through API responses.

## Injection

Use parameterized APIs and framework abstractions for database access. Never concatenate untrusted values into SQL, shell commands, LDAP queries, or other interpreters.

Validate or constrain dynamic identifiers such as column names, file paths, hostnames, and URLs rather than treating them as ordinary values.

## SSRF and outbound requests

Treat user-controlled URLs and network destinations as security-sensitive. Validate allowed schemes and constrain hosts/domains where appropriate. Prevent access to internal/private network ranges when the threat model requires it. Limit redirects, timeouts, and response sizes as appropriate.

Do not assume a URL is safe merely because it uses HTTPS.

## Deserialization and files

Use safe, typed serialization formats and trusted framework configuration. Do not enable unsafe polymorphic deserialization for attacker-controlled type metadata without a clear security model.

Treat uploaded documents, images, archives, and other files as untrusted input. Apply size limits, content/extension validation, isolated storage, and safe generated names.

Never combine untrusted path fragments with filesystem paths without containment validation.

## Inbound webhooks

Verify inbound webhook authenticity using the **raw request body bytes** before JSON deserialization—re-serializing a parsed DTO alters whitespace/property ordering and breaks signature verification:

1. Enable buffering or read raw bytes from `HttpContext.Request.Body` before model binding.
2. Compute `HMACSHA256.HashData(secretBytes, rawBodyBytes)` and decode the caller's hex/base64 signature header into bytes.
3. Compare with `CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes)` to prevent timing attacks.
4. Enforce a bounded timestamp tolerance window (e.g., ±5 minutes via `TimeProvider`) and record the unique event ID / nonce in a short-lived deduplication table or cache for replay protection.

## Outbound webhooks

When dispatching webhooks via `HttpClient` (typically from a Transactional Outbox worker):

- **Sign `{timestamp}.{payload}` with HMAC-SHA256:** Include `X-*-Timestamp` (Unix seconds), `X-*-Signature` (`sha256=<hex>`), `X-*-Event`, and a stable `X-*-Delivery` ID (the outbox message ID) so receivers can verify authenticity, enforce replay windows, and deduplicate retries.
- **Validate `EndpointUrl` beyond `[Url]`:** The `[Url]` attribute accepts URL fragments (`#...`, such as browser SPA `#!...` view URLs) and non-HTTP schemes. Reject URLs with non-empty `Uri.Fragment` (HTTP clients strip fragments before sending), require `https` outside local development, and apply SSRF protections (block private/loopback/link-local IPs via `SocketsHttpHandler.ConnectCallback`) for tenant-configurable URLs.

## Browser security and CORS

For browser applications, consider CSRF, XSS, CORS, cookie flags, content security policy, and secure transport as part of the threat model.

For cookie authentication, configure `Secure`, `HttpOnly`, and an appropriate `SameSite` policy for the deployment model.

Configure CORS with explicit allowed origins from validated configuration options. Never combine `AllowAnyOrigin()` (or wildcard origins) with `AllowCredentials()`, and never treat CORS as an authentication or authorization mechanism.

## Data Protection and key persistence

ASP.NET Core Data Protection encrypts authentication cookies, antiforgery tokens, and ASP.NET Core Identity tokens. Ephemeral local container keys invalidate sessions and antiforgery tokens on every restart or across load-balanced replicas.

Persist keys to a shared store and encrypt them at rest in multi-instance or containerized deployments:

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("orders-api") // Must match across all replicas
    .PersistKeysToDbContext<AppDbContext>() // Or PersistKeysToStackExchangeRedis / Azure Blob / shared volume
    .ProtectKeysWithAzureKeyVault(keyIdentifier, credential); // Or ProtectKeysWithCertificate(...)
```

- **Consistent `SetApplicationName`:** Always set `SetApplicationName(...)` explicitly so keys are not isolated by container content-root path.
- **Encrypt keys at rest:** Protect persisted XML keys with a cloud KMS (Azure Key Vault / AWS KMS), certificate, or DPAPI; never store unencrypted key rings in shared storage.
- **Right tool for the job:** Do not use `IDataProtector` for password hashing or permanent unrotated database column encryption.

## Secrets and telemetry

Do not commit credentials, private keys, access tokens, or connection secrets. Use the environment's supported secret-management mechanism.

Review logs, traces, exceptions, metrics, and telemetry for accidental sensitive-data disclosure.

## Rate limiting and abuse

Rate limiting is useful for endpoints vulnerable to high request volume, authentication abuse, expensive computation, enumeration, or downstream fan-out. Combine it with authentication, authorization, validation, quotas, and request body size limits (see [File uploads, streaming, and request size limits](api.md#file-uploads-streaming-and-request-size-limits)).

For .NET 10, use the built-in ASP.NET Core `AddRateLimiter` middleware (`Microsoft.AspNetCore.RateLimiting`) partitioned by authenticated tenant/user ID or client IP:

- **Token bucket:** Smooths bursty legitimate traffic while bounding average rate.
- **Sliding window:** Enforces steady request rates without fixed-window boundary spikes.
- **Concurrency limiter:** Protects scarce downstream resources (DB connections, PDF/AI workers) by capping simultaneous in-flight requests.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            ctx.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }
        await ctx.HttpContext.Response.WriteAsync("Too many requests.", ct);
    };

    options.AddPolicy("api-per-user", httpContext =>
    {
        var key = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 60,
            TokensPerPeriod = 20,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 0
        });
    });
});

// Middleware order: resolve ForwardedHeaders (with trusted KnownIPNetworks in .NET 10+ / KnownNetworks in .NET 8-9 and KnownProxies)
// BEFORE UseRateLimiter, and place UseRateLimiter after UseAuthentication but before UseAuthorization
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

var api = app.MapGroup("/api").RequireRateLimiting("api-per-user");
api.MapGet("/orders", GetOrders);
```

> [!WARNING]
> **Reverse-proxy IP trap:** Partitioning by `HttpContext.Connection.RemoteIpAddress` without `app.UseForwardedHeaders()` (configured with explicit `KnownIPNetworks` in .NET 10+ or `KnownNetworks` in .NET 8–9, plus `KnownProxies`) places every user behind the load balancer's IP and causes global self-DoS.

## Dependencies

Keep dependencies minimal and maintained, but do not reject a mature security library merely because it adds a package.

For security-sensitive primitives, established implementations are preferred over custom code even when the custom version would have fewer dependencies.

Review direct and transitive dependency vulnerabilities as normal maintenance.

## Security anti-patterns

❌ Hand-rolled password hashing, JWT validation, cryptography, or OAuth/OIDC protocol code.

❌ Authorization based only on UI visibility or client-supplied roles/tenant IDs.

❌ Checking role names in endpoints or business logic (`IsInRole("Admin")`, `RequireRole("Manager")`), storing a flat `Role` column on `Users`, or embedding unbounded permission lists in JWTs.

❌ Disabling certificate validation to “fix” local development and carrying that change into production.

❌ Logging access tokens, passwords, secrets, or sensitive personal data.

❌ Accepting arbitrary URLs without an SSRF threat model.

❌ Returning raw exception details to clients.

❌ Treating CORS as authentication.

❌ Leaving ephemeral local Data Protection keys in multi-replica or containerized apps that use cookies, antiforgery, or ASP.NET Core Identity.

## Threat modeling

For meaningful security-sensitive features, identify assets, trust boundaries, actors/attacker capabilities, entry points, authorization decisions, abuse cases, mitigations, and residual risk.

## Security checklist

- Authentication is appropriate and correctly validated.
- Authorization checks permissions (not role names) with scoped role assignments, protects the actual resource/action, and is verified by the 4-case AuthZ test matrix (`401`, cross-tenant, missing permission `403`, allowed).
- Tenant boundaries are enforced when applicable.
- Inputs are validated and constrained.
- Injection paths are safely handled.
- Outbound requests cannot create an unintended SSRF boundary.
- Deserialization and file handling are safe.
- Secrets and sensitive data are not exposed in source, responses, or telemetry.
- Transport security is preserved.
- Data Protection keys are persisted and encrypted at rest in multi-replica or containerized deployments (when using cookies, antiforgery, or Identity).
- Abuse/rate limits are appropriate.
- Security-sensitive primitives use established implementations.
- Failure behavior does not fail open.
