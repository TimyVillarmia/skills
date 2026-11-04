using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

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

// Use an established keyed lock (e.g., AsyncKeyedLock) or bounded ConcurrentDictionary of Semaphores:
public sealed class KeyedLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task ExecuteAsync(string key, Func<Task> action, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            await action();
        }
        finally
        {
            semaphore.Release();
        }
    }
}

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Status = exception switch
            {
                BadHttpRequestException => StatusCodes.Status400BadRequest,
                UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status500InternalServerError
            },
            Title = exception switch
            {
                BadHttpRequestException => "Bad Request",
                UnauthorizedAccessException => "Unauthorized",
                _ => "Internal Server Error"
            },
            Detail = httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred."
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
