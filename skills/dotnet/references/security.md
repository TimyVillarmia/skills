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
- [Browser security and CORS](#browser-security-and-cors)
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
- **Policy-based authorization:** Prefer named policies (`RequireAuthorization("BillingAdmin")`) over scattered `Roles = "..."` magic strings.

Validate tokens using established framework components. Never manually implement JWT parsing/signature validation, key rotation, OAuth/OIDC protocol behavior, password hashing, or cryptography.

Authentication answers **who is the caller**. Authorization answers **what the caller may do**.

## Authorization

### Default-Deny Authorization Policy
Enforce default-deny across the entire API by configuring `FallbackPolicy`. Endpoints that should be public must explicitly declare `.AllowAnonymous()`:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Require authentication by default on all endpoints
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

### Resource-Based Authorization (BOLA / IDOR Prevention)
Role or claim checks confirm *who* the caller is, but not *whether* they own or have access to a specific record. Broken Object-Level Authorization (BOLA/IDOR) occurs when code queries an entity by ID without checking ownership.

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

Keep security middleware/order deliberate. Authentication must establish the user before authorization evaluates policies. Endpoint-specific authorization must not be bypassed by placing business logic behind a UI-only check.

When using rate limiting, anti-forgery protection, CORS, HTTPS redirection, or other middleware, inspect the target ASP.NET Core version and existing pipeline rather than copying an unrelated middleware order.

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

## Browser security and CORS

For browser applications, consider CSRF, XSS, CORS, cookie flags, content security policy, and secure transport as part of the threat model.

For cookie authentication, configure `Secure`, `HttpOnly`, and an appropriate `SameSite` policy for the deployment model.

Configure CORS with explicit allowed origins from validated configuration options. Never combine `AllowAnyOrigin()` (or wildcard origins) with `AllowCredentials()`, and never treat CORS as an authentication or authorization mechanism.

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

// Middleware order: resolve ForwardedHeaders (with trusted KnownNetworks/KnownProxies) BEFORE UseRateLimiter
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var api = app.MapGroup("/api").RequireRateLimiting("api-per-user");
api.MapGet("/orders", GetOrders);
```

> [!WARNING]
> **Reverse-proxy IP trap:** Partitioning by `HttpContext.Connection.RemoteIpAddress` without `app.UseForwardedHeaders()` (configured with explicit `KnownNetworks`/`KnownProxies`) places every user behind the load balancer's IP and causes global self-DoS.

## Dependencies

Keep dependencies minimal and maintained, but do not reject a mature security library merely because it adds a package.

For security-sensitive primitives, established implementations are preferred over custom code even when the custom version would have fewer dependencies.

Review direct and transitive dependency vulnerabilities as normal maintenance.

## Security anti-patterns

❌ Hand-rolled password hashing, JWT validation, cryptography, or OAuth/OIDC protocol code.

❌ Authorization based only on UI visibility or client-supplied roles/tenant IDs.

❌ Disabling certificate validation to “fix” local development and carrying that change into production.

❌ Logging access tokens, passwords, secrets, or sensitive personal data.

❌ Accepting arbitrary URLs without an SSRF threat model.

❌ Returning raw exception details to clients.

❌ Treating CORS as authentication.

## Threat modeling

For meaningful security-sensitive features, identify assets, trust boundaries, actors/attacker capabilities, entry points, authorization decisions, abuse cases, mitigations, and residual risk.

## Security checklist

- Authentication is appropriate and correctly validated.
- Authorization protects the actual resource/action.
- Tenant boundaries are enforced when applicable.
- Inputs are validated and constrained.
- Injection paths are safely handled.
- Outbound requests cannot create an unintended SSRF boundary.
- Deserialization and file handling are safe.
- Secrets and sensitive data are not exposed in source, responses, or telemetry.
- Transport security is preserved.
- Abuse/rate limits are appropriate.
- Security-sensitive primitives use established implementations.
- Failure behavior does not fail open.
