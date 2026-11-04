# Multi-Tenancy & Tenant Isolation Engineering

Use this reference when building multi-tenant SaaS applications, isolating tenant data, resolving tenant context, or preventing cross-tenant data leakage in modern .NET.

---

## 1. Tenant Resolution & Ambient Context

Never trust client-supplied tenant IDs in request bodies. Resolve the tenant identity at the HTTP boundary from verified authentication claims or trusted headers:

```csharp
// 1. Scoped ambient tenant context
public interface ITenantContext
{
    Guid TenantId { get; }
    string Name { get; }
    bool IsResolved { get; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsResolved => TenantId != Guid.Empty;
}

// 2. Resolution middleware with tenant authorization
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        // For authenticated requests, resolve and authorize against user claims
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var allowedTenants = context.User.FindAll("tenant_id")
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Optional header allows switching between explicitly authorized tenants
            var requestedTenant = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                               ?? context.User.FindFirst("tenant_id")?.Value;

            if (string.IsNullOrEmpty(requestedTenant) ||
                !allowedTenants.Contains(requestedTenant) ||
                !Guid.TryParse(requestedTenant, out var tenantId))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = "User is not authorized for the requested tenant.",
                    Status = StatusCodes.Status403Forbidden
                });
                return;
            }

            tenantContext.TenantId = tenantId;
        }

        await next(context);
    }
}
```

---

## 2. Automated EF Core Isolation (Shared Database Model)

Do not rely on developers manually adding `.Where(x => x.TenantId == currentTenant)` to every query. Enforce isolation automatically at the DbContext level:

### Marker Interface
```csharp
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public sealed class Customer : ITenantEntity
{
    public Guid Id { get; init; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
}
```

### Global Query Filters (Model Builder)
Automatically register query filters on all entities implementing `ITenantEntity`.

> [!CAUTION]
> **Never use `Expression.Constant(tenantContext)` in query filter expressions.**
> EF Core caches the compiled `IModel`. Capturing an external object via `Expression.Constant` bakes the first tenant's ID into the model cache permanently, causing critical cross-tenant data leaks.
> Instead, reference a property on the `DbContext` instance itself (`CurrentTenantId`). EF Core dynamically parameterizes `DbContext` member accesses per query execution.

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    // Property on the DbContext instance evaluated dynamically per query
    public Guid CurrentTenantId => tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var entity = modelBuilder.Entity(entityType.ClrType);
                // Include the original tenant in tracked UPDATE/DELETE predicates.
                entity.Property<Guid>(nameof(ITenantEntity.TenantId)).IsConcurrencyToken();
                // Configures: e => e.TenantId == this.CurrentTenantId
                entity.HasQueryFilter(CreateTenantFilter(entityType.ClrType));
            }
        }
    }

    private LambdaExpression CreateTenantFilter(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var property = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        // References 'this.CurrentTenantId' on the executing DbContext instance
        var contextProperty = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
        var comparison = Expression.Equal(property, contextProperty);
        return Expression.Lambda(comparison, parameter);
    }
}
```

### Enforcing Tenant Boundaries on Save (`SaveChangesInterceptor`)
Use the interceptor together with the `TenantId` concurrency-token configuration above. An attached object's tenant value is not proof of persisted ownership. For updates and deletes, set the original tenant value from the authorized context so EF includes it in the SQL predicate. A foreign row then affects zero rows and raises `DbUpdateConcurrencyException`.

```csharp
public sealed class TenantSaveChangesInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        EnforceTenantBoundary(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnforceTenantBoundary(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnforceTenantBoundary(DbContext? context)
    {
        if (context is null)
            return;

        var tenantEntries = context.ChangeTracker.Entries<ITenantEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (tenantEntries.Count == 0)
            return;

        // Fail-closed: Never allow saving tenant entities without a resolved tenant context
        if (!tenantContext.IsResolved)
        {
            throw new InvalidOperationException(
                "Cannot persist tenant-scoped entities: TenantContext is unresolved. Failing closed.");
        }

        foreach (var entry in tenantEntries)
        {
            if (entry.State == EntityState.Added)
            {
                // Force tenant ownership from ambient context
                entry.Entity.TenantId = tenantContext.TenantId;
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.Entity.TenantId != tenantContext.TenantId)
                {
                    throw new InvalidOperationException("Tenant ownership cannot be changed.");
                }

                var tenantProperty = entry.Property(x => x.TenantId);
                tenantProperty.OriginalValue = tenantContext.TenantId;
                tenantProperty.IsModified = false;
            }
        }
    }
}
```

Register this interceptor on every tenant-scoped context. `ExecuteUpdate`, `ExecuteDelete`, and raw SQL bypass save interceptors; each such path must enforce its own authorized tenant predicate. Handle zero-row concurrency failures without retrying outside that boundary.

Enforce matching tenants on tenant-owned relationships so database cascades or triggers cannot affect another tenant's untracked rows.

### Auditing `IgnoreQueryFilters()`
❌ **Never use `.IgnoreQueryFilters()` in normal HTTP endpoints.**
Only platform-level administrative tasks (system-wide metrics, tenant deprovisioning) may bypass filters. Every usage of `IgnoreQueryFilters()` must be explicitly audited and commented.

---

## 3. Database-per-Tenant Model (Dedicated Database Isolation)

For enterprise compliance requiring physical data sovereignty, resolve connection strings dynamically per tenant:

```csharp
public sealed class TenantConnectionFactory(ITenantContext tenantContext, IConfiguration configuration)
{
    public string GetConnectionString()
    {
        if (!tenantContext.IsResolved)
            throw new InvalidOperationException("Cannot resolve database connection without tenant context.");

        var connectionString = configuration.GetConnectionString($"Tenant_{tenantContext.TenantId}");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail-closed: Never silently fall back to a shared default database
            throw new InvalidOperationException(
                $"No database connection configured for tenant '{tenantContext.TenantId}'. Failing closed.");
        }

        return connectionString;
    }
}
```

---

## 4. Multi-Tenant Cache Partitioning

❌ **Never use global cache keys in a multi-tenant application.**
If Tenant A and Tenant B both request `cache:user:101`, without tenant namespacing, Tenant B will receive Tenant A's cached personal data.

### Standard Tenant Cache Key Pattern:
```csharp
// Format: tenant:{tenantId}:{resource}:{id}
public static string GetUserCacheKey(Guid tenantId, Guid userId) =>
    $"tenant:{tenantId}:user:{userId}";

// Usage with HybridCache:
var key = GetUserCacheKey(tenantContext.TenantId, userId);
var user = await hybridCache.GetOrCreateAsync(key, async ct => await _queries.GetUserAsync(userId, ct));
```

---

## 5. Tenant Propagation in Background Work & Messaging

When dispatching background tasks (`Channel<T>`) or messages (MassTransit, RabbitMQ), propagate `TenantId` in message headers. Restore the ambient `ITenantContext` inside the consumer scope before executing business logic:

```csharp
public async Task Consume(ConsumeContext<GenerateReportEvent> context)
{
    var tenantContext = context.GetPayload<TenantContext>();
    tenantContext.TenantId = context.Message.TenantId;

    // Now all DbContext queries and cache keys are automatically isolated to this tenant
    await _reportService.GenerateAsync(context.CancellationToken);
}
```

---

## 6. Multi-Tenancy Review Checklist

- [ ] Is `TenantId` resolved from verified JWT claims or authenticated headers, never raw request bodies?
- [ ] Do all tenant-scoped database entities implement `ITenantEntity` with automatic EF Core Global Query Filters?
- [ ] Does a `SaveChangesInterceptor` prevent tenant ID spoofing and modification?
- [ ] Is every usage of `.IgnoreQueryFilters()` restricted to system administrative tools?
- [ ] Are all in-memory and distributed cache keys namespaced with `tenant:{tenantId}:`?
- [ ] Do asynchronous queue messages carry `TenantId` and restore `ITenantContext` in consumer scopes?
