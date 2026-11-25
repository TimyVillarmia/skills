# Authorization & Role Management

Use this reference whenever code enforces permissions, scopes roles, prevents BOLA/IDOR, exposes admin role-management endpoints, or evaluates whether to escalate to ReBAC/PBAC.

## Contents

- [Choosing an Authorization Model](#choosing-an-authorization-model)
- [Default-Deny Authorization Policy](#default-deny-authorization-policy)
- [Standard Permission-Based RBAC (Greenfield Default)](#standard-permission-based-rbac-greenfield-default)
- [Resource-Based Authorization (BOLA / IDOR Prevention)](#resource-based-authorization-bola--idor-prevention)
- [Role Management API & Invariants](#role-management-api--invariants)
- [Authorization Test Matrix](#authorization-test-matrix)
- [Authorization Anti-Patterns & Checklist](#authorization-anti-patterns--checklist)

---

## Choosing an Authorization Model

Standardize on a two-tier decision rule for greenfield projects:

| Model | When to Use |
|---|---|
| **Tenant-scoped, permission-based RBAC + resource ownership** *(Default)* | 80–90% of SaaS, B2B, and CRUD APIs. Roles group permissions; code checks permissions and resource policies. |
| **ReBAC** (OpenFGA / SpiceDB) | Only when the domain needs nested inheritance trees (folders → docs) or arbitrary user-to-user sharing graphs. |
| **External PBAC** (OPA / Cedar) | Polyglot microservices or regulated enterprises requiring decoupled, centrally audited policy-as-code. |

---

## Default-Deny Authorization Policy

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

---

## Standard Permission-Based RBAC (Greenfield Default)

Follow these six invariants on every greenfield project so authorization stays consistent across services:

1. **Check permissions, never role names:** Endpoints and handlers check permissions (`.RequirePermission(Permissions.InvoicesApprove)`), never `RequireRole("Admin")` or `User.IsInRole(...)`. Roles exist only as human-managed bundles of permissions.
2. **Canonical permission grammar:** Use lowercase `<resource>:<action>` keys (e.g., `invoices:read`, `invoices:create`, `invoices:approve`, `roles:read`, `roles:manage`, `members:manage`), with an optional `:own` / `:any` suffix only when distinguishing self vs. tenant-wide access. Stick to a consistent verb vocabulary (`read`, `create`, `update`, `delete`, `manage`, plus explicit domain verbs) defined as constants in code.
3. **Scoped role assignments (never a `Role` column on `Users`):** Model authorization with four concepts: `Role`, code-defined `Permissions`, `RolePermission`, and `RoleAssignment` bound to a scope (`("tenant", tenantId)` or `("global", null)`).
4. **One centralized AuthZ boundary:** Use a dynamic `IAuthorizationPolicyProvider` + `PermissionAuthorizationHandler` for endpoint-level permission checks, and `IAuthorizationService` for contextual/ownership checks. Never invent fake roles (e.g., `InvoiceOwnerEditor`) to encode resource state or ownership.
5. **Cache permission lookups, don't bloat JWTs:** Resolve effective permissions per `(userId, scopeId)` through `HybridCache` using tenant-namespaced keys (`tenant:{tenantId}:perms:{userId}`, tagged with `tenant:{tenantId}:perms`; see [Multi-Tenant Cache Partitioning](multi-tenancy.md#4-multi-tenant-cache-partitioning)). Avoid embedding unbounded permission arrays in JWTs (token bloat + delayed revocation).
6. **Verify with an AuthZ test matrix:** Cover authentication, tenant isolation, permission enforcement, ownership, and role-management edge cases in `WebApplicationFactory` integration tests.

### Canonical Permission Catalog & EF Core Schema

Define permissions once in code with metadata so both endpoint guards and the admin UI's `GET /api/permissions` catalog share a single source of truth:

```csharp
public sealed record PermissionDefinition(
    string Key,
    string Resource,
    string Action,
    string Description);

public static class Permissions
{
    public const string InvoicesRead = "invoices:read";
    public const string InvoicesCreate = "invoices:create";
    public const string InvoicesApprove = "invoices:approve";
    public const string RolesRead = "roles:read";
    public const string RolesManage = "roles:manage";
    public const string MembersManage = "members:manage";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(InvoicesRead, "invoices", "read", "View invoices"),
        new(InvoicesCreate, "invoices", "create", "Create draft invoices"),
        new(InvoicesApprove, "invoices", "approve", "Approve invoices for payment"),
        new(RolesRead, "roles", "read", "View roles and permission assignments"),
        new(RolesManage, "roles", "manage", "Create, update, and delete custom roles"),
        new(MembersManage, "members", "manage", "Assign and revoke user roles")
    ];

    public static readonly IReadOnlySet<string> ValidKeys =
        All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
}

public sealed class Role
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? TenantId { get; init; }             // null for global/system templates; TenantId for tenant roles
    public required string Name { get; set; }        // Display name, e.g., "Billing Admin"
    public required string Slug { get; set; }        // e.g., "billing-admin"
    public required string ScopeType { get; set; }   // "global" | "tenant"
    public bool IsSystem { get; init; }
    public List<RolePermission> Permissions { get; init; } = [];
}

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Domain join entity for RBAC role-permission mapping.")]
public sealed class RolePermission
{
    public Guid RoleId { get; init; }
    public required string PermissionKey { get; init; } // Validated against Permissions.ValidKeys
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

### Dynamic Permission Policy Provider & Endpoint Extension

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
    ValueTask<IReadOnlySet<string>> GetPermissionsAsync(
        Guid userId,
        Guid? scopeId,
        CancellationToken ct = default);

    async ValueTask<bool> HasPermissionAsync(
        Guid userId,
        Guid? scopeId,
        string permission,
        CancellationToken ct = default) =>
        (await GetPermissionsAsync(userId, scopeId, ct)).Contains(permission);
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

---

## Resource-Based Authorization (BOLA / IDOR Prevention)

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
}).RequirePermission(Permissions.InvoicesRead);
```

### Authorization Handler Implementation

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
        var userId = context.User.FindFirst("sub")?.Value
                  ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userId, out var callerId) && resource.CustomerId == callerId)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
```

---

## Role Management API & Invariants

When a project includes an Admin Role Management UI, expose one consistent backend contract so frontends never hard-code permission lists or drift across services.

### Canonical Admin API Surface

| Endpoint | Required Permission | Purpose |
|---|---|---|
| `GET /api/permissions` | `roles:read` | Returns `Permissions.All` grouped by `resource` so the admin UI renders permission checkboxes dynamically. |
| `GET /api/roles` | `roles:read` | Lists system and custom roles visible in the active scope, including assigned permission keys and member counts. |
| `POST /api/roles` | `roles:manage` | Creates a custom role (`IsSystem = false`) in the active scope with validated `permissionKeys[]`. |
| `PUT /api/roles/{id:guid}` | `roles:manage` | Updates display name and `permissionKeys[]` for a custom role; invalidates cached permissions for the tenant. |
| `DELETE /api/roles/{id:guid}` | `roles:manage` | Deletes an unused custom role (reject if `IsSystem` or still assigned, unless explicitly reassigning). |
| `GET /api/users/{userId:guid}/roles` | `members:manage` | Lists role assignments for a user in the active tenant scope. |
| `PUT /api/users/{userId:guid}/roles` | `members:manage` | Replaces a user's role assignments in the active tenant scope; invalidates that user's permission cache. |

### Six Non-Negotiable Role Management Invariants

1. **Prevent privilege escalation:** A caller may not create/update a role with permissions they do not themselves hold, nor assign a role containing permissions outside their own effective permission set (unless they hold an explicit super-admin permission). Never allow tenant-scoped endpoints to assign `ScopeType == "global"` roles.
2. **Protect system roles:** Roles with `IsSystem == true` (e.g., `owner`, `admin`, `member`) cannot be deleted or have their `Slug` or `ScopeType` modified (`403 Forbidden` or `409 Conflict` via `ProblemDetails`).
3. **Prevent last-admin lockout:** Reject any role-assignment removal or role edit that would leave a tenant with zero users holding `roles:manage` (or the `owner` system role) (`409 Conflict`).
4. **Validate every permission key against the code catalog:** Reject unknown keys not in `Permissions.ValidKeys` with `400 Bad Request` (`ProblemDetails`)—never store arbitrary client strings.
5. **Invalidate `HybridCache` atomically on mutation:**
   - Cache per-user permission sets under key `tenant:{tenantId}:perms:{userId}` with tag `tenant:{tenantId}:perms`.
   - When a **role's permissions** change, call `await cache.RemoveByTagAsync($"tenant:{tenantId}:perms", ct)` so all affected users pick up changes immediately.
   - When a **single user's assignments** change, call `await cache.RemoveAsync($"tenant:{tenantId}:perms:{userId}", ct)`.
6. **Audit every mutation:** Persist an audit record (or structured audit event) capturing `ActorUserId`, `TenantId`, `Action` (`role.created`, `role.updated`, `role.deleted`, `member.roles_updated`), `TargetId`, `AddedPermissions`/`RemovedPermissions` (or role IDs), and `OccurredAtUtc` via `TimeProvider`.

### Reference Guard & Cache Invalidation Pattern

```csharp
public sealed record UpsertRoleRequest(string Name, IReadOnlyList<string> PermissionKeys);

// Inside UpdateRoleHandler:
var unknown = req.PermissionKeys.Where(k => !Permissions.ValidKeys.Contains(k)).ToArray();
if (unknown.Length > 0)
    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
    {
        ["permissionKeys"] = [$"Unknown permissions: {string.Join(", ", unknown)}"]
    });

var callerPermissions = await permissionStore.GetPermissionsAsync(currentUser.UserId, tenant.TenantId, ct);
if (req.PermissionKeys.Any(k => !callerPermissions.Contains(k)))
    return TypedResults.Forbid(); // No privilege escalation

if (role.IsSystem)
    return TypedResults.Problem("System roles cannot be modified.", statusCode: StatusCodes.Status409Conflict);

// Apply diff, persist audit entry + SaveChangesAsync(ct), then evict tenant permission cache:
await cache.RemoveByTagAsync($"tenant:{tenant.TenantId}:perms", ct);
```

---

## Authorization Test Matrix

Verify every protected resource with at least these four cases in `WebApplicationFactory` integration tests:

1. **Unauthenticated** → `401 Unauthorized`
2. **Wrong tenant / cross-tenant resource** → `403 Forbidden` or `404 Not Found` (matching the tenant query filter)
3. **Authenticated without required permission** → `403 Forbidden`
4. **Authenticated with required permission (and ownership when applicable)** → `200` / `201` / `204`

For Role Management endpoints, add three regression tests:
- **Privilege escalation attempt** (assigning a permission the caller lacks) → `403 Forbidden`
- **System role deletion/mutation** → `409 Conflict` / `403 Forbidden`
- **Last-admin lockout attempt** (removing the last user with `roles:manage`) → `409 Conflict`

---

## Authorization Anti-Patterns & Checklist

❌ Checking role names in endpoints or business logic (`IsInRole("Admin")`, `RequireRole("Manager")`).

❌ Storing a flat `Role` column on `Users` instead of scoped `RoleAssignment` rows.

❌ Embedding unbounded permission lists in JWTs (token bloat and stale revocation).

❌ Hard-coding permission lists in the frontend instead of serving `GET /api/permissions` from code.

❌ Allowing admins to grant permissions they do not hold, delete system roles, or remove the last tenant admin.

### Checklist

- [ ] `FallbackPolicy` enforces `RequireAuthenticatedUser()` (deny by default); public routes opt out explicitly with `.AllowAnonymous()`.
- [ ] Endpoints require `<resource>:<action>` permissions via `.RequirePermission(...)`, never role names.
- [ ] Record-level ownership/state checks use `IAuthorizationService` (BOLA/IDOR prevention).
- [ ] Permission lookups are cached in `HybridCache` with tenant-namespaced keys/tags and evicted on role or assignment changes.
- [ ] Admin Role Management enforces no-escalation, system-role protection, last-admin lockout protection, catalog validation, and audit logging.
- [ ] Integration tests cover the 4-case AuthZ matrix (plus escalation/lockout tests for Role Management).
