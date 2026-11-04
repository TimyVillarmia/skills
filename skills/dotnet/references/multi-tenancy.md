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

// 2. Resolution middleware (e.g. from validated JWT claim)
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var tenantClaim = context.User.FindFirst("tenant_id")?.Value
                       ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (Guid.TryParse(tenantClaim, out var tenantId))
        {
            tenantContext.TenantId = tenantId;
        }

        // Fail-fast on authenticated routes missing tenant context
        if (context.User.Identity?.IsAuthenticated == true && !tenantContext.IsResolved)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Valid tenant context could not be resolved from credentials.",
                Status = StatusCodes.Status401Unauthorized
            });
            return;
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
Automatically register query filters on all entities implementing `ITenantEntity`:

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                // Configures: e => e.TenantId == tenantContext.TenantId
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(CreateTenantFilter(entityType.ClrType, tenantContext));
            }
        }
    }

    private static LambdaExpression CreateTenantFilter(Type type, ITenantContext context)
    {
        var parameter = Expression.Parameter(type, "e");
        var property = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        var tenantId = Expression.Property(Expression.Constant(context), nameof(ITenantContext.TenantId));
        var comparison = Expression.Equal(property, tenantId);
        return Expression.Lambda(comparison, parameter);
    }
}
```

### Overwriting Tenant ID on Save (`SaveChangesInterceptor`)
Prevent client tampering by automatically overwriting `TenantId` on added entities:

```csharp
public sealed class TenantSaveChangesInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null || !tenantContext.IsResolved)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        foreach (var entry in eventData.Context.ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                // Force tenant ownership from ambient context
                entry.Entity.TenantId = tenantContext.TenantId;
            }
            else if (entry.State == EntityState.Modified)
            {
                // Prevent reassignment of tenant ID
                entry.Property(x => x.TenantId).IsModified = false;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
```

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

        return configuration.GetConnectionString($"Tenant_{tenantContext.TenantId}")
            ?? configuration.GetConnectionString("DefaultConnection")!;
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
